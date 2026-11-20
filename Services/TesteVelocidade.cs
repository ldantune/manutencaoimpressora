using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Teste de velocidade usando os servidores públicos de teste da
//  Cloudflare (speed.cloudflare.com):
//   - ping: tempo de resposta de pedidos vazios (mediana) e a variação (jitter)
//   - download/upload: 4 conexões ao mesmo tempo por ~10 s; o primeiro
//     1,5 s é descartado (a conexão ainda está "acelerando")
// ---------------------------------------------------------------
public sealed class TesteVelocidade
{
    public enum Fase { Ping, Download, Upload }
    public record Resultado(double DownloadMbps, double UploadMbps, double PingMs, double JitterMs);

    const string Servidor = "https://speed.cloudflare.com";
    const int Conexoes = 4;
    const double SegundosAquecimento = 1.5;
    const double SegundosMedicao = 8;

    static readonly HttpClient Http = CriarCliente();
    static readonly byte[] DadosUpload = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);

    long _bytes;

    static HttpClient CriarCliente()
    {
        var cliente = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Conexoes * 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.None
        })
        { Timeout = Timeout.InfiniteTimeSpan };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("L2Utilitarios/1.0");
        return cliente;
    }

    public async Task<Resultado> ExecutarAsync(IProgress<(Fase Fase, double Valor, double Progresso)> progresso, CancellationToken ct)
    {
        var (ping, jitter) = await MedirPingAsync(progresso, ct);
        double download = await TransferirAsync(Fase.Download, progresso, ct);
        double upload = await TransferirAsync(Fase.Upload, progresso, ct);
        return new Resultado(download, upload, ping, jitter);
    }

    async Task<(double Ping, double Jitter)> MedirPingAsync(IProgress<(Fase, double, double)> progresso, CancellationToken ct)
    {
        const int Medicoes = 12;
        var tempos = new List<double>();
        for (int i = 0; i < Medicoes; i++)
        {
            var relogio = Stopwatch.StartNew();
            using (var resposta = await Http.GetAsync($"{Servidor}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct))
                resposta.EnsureSuccessStatusCode();
            relogio.Stop();
            if (i > 0) tempos.Add(relogio.Elapsed.TotalMilliseconds);   // o 1º inclui abrir a conexão
            if (tempos.Count > 0) progresso.Report((Fase.Ping, Mediana(tempos), (i + 1) * 10.0 / Medicoes));
        }
        double jitter = tempos.Zip(tempos.Skip(1), (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Average();
        return (Mediana(tempos), jitter);
    }

    static double Mediana(List<double> valores)
    {
        var ordenados = valores.OrderBy(v => v).ToList();
        int meio = ordenados.Count / 2;
        return ordenados.Count % 2 == 1 ? ordenados[meio] : (ordenados[meio - 1] + ordenados[meio]) / 2;
    }

    async Task<double> TransferirAsync(Fase fase, IProgress<(Fase, double, double)> progresso, CancellationToken ct)
    {
        Interlocked.Exchange(ref _bytes, 0);
        using var parar = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var conexoes = Enumerable.Range(0, Conexoes)
            .Select(_ => fase == Fase.Download ? BaixarAsync(parar.Token) : EnviarAsync(parar.Token))
            .ToArray();

        var relogio = Stopwatch.StartNew();
        double inicioMedicao = 0, velocidade = 0;
        long bytesNoInicio = 0;
        double inicioFase = fase == Fase.Download ? 10 : 55, pesoFase = 45;

        while (relogio.Elapsed.TotalSeconds < SegundosAquecimento + SegundosMedicao)
        {
            await Task.Delay(250, ct);
            if (conexoes.All(t => t.IsCompleted)) break;   // todas falharam
            double t = relogio.Elapsed.TotalSeconds;
            long bytes = Interlocked.Read(ref _bytes);
            if (inicioMedicao == 0 && t >= SegundosAquecimento) { inicioMedicao = t; bytesNoInicio = bytes; }

            velocidade = inicioMedicao > 0 && t > inicioMedicao + 0.2
                ? (bytes - bytesNoInicio) * 8 / (t - inicioMedicao) / 1_000_000
                : bytes * 8 / t / 1_000_000;
            double andamento = Math.Min(1, t / (SegundosAquecimento + SegundosMedicao));
            progresso.Report((fase, velocidade, inicioFase + andamento * pesoFase));
        }

        parar.Cancel();
        Exception? erro = null;
        try { await Task.WhenAll(conexoes); }
        catch (Exception ex) when (ex is not OperationCanceledException) { erro = ex; }
        catch (OperationCanceledException) { }
        ct.ThrowIfCancellationRequested();

        if (velocidade <= 0)
            throw new InvalidOperationException("não foi possível falar com o servidor de teste" +
                                                (erro is null ? "" : $" ({erro.GetBaseException().Message})"));
        return velocidade;
    }

    async Task BaixarAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var resposta = await Http.GetAsync($"{Servidor}/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, ct);
                resposta.EnsureSuccessStatusCode();
                await using var fluxo = await resposta.Content.ReadAsStreamAsync(ct);
                int lidos;
                while ((lidos = await fluxo.ReadAsync(buffer, ct)) > 0) Interlocked.Add(ref _bytes, lidos);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { /* fim do tempo de medição */ }
    }

    async Task EnviarAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var conteudo = new ConteudoContado(DadosUpload, n => Interlocked.Add(ref _bytes, n));
                using var resposta = await Http.PostAsync($"{Servidor}/__up", conteudo, ct);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
    }

    // Corpo do upload que vai contando os bytes à medida que são enviados
    sealed class ConteudoContado : HttpContent
    {
        readonly byte[] _dados;
        readonly Action<int> _contar;

        public ConteudoContado(byte[] dados, Action<int> contar)
        {
            _dados = dados;
            _contar = contar;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            const int Bloco = 64 * 1024;
            for (int i = 0; i < _dados.Length; i += Bloco)
            {
                int n = Math.Min(Bloco, _dados.Length - i);
                await stream.WriteAsync(_dados.AsMemory(i, n), ct);
                _contar(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _dados.Length;
            return true;
        }
    }
}

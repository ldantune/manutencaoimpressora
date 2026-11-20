using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Roda dentro do serviço do monitor (como Sistema) e conta:
//   - os bytes de cada aplicativo, pelos eventos de TCP/IP do kernel (ETW)
//   - o total de cada conexão (Wi-Fi, Ethernet...), lendo os contadores
//     das placas de rede a cada 5 segundos
//  Tudo é gravado no banco a cada 30 segundos.
// ---------------------------------------------------------------
public sealed class MonitorRedePorApp : IDisposable
{
    const string NomeSessao = "L2UtilitariosMonitorRede";
    const int MilissegundosEntreGravacoes = 30_000;
    const int MilissegundosEntreLeiturasDePlacas = 5_000;

    readonly BancoUsoApps _banco;
    readonly object _trava = new();

    // Acumulado desde a última gravação
    Dictionary<(DateTime Dia, string App), (string Caminho, long Recebidos, long Enviados)> _pendenteApps = new();
    Dictionary<(DateTime Dia, string Interface), (long Recebidos, long Enviados)> _pendenteInterfaces = new();

    // PID -> app (só acessado pela thread do ETW)
    readonly Dictionary<int, (string App, string Caminho)> _processos = new();

    // Último valor lido de cada placa (só acessado pelo timer das placas)
    readonly Dictionary<string, (long Recebidos, long Enviados)> _contadoresAnteriores = new();
    int _lendoPlacas;

    TraceEventSession? _sessao;
    Thread? _thread;
    System.Threading.Timer? _timerGravacao;
    System.Threading.Timer? _timerPlacas;

    public MonitorRedePorApp(BancoUsoApps banco)
    {
        _banco = banco;
    }

    public void Iniciar()
    {
        _banco.CriarSeNecessario();

        _sessao = new TraceEventSession(NomeSessao) { StopOnDispose = true };
        _sessao.EnableKernelProvider(
            KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);

        var kernel = _sessao.Source.Kernel;
        kernel.TcpIpRecv += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: true);
        kernel.TcpIpSend += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: false);
        kernel.TcpIpRecvIPV6 += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: true);
        kernel.TcpIpSendIPV6 += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: false);
        kernel.UdpIpRecv += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: true);
        kernel.UdpIpSend += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: false);
        kernel.UdpIpRecvIPV6 += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: true);
        kernel.UdpIpSendIPV6 += d => Somar(d.ProcessID, d.ProcessName, d.size, d.saddr, d.daddr, recebido: false);
        kernel.ProcessStop += d => _processos.Remove(d.ProcessID);   // o PID pode ser reaproveitado

        _thread = new Thread(() =>
        {
            try { _sessao.Source.Process(); }
            catch { /* sessão encerrada */ }
        })
        { IsBackground = true, Name = "Monitor de rede (ETW)" };
        _thread.Start();

        LerPlacas();   // primeira leitura só guarda os valores de partida
        _timerPlacas = new System.Threading.Timer(_ => LerPlacas(), null,
            MilissegundosEntreLeiturasDePlacas, MilissegundosEntreLeiturasDePlacas);
        _timerGravacao = new System.Threading.Timer(_ => Gravar(), null,
            MilissegundosEntreGravacoes, MilissegundosEntreGravacoes);
    }

    // ---------------- Por aplicativo (ETW) ----------------

    void Somar(int pid, string? nomeProcesso, int bytes, IPAddress? origem, IPAddress? destino, bool recebido)
    {
        if (bytes <= 0) return;
        // Tráfego interno da máquina (localhost) não é internet
        if ((origem != null && IPAddress.IsLoopback(origem)) || (destino != null && IPAddress.IsLoopback(destino))) return;

        var (app, caminho) = Identificar(pid, nomeProcesso);
        var chave = (DateTime.Today, app);
        lock (_trava)
        {
            _pendenteApps.TryGetValue(chave, out var atual);
            _pendenteApps[chave] = (caminho.Length > 0 ? caminho : atual.Caminho ?? "",
                atual.Recebidos + (recebido ? bytes : 0),
                atual.Enviados + (recebido ? 0 : bytes));
        }
    }

    // Nome amigável do app: a descrição do .exe (ex.: "Google Chrome"), senão o nome do processo.
    // Para o "Host de Serviço" (svchost.exe), mostra quais serviços do Windows rodam nele.
    (string App, string Caminho) Identificar(int pid, string? nomeProcesso)
    {
        if (pid <= 4) return ("Sistema do Windows", "");
        if (_processos.TryGetValue(pid, out var conhecido)) return conhecido;

        string app = string.IsNullOrWhiteSpace(nomeProcesso) ? "" : nomeProcesso;
        string caminho = "";
        try
        {
            using var processo = Process.GetProcessById(pid);
            caminho = processo.MainModule?.FileName ?? "";
            if (app.Length == 0) app = processo.ProcessName;
        }
        catch { /* processo protegido ou já encerrado */ }

        bool ehHostDeServico = Path.GetFileName(caminho).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)
                               || app.Equals("svchost", StringComparison.OrdinalIgnoreCase);
        if (ehHostDeServico)
        {
            var servicos = ServicosDoProcesso(pid);
            if (servicos.Count > 0)
                app = "Serviço do Windows: " + (servicos.Count == 1 ? servicos[0] : $"{servicos[0]} (+{servicos.Count - 1})");
            else
                app = "Host de Serviço do Windows";
        }
        else if (caminho.Length > 0)
        {
            try
            {
                var descricao = FileVersionInfo.GetVersionInfo(caminho).FileDescription;
                if (!string.IsNullOrWhiteSpace(descricao)) app = descricao.Trim();
            }
            catch { }
        }
        if (app.Length == 0) app = $"Processo {pid}";

        var resultado = (app, caminho);
        _processos[pid] = resultado;
        return resultado;
    }

    static List<string> ServicosDoProcesso(int pid)
    {
        var nomes = new List<string>();
        try
        {
            using var busca = new ManagementObjectSearcher($"SELECT DisplayName FROM Win32_Service WHERE ProcessId = {pid}");
            foreach (ManagementObject servico in busca.Get())
            {
                using (servico)
                    if (servico["DisplayName"] is string nome && nome.Length > 0) nomes.Add(nome);
            }
        }
        catch { /* WMI indisponível */ }
        nomes.Sort(StringComparer.CurrentCulture);
        return nomes;
    }

    // ---------------- Por conexão (contadores das placas) ----------------

    void LerPlacas()
    {
        if (Interlocked.Exchange(ref _lendoPlacas, 1) == 1) return;   // leitura anterior ainda rodando
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!RedeUtil.ContaParaUso(placa)) continue;
                IPInterfaceStatistics estatisticas;
                try { estatisticas = placa.GetIPStatistics(); }
                catch { continue; }

                long recebidos = estatisticas.BytesReceived, enviados = estatisticas.BytesSent;
                if (_contadoresAnteriores.TryGetValue(placa.Id, out var anterior))
                {
                    long down = Math.Max(0, recebidos - anterior.Recebidos);
                    long up = Math.Max(0, enviados - anterior.Enviados);
                    if (down > 0 || up > 0)
                    {
                        var chave = (DateTime.Today, placa.Name);
                        lock (_trava)
                        {
                            _pendenteInterfaces.TryGetValue(chave, out var atual);
                            _pendenteInterfaces[chave] = (atual.Recebidos + down, atual.Enviados + up);
                        }
                    }
                }
                _contadoresAnteriores[placa.Id] = (recebidos, enviados);
            }
        }
        catch { /* lista de placas indisponível neste instante */ }
        finally
        {
            Interlocked.Exchange(ref _lendoPlacas, 0);
        }
    }

    // ---------------- Gravação ----------------

    void Gravar()
    {
        Dictionary<(DateTime Dia, string App), (string Caminho, long Recebidos, long Enviados)> apps;
        Dictionary<(DateTime Dia, string Interface), (long Recebidos, long Enviados)> interfaces;
        lock (_trava)
        {
            if (_pendenteApps.Count == 0 && _pendenteInterfaces.Count == 0) return;
            apps = _pendenteApps;
            interfaces = _pendenteInterfaces;
            _pendenteApps = new();
            _pendenteInterfaces = new();
        }

        try
        {
            _banco.Acumular(
                apps.Select(kv => (kv.Key.Dia, kv.Key.App, kv.Value.Caminho, kv.Value.Recebidos, kv.Value.Enviados)),
                interfaces.Select(kv => (kv.Key.Dia, kv.Key.Interface, kv.Value.Recebidos, kv.Value.Enviados)));
        }
        catch
        {
            // Não conseguiu gravar (banco ocupado): devolve para a próxima tentativa
            lock (_trava)
            {
                foreach (var (chave, valor) in apps)
                {
                    _pendenteApps.TryGetValue(chave, out var atual);
                    _pendenteApps[chave] = (valor.Caminho, atual.Recebidos + valor.Recebidos, atual.Enviados + valor.Enviados);
                }
                foreach (var (chave, valor) in interfaces)
                {
                    _pendenteInterfaces.TryGetValue(chave, out var atual);
                    _pendenteInterfaces[chave] = (atual.Recebidos + valor.Recebidos, atual.Enviados + valor.Enviados);
                }
            }
        }
    }

    public void Dispose()
    {
        _timerGravacao?.Dispose();
        _timerPlacas?.Dispose();
        _sessao?.Dispose();          // encerra a sessão; o Process() da thread retorna
        _thread?.Join(TimeSpan.FromSeconds(5));
        LerPlacas();
        Gravar();
    }
}

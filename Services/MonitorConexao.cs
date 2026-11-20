using System.Net;
using System.Net.NetworkInformation;
using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Roda dentro do serviço do monitor: a cada 5 segundos mede se a
//  internet responde (ping para 1.1.1.1 e 8.8.8.8) e quanto demora.
//  Duas falhas seguidas abrem uma "queda", que fecha quando volta a
//  responder. Também diz se o roteador respondia na hora da queda
//  (problema do provedor) ou não (problema na rede de casa).
// ---------------------------------------------------------------
public sealed class MonitorConexao : IDisposable
{
    static readonly IPAddress[] Alvos = { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8") };
    const int MilissegundosEntreMedicoes = 5_000;
    const int TempoLimitePing = 2_000;
    const int FalhasParaQueda = 2;

    readonly BancoUsoApps _banco;
    readonly object _trava = new();
    Dictionary<DateTime, MinutoLatencia> _pendente = new();

    System.Threading.Timer? _timerMedicao;
    System.Threading.Timer? _timerGravacao;
    int _medindo;
    int _falhasSeguidas;
    long? _quedaAberta;

    public MonitorConexao(BancoUsoApps banco)
    {
        _banco = banco;
    }

    public void Iniciar()
    {
        _banco.CriarSeNecessario();
        _banco.FecharQuedasAbertas();
        _timerMedicao = new System.Threading.Timer(_ => Medir(), null, 2_000, MilissegundosEntreMedicoes);
        _timerGravacao = new System.Threading.Timer(_ => Gravar(), null, 30_000, 30_000);
    }

    void Medir()
    {
        if (Interlocked.Exchange(ref _medindo, 1) == 1) return;
        try
        {
            double? ms = null;
            using (var ping = new Ping())
            {
                foreach (var alvo in Alvos)
                {
                    try
                    {
                        var resposta = ping.Send(alvo, TempoLimitePing);
                        if (resposta.Status == IPStatus.Success) { ms = resposta.RoundtripTime; break; }
                    }
                    catch (PingException) { /* sem rede nenhuma */ }
                }
            }

            var agora = DateTime.Now;
            var minuto = new DateTime(agora.Year, agora.Month, agora.Day, agora.Hour, agora.Minute, 0);
            lock (_trava)
            {
                _pendente.TryGetValue(minuto, out var atual);
                atual ??= new MinutoLatencia(minuto, 0, 0, 0, 0);
                _pendente[minuto] = ms is double v
                    ? atual with { Soma = atual.Soma + v, Maximo = Math.Max(atual.Maximo, v), Respostas = atual.Respostas + 1 }
                    : atual with { Perdas = atual.Perdas + 1 };
            }

            if (ms is null)
            {
                _falhasSeguidas++;
                if (_falhasSeguidas == FalhasParaQueda && _quedaAberta is null)
                {
                    // A queda começou na primeira falha, não na segunda
                    var inicio = agora.AddMilliseconds(-MilissegundosEntreMedicoes * (FalhasParaQueda - 1));
                    _quedaAberta = _banco.AbrirQueda(inicio, Motivo());
                }
            }
            else
            {
                _falhasSeguidas = 0;
                if (_quedaAberta is long id)
                {
                    _banco.FecharQueda(id, agora);
                    _quedaAberta = null;
                }
            }
        }
        catch { /* nunca derruba o serviço */ }
        finally
        {
            Interlocked.Exchange(ref _medindo, 0);
        }
    }

    // Por que caiu: sem rede, roteador mudo ou internet fora (roteador respondendo)
    static string Motivo()
    {
        var gateway = RedeUtil.GatewayPadrao();
        if (gateway is null) return "Sem conexão de rede (cabo ou Wi-Fi desconectado)";
        try
        {
            using var ping = new Ping();
            if (ping.Send(gateway, 1_000).Status == IPStatus.Success)
                return "Internet fora (o roteador respondia, problema do provedor)";
        }
        catch { }
        return "Sem resposta do roteador";
    }

    void Gravar()
    {
        Dictionary<DateTime, MinutoLatencia> lote;
        lock (_trava)
        {
            if (_pendente.Count == 0) return;
            lote = _pendente;
            _pendente = new();
        }
        try { _banco.RegistrarLatencia(lote.Values); }
        catch
        {
            lock (_trava)
            {
                foreach (var (minuto, valor) in lote)
                {
                    _pendente.TryGetValue(minuto, out var atual);
                    _pendente[minuto] = atual is null ? valor : atual with
                    {
                        Soma = atual.Soma + valor.Soma, Maximo = Math.Max(atual.Maximo, valor.Maximo),
                        Respostas = atual.Respostas + valor.Respostas, Perdas = atual.Perdas + valor.Perdas
                    };
                }
            }
        }
    }

    public void Dispose()
    {
        _timerMedicao?.Dispose();
        _timerGravacao?.Dispose();
        Gravar();
        // A queda aberta fica aberta: ao reiniciar, o serviço fecha no último minuto medido
    }
}

using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Junta as três origens de dados de uso de internet:
//   - o monitor (serviço do Windows): conta sempre, por conexão e por app
//   - a contagem da própria tela (antes do monitor existir, ou com ele parado)
//   - o histórico importado do Windows (dias antigos, só "todas as conexões")
//  O monitor e a tela nunca contam ao mesmo tempo, então somar os dois é
//  seguro. O histórico do Windows só entra quando é maior (dia mal contado).
// ---------------------------------------------------------------
public class FonteUsoRede
{
    static readonly TradutorDeApps Tradutor = new();

    public HistoricoRede Local { get; }
    public BancoUsoApps Monitor { get; }

    public FonteUsoRede(HistoricoRede local, BancoUsoApps monitor)
    {
        Local = local;
        Monitor = monitor;
    }

    // Um item por dia entre as datas (inclusive); interfaceNome null = todas as conexões
    public List<UsoDia> ListarDias(DateTime desde, DateTime ate, string? interfaceNome = null)
    {
        var doMonitor = Monitor.DiasInterfaces(desde, ate, interfaceNome);
        var daTela = interfaceNome is null
            ? Local.ListarDias(desde, ate).Where(d => d.Total > 0).ToDictionary(d => d.Data, d => (d.Recebidos, d.Enviados))
            : new Dictionary<DateTime, (long Recebidos, long Enviados)>();
        var doWindows = interfaceNome is null
            ? Local.DiasWindows(desde, ate)
            : new Dictionary<DateTime, (long Recebidos, long Enviados)>();

        var lista = new List<UsoDia>();
        for (var dia = desde.Date; dia <= ate.Date; dia = dia.AddDays(1))
        {
            doMonitor.TryGetValue(dia, out var m);
            daTela.TryGetValue(dia, out var t);
            doWindows.TryGetValue(dia, out var w);
            long recebidos = m.Recebidos + t.Recebidos, enviados = m.Enviados + t.Enviados;
            if (w.Recebidos + w.Enviados > recebidos + enviados) (recebidos, enviados) = (w.Recebidos, w.Enviados);
            lista.Add(new UsoDia(dia, recebidos, enviados));
        }
        return lista;
    }

    public (long Recebidos, long Enviados) TotalEntre(DateTime desde, DateTime ate, string? interfaceNome = null)
    {
        var dias = ListarDias(desde, ate, interfaceNome);
        return (dias.Sum(d => d.Recebidos), dias.Sum(d => d.Enviados));
    }

    public DateTime? PrimeiroDia(string? interfaceNome = null)
    {
        var datas = new List<DateTime?> { Monitor.PrimeiroDiaInterfaces(interfaceNome) };
        if (interfaceNome is null)
        {
            datas.Add(Local.PrimeiroDia());
            datas.Add(Local.PrimeiroDiaWindows());
        }
        var validas = datas.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        return validas.Count == 0 ? null : validas.Min();
    }

    public List<string> Interfaces() => Monitor.Interfaces();

    // Uso por app desde a data: o monitor, mais o histórico do Windows para os dias
    // anteriores ao início do monitor
    public List<UsoApp> ListarApps(DateTime desde)
    {
        var porApp = new Dictionary<string, UsoApp>(StringComparer.CurrentCultureIgnoreCase);
        void Somar(UsoApp uso)
        {
            porApp[uso.App] = porApp.TryGetValue(uso.App, out var atual)
                ? atual with
                {
                    Caminho = atual.Caminho.Length > 0 ? atual.Caminho : uso.Caminho,
                    Recebidos = atual.Recebidos + uso.Recebidos,
                    Enviados = atual.Enviados + uso.Enviados
                }
                : uso;
        }

        foreach (var uso in Monitor.Listar(desde)) Somar(uso);
        var inicioMonitor = Monitor.PrimeiroDiaApps() ?? DateTime.Today.AddDays(1);
        if (desde.Date < inicioMonitor)
            foreach (var uso in Local.ListarAppsWindows(desde, inicioMonitor))
            {
                // Registros importados antes da tradução de nomes ainda têm o nome técnico
                if (TradutorDeApps.EhNomeTecnico(uso.App))
                {
                    var (nome, caminho) = Tradutor.Traduzir(uso.App, null);
                    Somar(uso with { App = nome, Caminho = caminho.Length > 0 ? caminho : uso.Caminho });
                }
                else Somar(uso);
            }

        return porApp.Values.OrderByDescending(a => a.Total).ToList();
    }
}

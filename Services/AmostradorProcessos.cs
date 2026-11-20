using System.Diagnostics;

namespace ManutencaoImpressora.Services;

public record ProcessoPesado(string Nome, string Caminho, double Cpu, long Memoria);

// ---------------------------------------------------------------
//  Descobre os programas que mais usam processador e memória,
//  comparando o tempo de processador de cada um entre duas leituras.
//  Processos com o mesmo nome (ex.: as várias abas do Chrome) somam.
// ---------------------------------------------------------------
public sealed class AmostradorProcessos
{
    readonly Dictionary<int, TimeSpan> _tempoAnterior = new();
    readonly Dictionary<string, (string Nome, string Caminho)> _nomes = new(StringComparer.OrdinalIgnoreCase);
    DateTime _leituraAnterior = DateTime.MinValue;

    // Da última leitura: os que mais usam memória (para o diagnóstico de memória cheia)
    public List<ProcessoPesado> PorMemoria { get; private set; } = new();

    // A máquina virtual do WSL/Docker aparece como "vmmem": nome que a pessoa reconhece
    string NomeExibido(string processo) =>
        processo.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase) ? "WSL / Docker (máquina virtual)" : _nomes[processo].Nome;

    public static bool EhWsl(ProcessoPesado p) => p.Nome.StartsWith("WSL", StringComparison.Ordinal);

    public List<ProcessoPesado> Ler(int quantidade)
    {
        var agora = DateTime.UtcNow;
        double segundos = (agora - _leituraAnterior).TotalSeconds;
        bool temBase = _leituraAnterior != DateTime.MinValue && segundos > 0.2;
        _leituraAnterior = agora;

        var porNome = new Dictionary<string, (double Cpu, long Memoria)>(StringComparer.OrdinalIgnoreCase);
        var vistos = new HashSet<int>();
        foreach (var processo in Process.GetProcesses())
        {
            using (processo)
            {
                try
                {
                    if (processo.Id == 0) continue;
                    vistos.Add(processo.Id);
                    long memoria = processo.WorkingSet64;
                    double cpu = 0;
                    try
                    {
                        var tempo = processo.TotalProcessorTime;
                        if (temBase && _tempoAnterior.TryGetValue(processo.Id, out var antes))
                            cpu = (tempo - antes).TotalSeconds / segundos / Environment.ProcessorCount * 100;
                        _tempoAnterior[processo.Id] = tempo;
                    }
                    catch { /* processo protegido: só a memória */ }

                    string nome = processo.ProcessName;
                    porNome.TryGetValue(nome, out var soma);
                    porNome[nome] = (soma.Cpu + Math.Max(0, cpu), soma.Memoria + memoria);
                    if (!_nomes.ContainsKey(nome)) _nomes[nome] = NomeAmigavel(processo);
                }
                catch { /* processo encerrou no meio */ }
            }
        }
        foreach (var pid in _tempoAnterior.Keys.Where(p => !vistos.Contains(p)).ToList()) _tempoAnterior.Remove(pid);

        var todos = porNome
            .Where(kv => !kv.Key.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            .Select(kv => new ProcessoPesado(NomeExibido(kv.Key), _nomes[kv.Key].Caminho, kv.Value.Cpu, kv.Value.Memoria))
            .ToList();
        PorMemoria = todos.OrderByDescending(p => p.Memoria).Take(quantidade).ToList();
        return todos
            .OrderByDescending(p => Math.Round(p.Cpu))
            .ThenByDescending(p => p.Memoria)
            .Take(quantidade)
            .ToList();
    }

    static (string, string) NomeAmigavel(Process processo)
    {
        try
        {
            string caminho = processo.MainModule?.FileName ?? "";
            string? descricao = caminho.Length > 0 ? FileVersionInfo.GetVersionInfo(caminho).FileDescription : null;
            return (string.IsNullOrWhiteSpace(descricao) ? processo.ProcessName : descricao.Trim(), caminho);
        }
        catch { return (processo.ProcessName, ""); }
    }
}

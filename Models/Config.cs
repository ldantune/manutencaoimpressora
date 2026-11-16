using System.Text.Json;

namespace ManutencaoImpressora.Models;

// ---------------------------------------------------------------
//  Configuração salva em %AppData%\ManutencaoImpressora\config.json
// ---------------------------------------------------------------
public class Config
{
    public string Impressora { get; set; } = "";
    public string CaminhoDocumento { get; set; } = "";
    public List<DayOfWeek> Dias { get; set; } = new() { DayOfWeek.Monday, DayOfWeek.Thursday };
    public int IntervaloSemanas { get; set; } = 1;
    public TimeSpan Hora { get; set; } = new(10, 0, 0);
    public DateTime Inicio { get; set; } = DateTime.Today;
    public bool TemFim { get; set; }
    public DateTime Fim { get; set; } = DateTime.Today.AddYears(1);

    public static Config Carregar(string arquivo)
    {
        try
        {
            if (File.Exists(arquivo))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(arquivo)) ?? new();
        }
        catch { }
        return new();
    }

    public void Salvar(string arquivo) =>
        File.WriteAllText(arquivo,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    // Calcula a próxima data de impressão (só para mostrar na tela)
    public DateTime? ProximaExecucao()
    {
        var agora = DateTime.Now;
        var semanaInicial = Inicio.Date.AddDays(-(int)Inicio.DayOfWeek);
        var d = Inicio.Date > agora.Date ? Inicio.Date : agora.Date;

        for (int i = 0; i < 400; i++, d = d.AddDays(1))
        {
            if (TemFim && d > Fim.Date) return null;
            if (!Dias.Contains(d.DayOfWeek)) continue;
            int semanas = (int)((d - semanaInicial).TotalDays / 7);
            if (semanas % IntervaloSemanas != 0) continue;
            var quando = d + Hora;
            if (quando > agora) return quando;
        }
        return null;
    }
}

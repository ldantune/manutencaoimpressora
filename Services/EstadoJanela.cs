using System.Text.Json;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Tamanho, posição e se a janela estava maximizada, para reabrir
//  do jeito que a pessoa deixou (%AppData%\ManutencaoImpressora\janela.json)
// ---------------------------------------------------------------
public record EstadoJanela(int X, int Y, int Largura, int Altura, bool Maximizada)
{
    static string Arquivo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManutencaoImpressora", "janela.json");

    public Rectangle Retangulo => new(X, Y, Largura, Altura);

    public static EstadoJanela? Carregar()
    {
        try
        {
            if (!File.Exists(Arquivo)) return null;
            var estado = JsonSerializer.Deserialize<EstadoJanela>(File.ReadAllText(Arquivo));
            // Só usa se ainda couber em algum monitor (ex.: monitor externo desconectado)
            if (estado is null || estado.Largura < 200 || estado.Altura < 200) return null;
            return Screen.AllScreens.Any(t => t.WorkingArea.IntersectsWith(estado.Retangulo)) ? estado : null;
        }
        catch { return null; }
    }

    public void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this));
        }
        catch { /* não é crítico */ }
    }
}

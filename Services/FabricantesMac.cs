using System.Text;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Fabricante do aparelho a partir do endereço físico (MAC): os 6
//  primeiros dígitos identificam o fabricante na lista oficial do IEEE.
//  A lista é baixada uma vez e guardada em cache por 90 dias.
// ---------------------------------------------------------------
public static class FabricantesMac
{
    const string Fonte = "https://standards-oui.ieee.org/oui/oui.txt";
    static readonly string Arquivo = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManutencaoImpressora", "fabricantes_mac.txt");

    static Dictionary<string, string>? _mapa;

    public static async Task CarregarAsync()
    {
        if (_mapa != null) return;
        try
        {
            if (!File.Exists(Arquivo) || File.GetLastWriteTime(Arquivo) < DateTime.Now.AddDays(-90))
                await BaixarAsync();
        }
        catch { /* sem internet: usa o cache antigo, se houver */ }

        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(Arquivo))
                foreach (var linha in File.ReadLines(Arquivo))
                    if (linha.Length > 7 && linha[6] == '\t') mapa[linha[..6]] = linha[7..];
        }
        catch { }
        _mapa = mapa;
    }

    static async Task BaixarAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 L2Utilitarios");
        string texto = await http.GetStringAsync(Fonte);

        var compacto = new StringBuilder();
        foreach (var linha in texto.Split('\n'))
        {
            int marca = linha.IndexOf("(hex)", StringComparison.Ordinal);
            if (marca < 0) continue;
            string prefixo = linha[..marca].Trim().Replace("-", "");
            string nome = linha[(marca + 5)..].Trim();
            if (prefixo.Length == 6 && nome.Length > 0) compacto.Append(prefixo).Append('\t').Append(nome).Append('\n');
        }
        if (compacto.Length < 10_000) return;   // download incompleto
        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
        await File.WriteAllTextAsync(Arquivo, compacto.ToString());
    }

    // Celulares modernos usam um endereço "privado" aleatório (2º dígito 2, 6, A ou E)
    public static bool EnderecoPrivado(string mac)
    {
        string hex = mac.Replace(":", "").Replace("-", "");
        return hex.Length >= 2 && (Convert.ToInt32(hex[..2], 16) & 0x02) != 0;
    }

    public static string? Buscar(string mac)
    {
        string hex = mac.Replace(":", "").Replace("-", "");
        if (hex.Length < 6 || EnderecoPrivado(mac) || _mapa is null) return null;
        return _mapa.TryGetValue(hex[..6], out var nome) ? Simplificar(nome) : null;
    }

    // "Samsung Electronics Co.,Ltd" -> "Samsung Electronics"
    static string Simplificar(string nome)
    {
        foreach (var sufixo in new[] { " Co.,Ltd", " Co., Ltd", ",Inc.", ", Inc.", " Inc.", " Corporation", " Corp.", " Ltd.", " LLC", " GmbH", " Limited" })
        {
            int i = nome.IndexOf(sufixo, StringComparison.OrdinalIgnoreCase);
            if (i > 0) nome = nome[..i];
        }
        return nome.Trim().TrimEnd(',', '.');
    }
}

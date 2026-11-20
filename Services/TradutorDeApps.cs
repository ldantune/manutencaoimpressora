using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Management.Deployment;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  O Windows identifica os apps de forma "técnica"; aqui eles são
//  traduzidos para o mesmo nome que o monitor usa:
//   - \Device\HarddiskVolume3\...\chrome.exe -> C:\...\chrome.exe -> "Google Chrome"
//   - Claude_pzs8sxrjxfjjc (app da Microsoft Store) -> "Claude", com o logo do app
// ---------------------------------------------------------------
public sealed class TradutorDeApps
{
    readonly Dictionary<string, (string Nome, string Caminho)> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(string Dispositivo, string Letra)> _volumes = MapearVolumes();
    PackageManager? _pacotes;

    public (string Nome, string Caminho) Traduzir(string id, string? nomeInformado)
    {
        id = (id ?? "").Trim();
        if (_cache.TryGetValue(id, out var pronto)) return pronto;

        (string, string) resultado;
        if (id.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase) || id.Contains('\\'))
            resultado = DeArquivo(id, nomeInformado);
        else if (PareceNomeDePacote(id))
            resultado = DePacote(id, nomeInformado);
        else
            resultado = (string.IsNullOrWhiteSpace(nomeInformado) ? id : nomeInformado.Trim(), "");

        _cache[id] = resultado;
        return resultado;
    }

    // Programa comum: mesmo nome que o monitor usa (descrição do .exe)
    (string, string) DeArquivo(string id, string? nomeInformado)
    {
        string caminho = ParaCaminhoComLetra(id) ?? id;
        if (!File.Exists(caminho))
        {
            string arquivo = Path.GetFileNameWithoutExtension(caminho);
            return (!string.IsNullOrWhiteSpace(nomeInformado) ? nomeInformado.Trim() : arquivo, "");
        }

        string nome = "";
        try { nome = FileVersionInfo.GetVersionInfo(caminho).FileDescription?.Trim() ?? ""; } catch { }
        if (nome.Length == 0) nome = !string.IsNullOrWhiteSpace(nomeInformado) ? nomeInformado.Trim() : Path.GetFileNameWithoutExtension(caminho);
        return (nome, caminho);
    }

    // App da Microsoft Store: nome de exibição e logo do pacote
    (string, string) DePacote(string familia, string? nomeInformado)
    {
        try
        {
            _pacotes ??= new PackageManager();
            var pacote = _pacotes.FindPackagesForUser(string.Empty, familia).FirstOrDefault();
            if (pacote != null)
            {
                string nome = pacote.DisplayName;
                string logo = "";
                try { logo = AcharArquivoDeLogo(pacote.Logo?.LocalPath); } catch { }
                if (!string.IsNullOrWhiteSpace(nome)) return (nome.Trim(), logo);
            }
        }
        catch { /* sem acesso aos pacotes */ }

        // Sem o pacote instalado: "5319275A.WhatsAppDesktop_cv1g..." -> "WhatsAppDesktop"
        string semEditor = familia[..familia.LastIndexOf('_')];
        int ponto = semEditor.IndexOf('.');
        if (ponto >= 0 && ponto < semEditor.Length - 1) semEditor = semEditor[(ponto + 1)..];
        return (!string.IsNullOrWhiteSpace(nomeInformado) ? nomeInformado.Trim() : semEditor, "");
    }

    public static bool PareceNomeDePacote(string id)
    {
        int sublinhado = id.LastIndexOf('_');
        if (sublinhado <= 0) return false;
        string editor = id[(sublinhado + 1)..];
        return editor.Length == 13 && editor.All(char.IsLetterOrDigit);
    }

    // O logo vem como "Assets\StoreLogo.png", mas o arquivo real costuma ser "StoreLogo.scale-100.png"
    static string AcharArquivoDeLogo(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho)) return "";
        if (File.Exists(caminho)) return caminho;
        string? pasta = Path.GetDirectoryName(caminho);
        if (pasta is null || !Directory.Exists(pasta)) return "";
        string nome = Path.GetFileNameWithoutExtension(caminho);
        return Directory.GetFiles(pasta, nome + ".*" + Path.GetExtension(caminho))
            .OrderBy(f => f.Contains("scale-100") ? 0 : f.Contains("scale-200") ? 1 : 2)
            .FirstOrDefault() ?? "";
    }

    // \Device\HarddiskVolume3\pasta\app.exe -> C:\pasta\app.exe
    string? ParaCaminhoComLetra(string id)
    {
        foreach (var (dispositivo, letra) in _volumes)
            if (id.StartsWith(dispositivo + "\\", StringComparison.OrdinalIgnoreCase))
                return letra + id[dispositivo.Length..];
        return null;
    }

    static List<(string, string)> MapearVolumes()
    {
        var lista = new List<(string, string)>();
        foreach (var unidade in DriveInfo.GetDrives())
        {
            string letra = unidade.Name[..2];   // "C:"
            var destino = new StringBuilder(1024);
            if (QueryDosDevice(letra, destino, destino.Capacity) != 0)
                lista.Add((destino.ToString(), letra));
        }
        return lista;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    public static bool EhNomeTecnico(string nome) => nome.Contains('\\') || PareceNomeDePacote(nome);
}

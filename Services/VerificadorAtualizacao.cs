using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Confere no GitHub (releases do repositório) se existe uma versão
//  mais nova do que a instalada, e baixa o instalador dela.
//  Falha em silêncio se não houver internet ou o repositório/release
//  ainda não existir.
// ---------------------------------------------------------------
public record AtualizacaoDisponivel(Version Versao, string UrlInstalador);

public class VerificadorAtualizacao
{
    const string UrlApi = "https://api.github.com/repos/ldantune/manutencaoimpressora/releases/latest";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<AtualizacaoDisponivel?> VerificarAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UrlApi);
            req.Headers.UserAgent.ParseAdd("ManutencaoImpressora");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var release = await resp.Content.ReadFromJsonAsync<ReleaseGitHub>();
            if (release is null || string.IsNullOrWhiteSpace(release.TagName)) return null;

            var versaoRemota = ParseVersao(release.TagName);
            var versaoAtual = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            if (versaoRemota is null || versaoRemota <= versaoAtual) return null;

            var instalador = release.Assets?.FirstOrDefault(
                a => a.Name.EndsWith("_Instalador.exe", StringComparison.OrdinalIgnoreCase));
            if (instalador is null) return null;

            return new AtualizacaoDisponivel(versaoRemota, instalador.DownloadUrl);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> BaixarInstaladorAsync(string url, string destino)
    {
        var bytes = await Http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(destino, bytes);
        return destino;
    }

    static Version? ParseVersao(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? v : null;

    class ReleaseGitHub
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<AssetGitHub>? Assets { get; set; }
    }

    class AssetGitHub
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";
    }
}

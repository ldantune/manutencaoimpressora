namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Registro simples do monitor de rede (instalação e início do
//  serviço), em C:\ProgramData\L2Utilitarios\monitor.log, para
//  entender o que houve quando ele não sobe
// ---------------------------------------------------------------
public static class LogMonitor
{
    static readonly string Arquivo = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L2Utilitarios", "monitor.log");

    public static void Registrar(string mensagem)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            // Mantém o arquivo pequeno
            if (File.Exists(Arquivo) && new FileInfo(Arquivo).Length > 512 * 1024) File.Delete(Arquivo);
            File.AppendAllText(Arquivo, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}  {mensagem}{Environment.NewLine}");
        }
        catch { /* sem permissão de escrita (ex.: tela sem admin) */ }
    }

    public static string UltimasLinhas(int quantidade)
    {
        try
        {
            if (!File.Exists(Arquivo)) return "";
            using var leitura = new FileStream(Arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var texto = new StreamReader(leitura);
            var linhas = texto.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine, linhas.TakeLast(quantidade));
        }
        catch { return ""; }
    }
}

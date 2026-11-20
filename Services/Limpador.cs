using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace ManutencaoImpressora.Services;

public enum CategoriaLimpeza { Temporarios, AtualizacoesWindows, Lixeira, Miniaturas, RelatoriosErro, CacheNavegadores, DownloadsAntigos }

public record ResultadoAnalise(CategoriaLimpeza Categoria, long Bytes, int Arquivos);
public record ArquivoGrande(string Caminho, long Bytes, DateTime Modificado);

// ---------------------------------------------------------------
//  Calcula quanto cada categoria ocupa e apaga o que for escolhido.
//  Arquivos em uso são pulados. Downloads antigos vão para a Lixeira
//  (dá para recuperar); o resto é apagado de vez.
// ---------------------------------------------------------------
public static class Limpador
{
    const int DiasDownloadsAntigos = 90;

    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string Downloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public static string PastaAtualizacoesWindows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

    public static bool PedeAdministrador(CategoriaLimpeza c) => c == CategoriaLimpeza.AtualizacoesWindows;

    // Pastas inteiras (conteúdo) de cada categoria
    static IEnumerable<string> Pastas(CategoriaLimpeza categoria)
    {
        switch (categoria)
        {
            case CategoriaLimpeza.Temporarios:
                yield return Path.GetTempPath();
                break;
            case CategoriaLimpeza.AtualizacoesWindows:
                yield return PastaAtualizacoesWindows;
                break;
            case CategoriaLimpeza.RelatoriosErro:
                yield return Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportArchive");
                yield return Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportQueue");
                yield return Path.Combine(Local, "CrashDumps");
                break;
            case CategoriaLimpeza.CacheNavegadores:
                foreach (var dados in new[] { Path.Combine(Local, "Google", "Chrome", "User Data"), Path.Combine(Local, "Microsoft", "Edge", "User Data") })
                {
                    if (!Directory.Exists(dados)) continue;
                    foreach (var perfil in SemErro(() => Directory.GetDirectories(dados)))
                        foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                        {
                            string pasta = Path.Combine(perfil, cache);
                            if (Directory.Exists(pasta)) yield return pasta;
                        }
                }
                break;
        }
    }

    static IEnumerable<string> Arquivos(CategoriaLimpeza categoria)
    {
        switch (categoria)
        {
            case CategoriaLimpeza.Miniaturas:
                string explorer = Path.Combine(Local, "Microsoft", "Windows", "Explorer");
                foreach (var f in SemErro(() => Directory.GetFiles(explorer, "thumbcache_*.db"))) yield return f;
                foreach (var f in SemErro(() => Directory.GetFiles(explorer, "iconcache_*.db"))) yield return f;
                break;
            case CategoriaLimpeza.DownloadsAntigos:
                var limite = DateTime.Now.AddDays(-DiasDownloadsAntigos);
                foreach (var f in SemErro(() => Directory.GetFiles(Downloads)))
                    if (File.GetLastWriteTime(f) < limite) yield return f;
                break;
            default:
                foreach (var pasta in Pastas(categoria))
                    foreach (var f in Recursivo(pasta)) yield return f;
                break;
        }
    }

    static T[] SemErro<T>(Func<T[]> obter)
    {
        try { return obter(); } catch { return Array.Empty<T>(); }
    }

    // Percorre a pasta sem seguir atalhos de pasta (junctions) e ignorando o que não dá para ler
    static IEnumerable<string> Recursivo(string pasta)
    {
        if (!Directory.Exists(pasta)) yield break;
        var opcoes = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        };
        IEnumerable<string> arquivos;
        try { arquivos = Directory.EnumerateFiles(pasta, "*", opcoes); }
        catch { yield break; }
        foreach (var f in arquivos) yield return f;
    }

    static long Tamanho(string arquivo)
    {
        try { return new FileInfo(arquivo).Length; } catch { return 0; }
    }

    public static ResultadoAnalise Analisar(CategoriaLimpeza categoria)
    {
        if (categoria == CategoriaLimpeza.Lixeira)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin(null, ref info) == 0
                ? new ResultadoAnalise(categoria, info.i64Size, (int)info.i64NumItems)
                : new ResultadoAnalise(categoria, 0, 0);
        }
        long bytes = 0;
        int quantidade = 0;
        foreach (var f in Arquivos(categoria)) { bytes += Tamanho(f); quantidade++; }
        return new ResultadoAnalise(categoria, bytes, quantidade);
    }

    // Apaga a categoria; devolve quantos bytes foram liberados
    public static long Limpar(CategoriaLimpeza categoria)
    {
        if (categoria == CategoriaLimpeza.Lixeira)
        {
            long antes = Analisar(categoria).Bytes;
            const uint SemConfirmacao = 0x1, SemProgresso = 0x2, SemSom = 0x4;
            SHEmptyRecycleBin(IntPtr.Zero, null, SemConfirmacao | SemProgresso | SemSom);
            return antes - Analisar(categoria).Bytes;
        }

        long liberado = 0;
        foreach (var arquivo in Arquivos(categoria).ToList())
        {
            long tamanho = Tamanho(arquivo);
            try
            {
                if (categoria == CategoriaLimpeza.DownloadsAntigos)
                    FileSystem.DeleteFile(arquivo, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                {
                    File.SetAttributes(arquivo, FileAttributes.Normal);
                    File.Delete(arquivo);
                }
                liberado += tamanho;
            }
            catch { /* em uso ou sem permissão: fica */ }
        }

        // Remove as pastas que ficaram vazias (sem apagar a pasta principal)
        foreach (var pasta in Pastas(categoria))
            foreach (var sub in SemErro(() => Directory.GetDirectories(pasta, "*", System.IO.SearchOption.AllDirectories)).OrderByDescending(p => p.Length))
                try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }

        return liberado;
    }

    // Os maiores arquivos da pasta do usuário (só para mostrar; nada é apagado)
    public static List<ArquivoGrande> MaioresArquivos(int quantidade, CancellationToken ct)
    {
        var maiores = new PriorityQueue<ArquivoGrande, long>();
        string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        try
        {
            foreach (var info in new DirectoryInfo(perfil).EnumerateFiles("*", opcoes))
            {
                if (ct.IsCancellationRequested) break;
                long tamanho;
                try { tamanho = info.Length; } catch { continue; }
                if (tamanho < 100L * 1024 * 1024) continue;
                maiores.Enqueue(new ArquivoGrande(info.FullName, tamanho, info.LastWriteTime), tamanho);
                if (maiores.Count > quantidade) maiores.Dequeue();
            }
        }
        catch { }
        var lista = new List<ArquivoGrande>();
        while (maiores.Count > 0) lista.Add(maiores.Dequeue());
        lista.Reverse();
        return lista;
    }

    // Usado pelo programa rodando como administrador: /limpar-atualizacoes
    public static int LimparAtualizacoesComoAdministrador()
    {
        try { Limpar(CategoriaLimpeza.AtualizacoesWindows); return 0; }
        catch { return 1; }
    }

    // ---------------- Lixeira (shell32) ----------------

    [StructLayout(LayoutKind.Sequential)]   // x64: alinhamento padrão (cbSize = 24)
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint flags);
}

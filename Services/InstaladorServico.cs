using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Instala/remove o serviço do monitor de rede. Chamado pelo
//  instalador (Inno Setup) com /instalar-servico e /remover-servico,
//  ou pela tela, pedindo permissão de administrador (UAC).
//
//  O serviço roda de uma CÓPIA do programa em
//  C:\ProgramData\L2Utilitarios\servico, protegida para só o Sistema e
//  administradores poderem alterar. Assim ele não trava os arquivos da
//  pasta de onde o programa foi aberto (ex.: o bin do projeto durante o
//  desenvolvimento) e ninguém sem admin consegue trocar o que roda como Sistema.
// ---------------------------------------------------------------
public static class InstaladorServico
{
    public const string Nome = "L2UtilitariosMonitor";
    const string NomeExibicao = "L2 Utilitarios - Monitor de rede";
    const string Descricao = "Conta o uso de internet por aplicativo para o L2 Utilitarios.";

    static readonly string PastaServico = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L2Utilitarios", "servico");

    public enum ResultadoInstalacao { Rodando, Recusado, Falhou }

    // Espera o serviço chegar em "rodando" (ele passa alguns segundos "iniciando")
    public static bool EsperarRodando(TimeSpan limite)
    {
        try
        {
            using var servico = new ServiceController(Nome);
            servico.WaitForStatus(ServiceControllerStatus.Running, limite);
            return true;
        }
        catch { return EstaRodando(); }
    }

    public static bool EstaRodando()
    {
        try
        {
            using var servico = new ServiceController(Nome);
            return servico.Status == ServiceControllerStatus.Running;
        }
        catch { return false; }   // não instalado
    }

    // Caminho do .exe que o serviço instalado executa (lido do registro; não precisa de admin)
    public static string? CaminhoInstalado()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{Nome}");
            if (chave?.GetValue("ImagePath") is not string imagem) return null;
            imagem = Environment.ExpandEnvironmentVariables(imagem.Trim());
            if (imagem.StartsWith('"'))
            {
                int fim = imagem.IndexOf('"', 1);
                return fim > 0 ? imagem[1..fim] : imagem.Trim('"');
            }
            int argumento = imagem.IndexOf(" /servico", StringComparison.OrdinalIgnoreCase);
            return argumento > 0 ? imagem[..argumento] : imagem;
        }
        catch { return null; }
    }

    // O serviço existe, mas roda uma versão diferente desta (outra pasta, ou a cópia ficou para trás)
    public static bool PrecisaAtualizar()
    {
        string? instalado = CaminhoInstalado();
        string? atual = Environment.ProcessPath;
        if (instalado is null || atual is null) return false;
        try
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(instalado))?.TrimEnd('\\'),
                               PastaServico.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return true;   // instalado do jeito antigo (rodando direto de outra pasta)

            // Mesma versão = mesmo .dll principal (tamanho e data)
            string dll = Path.GetFileNameWithoutExtension(atual) + ".dll";
            var origem = new FileInfo(Path.Combine(AppContext.BaseDirectory, dll));
            var copia = new FileInfo(Path.Combine(PastaServico, dll));
            if (!origem.Exists) return false;
            return !copia.Exists || origem.Length != copia.Length || origem.LastWriteTimeUtc != copia.LastWriteTimeUtc;
        }
        catch { return false; }
    }

    public static int Instalar()
    {
        Parar();
        string exe;
        try
        {
            exe = CopiarParaPastaDoServico();
        }
        catch (Exception ex)
        {
            LogMonitor.Registrar("Não foi possível copiar o programa para a pasta do serviço: " + ex.Message);
            return 1;
        }
        string binPath = $"binPath= \"\\\"{exe}\\\" /servico\"";

        if (Sc($"create {Nome} {binPath} start= delayed-auto DisplayName= \"{NomeExibicao}\"") != 0)
            Sc($"config {Nome} {binPath} start= delayed-auto DisplayName= \"{NomeExibicao}\"");   // já existia: atualiza o caminho
        Sc($"description {Nome} \"{Descricao}\"");
        Sc($"failure {Nome} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
        Sc($"start {Nome}");
        bool ok = EsperarRodando(TimeSpan.FromSeconds(30));
        LogMonitor.Registrar(ok ? $"Monitor instalado e rodando ({exe})." : "Monitor instalado, mas não chegou a rodar em 30 s.");
        return ok ? 0 : 1;
    }

    public static int Remover()
    {
        Parar();
        Sc($"delete {Nome}");
        try { if (Directory.Exists(PastaServico)) Directory.Delete(PastaServico, recursive: true); }
        catch { /* arquivo ainda em uso; fica para a próxima */ }
        return 0;
    }

    // Copia os arquivos do programa (pasta de onde ele está rodando) para a pasta do serviço
    static string CopiarParaPastaDoServico()
    {
        string origem = AppContext.BaseDirectory.TrimEnd('\\');
        string nomeExe = Path.GetFileName(Environment.ProcessPath ?? Application.ExecutablePath);
        Directory.CreateDirectory(PastaServico);
        ProtegerPasta(PastaServico);

        if (!string.Equals(origem, PastaServico.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var arquivo in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            {
                string destino = Path.Combine(PastaServico, Path.GetRelativePath(origem, arquivo));
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.Copy(arquivo, destino, overwrite: true);
                File.SetLastWriteTimeUtc(destino, File.GetLastWriteTimeUtc(arquivo));
            }
        }
        return Path.Combine(PastaServico, nomeExe);
    }

    // Só Sistema e administradores alteram; os demais usuários só leem e executam
    static void ProtegerPasta(string pasta)
    {
        var regras = new DirectorySecurity();
        regras.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags heranca = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        regras.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, heranca, PropagationFlags.None, AccessControlType.Allow));
        regras.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, heranca, PropagationFlags.None, AccessControlType.Allow));
        regras.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, heranca, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(pasta).SetAccessControl(regras);
    }

    static void Parar()
    {
        try
        {
            using var servico = new ServiceController(Nome);
            if (servico.Status != ServiceControllerStatus.Stopped)
            {
                servico.Stop();
                servico.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }
        }
        catch { /* não instalado ou já parado */ }
    }

    static int Sc(string argumentos)
    {
        try
        {
            using var processo = Process.Start(new ProcessStartInfo("sc.exe", argumentos)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (processo is null) return -1;
            string saida = (processo.StandardOutput.ReadToEnd() + processo.StandardError.ReadToEnd()).Trim();
            processo.WaitForExit();
            if (processo.ExitCode != 0)
                LogMonitor.Registrar($"sc {argumentos.Split(' ')[0]} terminou com código {processo.ExitCode}: " +
                                     string.Join(" ", saida.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())));
            return processo.ExitCode;
        }
        catch (Exception ex)
        {
            LogMonitor.Registrar($"Não foi possível rodar o sc.exe: {ex.Message}");
            return -1;
        }
    }

    // Usado pela tela: roda este .exe como administrador para instalar o serviço
    public static async Task<ResultadoInstalacao> InstalarComPermissaoAsync()
    {
        try
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            using var processo = Process.Start(new ProcessStartInfo(exe, "/instalar-servico")
            {
                UseShellExecute = true, Verb = "runas"
            });
            if (processo is null) return ResultadoInstalacao.Falhou;
            await processo.WaitForExitAsync();
            bool rodando = await Task.Run(() => EsperarRodando(TimeSpan.FromSeconds(15)));
            return rodando ? ResultadoInstalacao.Rodando : ResultadoInstalacao.Falhou;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ResultadoInstalacao.Recusado;   // a pessoa recusou o pedido de administrador
        }
    }
}

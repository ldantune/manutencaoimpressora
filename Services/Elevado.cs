using System.Diagnostics;

namespace ManutencaoImpressora.Services;

// Roda este mesmo programa como administrador (pede permissão ao Windows) para uma tarefa pontual
public static class Elevado
{
    public static async Task<bool> ExecutarAsync(string argumentos)
    {
        try
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            using var processo = Process.Start(new ProcessStartInfo(exe, argumentos) { UseShellExecute = true, Verb = "runas" });
            if (processo is null) return false;
            await processo.WaitForExitAsync();
            return processo.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }   // pedido recusado
    }
}

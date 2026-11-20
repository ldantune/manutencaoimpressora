using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Leituras do computador para a tela "Saúde do PC": uso do
//  processador e da memória, discos, tempo ligado e dados do sistema.
//  Usa APIs do Windows direto (sem precisar de administrador).
// ---------------------------------------------------------------
public sealed class InfoSistema
{
    long _ociosoAnterior, _kernelAnterior, _usuarioAnterior;

    // Uso do processador (0–100) desde a última chamada
    public double UsoProcessador()
    {
        if (!GetSystemTimes(out var ocioso, out var kernel, out var usuario)) return 0;
        long o = ocioso.Valor, k = kernel.Valor, u = usuario.Valor;
        long dOcioso = o - _ociosoAnterior, dTotal = (k - _kernelAnterior) + (u - _usuarioAnterior);
        bool primeira = _kernelAnterior == 0;
        (_ociosoAnterior, _kernelAnterior, _usuarioAnterior) = (o, k, u);
        if (primeira || dTotal <= 0) return 0;
        return Math.Clamp((1 - (double)dOcioso / dTotal) * 100, 0, 100);
    }

    public static (ulong Total, ulong Livre) Memoria()
    {
        var estado = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref estado) ? (estado.ullTotalPhys, estado.ullAvailPhys) : (0, 0);
    }

    public static TimeSpan TempoLigado() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public static List<DriveInfo> Discos() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();

    public static string VersaoWindows()
    {
        try
        {
            using var chave = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string nome = chave?.GetValue("ProductName") as string ?? "Windows";
            string versao = chave?.GetValue("DisplayVersion") as string ?? "";
            // O registro continua dizendo "Windows 10" no Windows 11; o número da compilação resolve
            if (Environment.OSVersion.Version.Build >= 22000) nome = nome.Replace("Windows 10", "Windows 11");
            return $"{nome} {versao}".Trim();
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    public static string Processador()
    {
        try
        {
            using var chave = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string nome = (chave?.GetValue("ProcessorNameString") as string ?? "Processador").Trim();
            return $"{nome} · {Environment.ProcessorCount} núcleos lógicos";
        }
        catch { return $"{Environment.ProcessorCount} núcleos lógicos"; }
    }

    // Data da atualização do Windows mais recente instalada (WMI; pode levar alguns segundos)
    public static DateTime? UltimaAtualizacaoWindows()
    {
        try
        {
            DateTime? maior = null;
            using var busca = new ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
            foreach (ManagementObject item in busca.Get())
            {
                using (item)
                {
                    if (item["InstalledOn"] is string texto &&
                        DateTime.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var data) &&
                        (maior is null || data > maior))
                        maior = data;
                }
            }
            return maior;
        }
        catch { return null; }
    }

    // ---------------- APIs do Windows ----------------

    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME
    {
        public uint Baixo, Alto;
        public long Valor => ((long)Alto << 32) | Baixo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FILETIME ocioso, out FILETIME kernel, out FILETIME usuario);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX estado);
}

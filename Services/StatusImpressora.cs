using System.Management;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Consulta via WMI se a impressora está acessível (ligada e
//  conectada). Se não conseguir verificar, assume que está tudo bem,
//  para não exibir um aviso falso.
// ---------------------------------------------------------------
public static class StatusImpressora
{
    public static bool EstaOnline(string nomeImpressora)
    {
        if (string.IsNullOrWhiteSpace(nomeImpressora)) return true;

        try
        {
            string nomeEscapado = nomeImpressora.Replace("'", "''");
            using var pesquisa = new ManagementObjectSearcher(
                $"SELECT WorkOffline FROM Win32_Printer WHERE Name = '{nomeEscapado}'");
            foreach (ManagementObject impressora in pesquisa.Get())
                return !(bool)impressora["WorkOffline"];
        }
        catch { /* não conseguiu verificar: assume que está tudo bem */ }

        return true;
    }
}

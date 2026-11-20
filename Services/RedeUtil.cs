using System.Net.NetworkInformation;

namespace ManutencaoImpressora.Services;

// Regras comuns sobre placas de rede (usadas pela tela e pelo serviço)
public static class RedeUtil
{
    // Placas virtuais (Hyper-V, WSL, VPNs virtuais...) duplicariam a contagem da placa real
    public static bool EhVirtual(NetworkInterface ni) =>
        ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || ni.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase);

    public static bool ContaParaUso(NetworkInterface ni) =>
        ni.OperationalStatus == OperationalStatus.Up
        && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
        && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel
        && !EhVirtual(ni);
}

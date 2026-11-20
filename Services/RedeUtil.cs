using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

    // A conexão que leva à internet: placa real, com IPv4 e gateway padrão
    public static (NetworkInterface Placa, IPAddress Ip, IPAddress Mascara, IPAddress? Gateway)? ConexaoPrincipal()
    {
        try
        {
            var candidatas = NetworkInterface.GetAllNetworkInterfaces().Where(ContaParaUso)
                .Select(placa =>
                {
                    var propriedades = placa.GetIPProperties();
                    var endereco = propriedades.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                    var gateway = propriedades.GatewayAddresses.Select(g => g.Address)
                        .FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any));
                    return (placa, endereco, gateway);
                })
                .Where(c => c.endereco != null)
                .OrderByDescending(c => c.gateway != null)
                .ThenByDescending(c => c.placa.Speed)
                .ToList();
            if (candidatas.Count == 0) return null;
            var (escolhida, ip, gw) = candidatas[0];
            return (escolhida, ip!.Address, ip.IPv4Mask, gw);
        }
        catch { return null; }
    }

    public static IPAddress? GatewayPadrao() => ConexaoPrincipal()?.Gateway;
}

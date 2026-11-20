using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ManutencaoImpressora.Services;

public record DispositivoEncontrado(IPAddress Ip, string Mac, string? Nome, bool EsteComputador, bool Roteador);

// ---------------------------------------------------------------
//  Procura os aparelhos da rede local: manda um ping para cada
//  endereço da faixa (até 254) e depois lê a tabela ARP do Windows,
//  que liga cada IP que respondeu ao endereço físico (MAC) do aparelho.
//  Não precisa de administrador.
// ---------------------------------------------------------------
public static partial class ScannerRede
{
    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})")]
    private static partial Regex LinhaArp();

    public static async Task<(List<DispositivoEncontrado> Dispositivos, string Rede)> ProcurarAsync(
        IProgress<int>? progresso, CancellationToken ct)
    {
        var conexao = RedeUtil.ConexaoPrincipal()
                      ?? throw new InvalidOperationException("o computador não está conectado a nenhuma rede");
        var (placa, ip, mascara, gateway) = conexao;

        uint ipNum = Numero(ip), mascaraNum = Numero(mascara);
        // Redes muito grandes (ex.: /16 de empresa): só a faixa /24 do próprio computador
        if (BitOperations.PopCount(mascaraNum) < 24) mascaraNum = 0xFFFFFF00;
        uint rede = ipNum & mascaraNum, broadcast = rede | ~mascaraNum;

        var alvos = new List<IPAddress>();
        for (uint n = rede + 1; n < broadcast; n++) alvos.Add(Endereco(n));

        // 1) Ping em todos (64 por vez) só para os aparelhos entrarem na tabela ARP
        int feitos = 0;
        using var limite = new SemaphoreSlim(64);
        await Task.WhenAll(alvos.Select(async alvo =>
        {
            await limite.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                await ping.SendPingAsync(alvo, 800);
            }
            catch { /* sem resposta */ }
            finally
            {
                limite.Release();
                progresso?.Report(Interlocked.Increment(ref feitos) * 90 / alvos.Count);
            }
        }));

        // 2) Tabela ARP: IP -> MAC
        var tabela = (await LerTabelaArpAsync(ip))
            .Where(e => (Numero(e.Ip) & mascaraNum) == rede && Numero(e.Ip) != broadcast && !e.Ip.Equals(ip))
            .GroupBy(e => e.Mac).Select(g => g.First())
            .ToList();

        // 3) Nomes (DNS/NetBIOS do Windows), com tempo limite
        var nomes = await Task.WhenAll(tabela.Select(e => NomeAsync(e.Ip)));
        progresso?.Report(100);

        var lista = new List<DispositivoEncontrado>
        {
            new(ip, FormatarMac(placa.GetPhysicalAddress()), Environment.MachineName, EsteComputador: true, Roteador: false)
        };
        lista.AddRange(tabela.Select((e, i) => new DispositivoEncontrado(e.Ip, e.Mac, nomes[i], false, e.Ip.Equals(gateway))));
        lista = lista.OrderByDescending(d => d.Roteador).ThenByDescending(d => d.EsteComputador).ThenBy(d => Numero(d.Ip)).ToList();

        string descricao = $"Rede {Endereco(rede)}/{BitOperations.PopCount(mascaraNum)} · {placa.Name}" +
                           (gateway is null ? "" : $" · roteador {gateway}");
        return (lista, descricao);
    }

    static async Task<List<(IPAddress Ip, string Mac)>> LerTabelaArpAsync(IPAddress ipDaPlaca)
    {
        var lista = new List<(IPAddress, string)>();
        using var processo = Process.Start(new ProcessStartInfo("arp", $"-a -N {ipDaPlaca}")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        });
        if (processo is null) return lista;
        string saida = await processo.StandardOutput.ReadToEndAsync();
        await processo.WaitForExitAsync();

        foreach (Match m in LinhaArp().Matches(saida))
        {
            string mac = m.Groups[2].Value.ToUpperInvariant().Replace('-', ':');
            // Ignora broadcast e endereços de grupo (multicast)
            if (mac == "FF:FF:FF:FF:FF:FF" || mac.StartsWith("01:00:5E") || mac.StartsWith("33:33")) continue;
            if (IPAddress.TryParse(m.Groups[1].Value, out var endereco)) lista.Add((endereco, mac));
        }
        return lista;
    }

    static async Task<string?> NomeAsync(IPAddress ip)
    {
        try
        {
            var busca = Dns.GetHostEntryAsync(ip);
            if (await Task.WhenAny(busca, Task.Delay(2_000)) != busca || busca.IsFaulted) return null;
            string nome = busca.Result.HostName;
            if (string.IsNullOrWhiteSpace(nome) || nome == ip.ToString()) return null;
            return nome.Split('.')[0];
        }
        catch { return null; }
    }

    public static string FormatarMac(PhysicalAddress endereco) =>
        string.Join(":", endereco.GetAddressBytes().Select(b => b.ToString("X2")));

    static uint Numero(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    static IPAddress Endereco(uint n) => new(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });
}

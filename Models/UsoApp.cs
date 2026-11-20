namespace ManutencaoImpressora.Models;

// Uso de internet de um aplicativo num período (bytes recebidos = download)
public record UsoApp(string App, string Caminho, long Recebidos, long Enviados)
{
    public long Total => Recebidos + Enviados;
}

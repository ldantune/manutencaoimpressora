namespace ManutencaoImpressora.Models;

// Uso de internet de um dia (bytes recebidos = download, enviados = upload)
public record UsoDia(DateTime Data, long Recebidos, long Enviados)
{
    public long Total => Recebidos + Enviados;
}

namespace ManutencaoImpressora.Views.Abas;

// Telas que avisam algo pela bandeja do sistema (ex.: "Internet caiu")
public interface INotificador
{
    event Action<string, string, ToolTipIcon>? Notificar;
}

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Contrato que toda aba do painel implementa, para a janela
//  principal saber qual altura reservar para a aba selecionada
// ---------------------------------------------------------------
public interface IAbaUtilitario
{
    int AlturaPreferida { get; }
    event EventHandler? AlturaMudou;
}

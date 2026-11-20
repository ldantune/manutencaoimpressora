namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Panel com buffer duplo que se repinta inteiro ao mudar de tamanho
//  (evita "restos" de pintura, como linhas soltas ao maximizar)
// ---------------------------------------------------------------
public class PainelSuave : Panel
{
    public PainelSuave()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }
}

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Cartão branco com cantos arredondados; empilha os controles
//  um embaixo do outro (uma linha por controle)
// ---------------------------------------------------------------
public class CardPanel : TableLayoutPanel
{
    public CardPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Tema.Card;
        Padding = new Padding(16, 14, 16, 16);
        Margin = new Padding(0, 0, 0, 12);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public void AdicionarLinha(Control c)
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowCount = RowStyles.Count;
        Controls.Add(c, 0, RowCount - 1);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Tema.Fundo);
        Tema.Caixa(e.Graphics, ClientRectangle, 8 * Tema.Escala(this), BackColor, Tema.BordaCard);
    }
}

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Linha de resumo do agendamento, com ícone de calendário
// ---------------------------------------------------------------
public class CaixaResumo : Control
{
    public CaixaResumo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 40;
        Margin = new Padding(0, 4, 0, 0);
        AccessibleRole = AccessibleRole.StaticText;
    }

    Rectangle AreaTexto()
    {
        float s = Tema.Escala(this);
        int esquerda = (int)Math.Round(40 * s);
        return new Rectangle(esquerda, (int)Math.Round(10 * s), Math.Max(1, Width - esquerda - (int)Math.Round(12 * s)), 0);
    }

    void AjustarAltura()
    {
        if (!IsHandleCreated) return;
        var area = AreaTexto();
        int h = TextRenderer.MeasureText(Text, Tema.Corpo, new Size(area.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        int nova = area.Y * 2 + h;
        if (Height != nova) Height = nova;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        AjustarAltura();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AjustarAltura(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); AjustarAltura(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);
        Tema.Caixa(g, ClientRectangle, 6 * s, Tema.ResumoFundo);

        var area = AreaTexto();
        int hLinha = TextRenderer.MeasureText("Ag", Tema.Corpo, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, Icones.Calendario, Tema.Icone,
            new Rectangle((int)Math.Round(14 * s), area.Y, (int)Math.Round(20 * s), hLinha),
            Tema.Destaque, TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, Text, Tema.Corpo, new Rectangle(area.X, area.Y, area.Width, Height - area.Y),
            Tema.Texto, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left);
    }
}

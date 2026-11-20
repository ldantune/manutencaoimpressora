namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Abinha de texto (SelectorBar do Windows 11), ex.: "Ao vivo" e
//  "Relatório". É um RadioButton: as setas trocam de opção.
// ---------------------------------------------------------------
public class BotaoPivo : RadioButton
{
    bool _hover;

    public BotaoPivo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Margin = new Padding(0, 0, 4, 0);
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        float s = Tema.Escala(this);
        var t = TextRenderer.MeasureText(Text, Tema.CorpoForte, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(t.Width + (int)Math.Round(24 * s), (int)Math.Round(36 * s));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float s = Tema.Escala(this);
        int alturaTexto = Height - (int)Math.Round(6 * s);

        if (_hover && !Checked)
            Tema.Caixa(g, new Rectangle(0, 0, Width, alturaTexto), 4 * s, Color.FromArgb(234, 234, 234));

        TextRenderer.DrawText(g, Text, Checked ? Tema.CorpoForte : Font, new Rectangle(0, 0, Width, alturaTexto),
            Checked ? Tema.Texto : Tema.TextoSecundario,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Checked)
        {
            int largura = (int)Math.Round(16 * s), altura = (int)Math.Round(3 * s);
            Tema.Caixa(g, new Rectangle((Width - largura) / 2, Height - altura - (int)Math.Round(1 * s), largura, altura),
                altura / 2f, Tema.Destaque);
        }

        if (Focused && ShowFocusCues)
            Tema.AnelFoco(g, this, 4 * s);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

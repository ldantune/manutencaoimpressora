namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Dia da semana como botão liga/desliga (continua sendo um
//  CheckBox: espaço alterna, leitores de tela entendem)
// ---------------------------------------------------------------
public class BotaoDia : CheckBox
{
    bool _hover;

    public BotaoDia()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 34;
        Dock = DockStyle.Fill;
        Margin = new Padding(3, 0, 3, 0);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);

        Color fundo, texto;
        Color? borda;
        if (Checked)
        {
            fundo = _hover ? Tema.DestaqueHover : Tema.Destaque;
            borda = fundo; texto = Color.White;
        }
        else
        {
            fundo = _hover ? Tema.BotaoHover : Tema.BotaoFundo;
            borda = Tema.BotaoBorda; texto = Tema.Texto;
        }

        Tema.Caixa(g, ClientRectangle, 4 * s, fundo, borda);
        TextRenderer.DrawText(g, Text, Checked ? Tema.CorpoForte : Font, ClientRectangle, texto,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
            Tema.AnelFoco(g, this, 4 * s);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

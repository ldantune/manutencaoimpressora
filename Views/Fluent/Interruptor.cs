namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Interruptor liga/desliga (ToggleSwitch do Windows 11)
// ---------------------------------------------------------------
public class Interruptor : CheckBox
{
    bool _hover;

    public Interruptor()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(44, 24);
        Margin = new Padding(0);
        Cursor = Cursors.Hand;
        Text = "";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);

        int largura = (int)Math.Round(40 * s), altura = (int)Math.Round(20 * s);
        var trilho = new Rectangle((Width - largura) / 2, (Height - altura) / 2, largura, altura);

        if (Checked)
            Tema.Caixa(g, trilho, altura / 2f, _hover ? Tema.DestaqueHover : Tema.Destaque);
        else
            Tema.Caixa(g, trilho, altura / 2f, _hover ? Tema.BotaoHover : Color.White, Tema.BordaControle);

        float bola = (_hover ? 14 : 12) * s;
        float margem = (altura - bola) / 2f;
        float x = Checked ? trilho.Right - margem - bola - 1 : trilho.X + margem + 1;
        using (var b = new SolidBrush(Checked ? Color.White : Tema.TextoSecundario))
            g.FillEllipse(b, x, trilho.Y + margem, bola, bola);

        if (Focused && ShowFocusCues)
            Tema.AnelFoco(g, this, Height / 2f);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

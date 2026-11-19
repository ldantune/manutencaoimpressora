namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Item do menu lateral (NavigationView do Windows 11): ícone +
//  texto, com a barrinha azul no item selecionado. É um RadioButton,
//  então as setas do teclado trocam de item e leitores de tela
//  entendem que só um fica selecionado.
// ---------------------------------------------------------------
public class BotaoNavegacao : RadioButton
{
    static readonly Color FundoSelecionado = Color.FromArgb(229, 229, 229);
    static readonly Color FundoHover = Color.FromArgb(234, 234, 234);
    static readonly Color FundoSelecionadoHover = Color.FromArgb(222, 222, 222);

    bool _hover;
    string _icone = "";

    public string Icone { get => _icone; set { _icone = value; Invalidate(); } }

    public BotaoNavegacao()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 40;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float s = Tema.Escala(this);

        var area = new Rectangle(0, (int)Math.Round(2 * s), Width, Height - (int)Math.Round(4 * s));
        if (Checked || _hover)
        {
            var fundo = Checked ? (_hover ? FundoSelecionadoHover : FundoSelecionado) : FundoHover;
            Tema.Caixa(g, area, 4 * s, fundo);
        }

        if (Checked)
        {
            int alturaBarra = (int)Math.Round(16 * s);
            var barra = new Rectangle(area.X, (Height - alturaBarra) / 2, (int)Math.Round(3 * s), alturaBarra);
            Tema.Caixa(g, barra, 1.5f * s, Tema.Destaque);
        }

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (_icone.Length > 0)
            TextRenderer.DrawText(g, _icone, Tema.Icone,
                new Rectangle((int)Math.Round(14 * s), 0, (int)Math.Round(20 * s), Height), Tema.Texto, flags);

        int xTexto = (int)Math.Round(46 * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(xTexto, 0, Width - xTexto - (int)Math.Round(8 * s), Height),
            Tema.Texto, flags);

        if (Focused && ShowFocusCues)
            Tema.AnelFoco(g, this, 4 * s);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

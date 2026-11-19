namespace ManutencaoImpressora.Views.Fluent;

public enum EstiloBotao { Padrao, Primario, Sutil }

// ---------------------------------------------------------------
//  Botão no estilo Windows 11 (padrão, destaque ou sutil),
//  com ícone opcional à esquerda do texto
// ---------------------------------------------------------------
public class BotaoFluent : Button
{
    bool _hover, _pressionado;
    EstiloBotao _estilo;
    string? _icone;

    public EstiloBotao Estilo { get => _estilo; set { _estilo = value; Invalidate(); } }
    public string? Icone { get => _icone; set { _icone = value; Invalidate(); } }

    public BotaoFluent()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        float s = Tema.Escala(this);
        bool temTexto = !string.IsNullOrEmpty(Text);
        int altura = (int)Math.Round(32 * s);
        if (!temTexto) return new Size(altura, altura);

        int largura = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width
                      + (int)Math.Round(2 * (_estilo == EstiloBotao.Sutil ? 10 : 14) * s);
        if (_icone != null) largura += (int)Math.Round(24 * s);
        return new Size(Math.Max(largura, (int)Math.Round(88 * s)), altura);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);

        Color fundo, texto;
        Color? borda;
        if (!Enabled)
        {
            fundo = Color.FromArgb(245, 245, 245); borda = Tema.BordaCard; texto = Tema.TextoDesabilitado;
        }
        else if (_estilo == EstiloBotao.Primario)
        {
            fundo = _pressionado ? Tema.DestaquePressionado : _hover ? Tema.DestaqueHover : Tema.Destaque;
            borda = fundo; texto = Color.White;
        }
        else if (_estilo == EstiloBotao.Sutil)
        {
            fundo = _pressionado ? Tema.BotaoPressionado : _hover ? Tema.BotaoHover : (Parent?.BackColor ?? Tema.Card);
            borda = null; texto = Tema.Texto;
        }
        else
        {
            fundo = _pressionado ? Tema.BotaoPressionado : _hover ? Tema.BotaoHover : Tema.BotaoFundo;
            borda = Tema.BotaoBorda; texto = Tema.Texto;
        }

        Tema.Caixa(g, ClientRectangle, 4 * s, fundo, borda);

        // Ícone + texto centralizados
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.Left | TextFormatFlags.SingleLine;
        bool temTexto = !string.IsNullOrEmpty(Text);
        Size tIcone = _icone != null ? TextRenderer.MeasureText(_icone, Tema.Icone, Size.Empty, flags) : Size.Empty;
        Size tTexto = temTexto ? TextRenderer.MeasureText(Text, Font, Size.Empty, flags) : Size.Empty;
        int espaco = _icone != null && temTexto ? (int)Math.Round(8 * s) : 0;
        int x = (Width - (tIcone.Width + espaco + tTexto.Width)) / 2;

        if (_icone != null)
        {
            TextRenderer.DrawText(g, _icone, Tema.Icone, new Rectangle(x, 0, tIcone.Width, Height), texto, flags);
            x += tIcone.Width + espaco;
        }
        if (temTexto)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, tTexto.Width + 2, Height), texto, flags);

        if (Focused && ShowFocusCues)
            Tema.AnelFoco(g, this, 4 * s);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressionado = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressionado = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _pressionado = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
}

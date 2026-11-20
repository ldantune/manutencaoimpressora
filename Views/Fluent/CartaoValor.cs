namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Cartão de número: título, período, valor grande e um detalhe
//  (ex.: "Quedas hoje · 19/11 · 2 · 4 min sem internet")
// ---------------------------------------------------------------
public class CartaoValor : Control
{
    string _titulo = "", _periodo = "", _valor = "—", _detalhe = "";
    Color _corValor = Tema.Texto;

    public CartaoValor()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 104;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public void Definir(string titulo, string periodo, string valor, string detalhe, Color? corValor = null)
    {
        _titulo = titulo; _periodo = periodo; _valor = valor; _detalhe = detalhe;
        _corValor = corValor ?? Tema.Texto;
        AccessibleName = $"{titulo} ({periodo}): {valor}. {detalhe}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float s = Tema.Escala(this);
        Tema.Caixa(g, ClientRectangle, 8 * s, Tema.Card, Tema.BordaCard);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        int margem = (int)Math.Round(14 * s), largura = Width - margem * 2, y = (int)Math.Round(12 * s);

        int hTitulo = TextRenderer.MeasureText("Ag", Tema.CorpoForte, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, _titulo, Tema.CorpoForte, new Rectangle(margem, y, largura, hTitulo), Tema.Texto, flags | TextFormatFlags.Left);
        TextRenderer.DrawText(g, _periodo, Tema.Legenda, new Rectangle(margem, y, largura, hTitulo), Tema.TextoSecundario, flags | TextFormatFlags.Right);
        y += hTitulo + (int)Math.Round(6 * s);

        int hValor = TextRenderer.MeasureText("Ag", Tema.Numero, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, _valor, Tema.Numero, new Rectangle(margem, y, largura, hValor), _corValor, flags | TextFormatFlags.Left);
        y += hValor + (int)Math.Round(6 * s);

        int hDetalhe = TextRenderer.MeasureText("Ag", Tema.Legenda, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, _detalhe, Tema.Legenda, new Rectangle(margem, y, largura, hDetalhe), Tema.TextoSecundario, flags | TextFormatFlags.Left);
    }
}

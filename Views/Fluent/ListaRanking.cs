using ManutencaoImpressora.Services;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Lista curta "rótulo — barrinha — valor", ex.: dias de maior uso
// ---------------------------------------------------------------
public class ListaRanking : Control
{
    const int AlturaLinha = 32;

    List<(string Rotulo, long Valor)> _itens = new();
    public string TextoVazio { get; set; } = "Sem dados ainda.";

    public ListaRanking()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Card;
        Height = AlturaLinha * 5;
        AccessibleRole = AccessibleRole.List;
    }

    public void Definir(List<(string Rotulo, long Valor)> itens)
    {
        _itens = itens;
        float s = Tema.Escala(this);
        Height = (int)Math.Round(Math.Max(1, itens.Count) * AlturaLinha * s);
        AccessibleDescription = string.Join("; ", itens.Select(i => $"{i.Rotulo}: {FormatoBytes.Formatar(i.Valor)}"));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);

        if (_itens.Count == 0)
        {
            TextRenderer.DrawText(g, TextoVazio, Tema.Corpo, ClientRectangle, Tema.TextoSecundario,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        long maior = Math.Max(1, _itens.Max(i => i.Valor));
        int hLinha = (int)Math.Round(AlturaLinha * s);
        int larguraRotulo = (int)Math.Round(120 * s);
        int larguraValor = (int)Math.Round(80 * s);
        int espaco = (int)Math.Round(12 * s);
        int larguraBarra = Width - larguraRotulo - larguraValor - espaco * 2;
        int alturaBarra = (int)Math.Round(6 * s);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

        for (int i = 0; i < _itens.Count; i++)
        {
            var (rotulo, valor) = _itens[i];
            int y = i * hLinha;
            TextRenderer.DrawText(g, rotulo, Tema.Corpo, new Rectangle(0, y, larguraRotulo, hLinha), Tema.Texto, flags | TextFormatFlags.Left);

            if (larguraBarra > 0)
            {
                int xBarra = larguraRotulo + espaco;
                int yBarra = y + (hLinha - alturaBarra) / 2;
                Tema.Caixa(g, new Rectangle(xBarra, yBarra, larguraBarra, alturaBarra), alturaBarra / 2f, Color.FromArgb(237, 237, 237));
                int preenchido = Math.Max(alturaBarra, (int)(larguraBarra * (valor / (double)maior)));
                Tema.Caixa(g, new Rectangle(xBarra, yBarra, preenchido, alturaBarra), alturaBarra / 2f, Tema.CorDownload);
            }

            TextRenderer.DrawText(g, FormatoBytes.Formatar(valor), Tema.Corpo,
                new Rectangle(Width - larguraValor, y, larguraValor, hLinha), Tema.Texto, flags | TextFormatFlags.Right);
        }
    }
}

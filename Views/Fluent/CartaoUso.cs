using System.Drawing.Drawing2D;
using ManutencaoImpressora.Services;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Cartão de resumo de uso: título, período, total, barrinha com a
//  proporção download/upload e os dois valores embaixo
// ---------------------------------------------------------------
public class CartaoUso : Control
{
    string _titulo = "", _periodo = "";
    long _recebidos, _enviados;

    public CartaoUso()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 116;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public void Definir(string titulo, string periodo, long recebidos, long enviados)
    {
        _titulo = titulo;
        _periodo = periodo;
        _recebidos = recebidos;
        _enviados = enviados;
        AccessibleName = $"{titulo} ({periodo}): {FormatoBytes.Formatar(recebidos + enviados)}, " +
                         $"download {FormatoBytes.Formatar(recebidos)}, upload {FormatoBytes.Formatar(enviados)}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float s = Tema.Escala(this);
        Tema.Caixa(g, ClientRectangle, 8 * s, Tema.Card, Tema.BordaCard);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        int margem = (int)Math.Round(14 * s);
        int largura = Width - margem * 2;
        int y = (int)Math.Round(12 * s);

        // Título e período
        int hTitulo = TextRenderer.MeasureText("Ag", Tema.CorpoForte, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, _titulo, Tema.CorpoForte, new Rectangle(margem, y, largura, hTitulo), Tema.Texto,
            flags | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _periodo, Tema.Legenda, new Rectangle(margem, y, largura, hTitulo), Tema.TextoSecundario,
            flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        y += hTitulo + (int)Math.Round(6 * s);

        // Total
        long total = _recebidos + _enviados;
        string textoTotal = FormatoBytes.Formatar(total);
        int hTotal = TextRenderer.MeasureText(textoTotal, Tema.Numero, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, textoTotal, Tema.Numero, new Rectangle(margem, y, largura, hTotal), Tema.Texto, flags);
        y += hTotal + (int)Math.Round(8 * s);

        // Barrinha download/upload
        var barra = new RectangleF(margem, y, largura, 6 * s);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var caminho = Tema.Arredondado(barra, barra.Height / 2))
        {
            using (var trilho = new SolidBrush(Color.FromArgb(237, 237, 237))) g.FillPath(trilho, caminho);
            if (total > 0)
            {
                var clipAnterior = g.Clip;
                g.SetClip(caminho, CombineMode.Intersect);
                float wDown = barra.Width * _recebidos / total;
                using (var b = new SolidBrush(Tema.CorDownload)) g.FillRectangle(b, barra.X, barra.Y, wDown, barra.Height);
                using (var b = new SolidBrush(Tema.CorUpload)) g.FillRectangle(b, barra.X + wDown, barra.Y, barra.Width - wDown, barra.Height);
                g.Clip = clipAnterior;
            }
        }
        y += (int)Math.Round(14 * s);

        // ↓ download   ↑ upload
        int x = margem;
        int hRodape = TextRenderer.MeasureText("Ag", Tema.Legenda, Size.Empty, TextFormatFlags.NoPadding).Height;
        void Parte(string texto, Font fonte, Color cor, int espacoDepois)
        {
            int w = TextRenderer.MeasureText(texto, fonte, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, texto, fonte, new Rectangle(x, y, w + 2, hRodape), cor, flags | TextFormatFlags.VerticalCenter);
            x += w + espacoDepois;
        }
        Parte("↓", Tema.CorpoForte, Tema.CorDownload, (int)Math.Round(4 * s));
        Parte(FormatoBytes.Formatar(_recebidos), Tema.Legenda, Tema.Texto, (int)Math.Round(14 * s));
        Parte("↑", Tema.CorpoForte, Tema.CorUploadTexto, (int)Math.Round(4 * s));
        Parte(FormatoBytes.Formatar(_enviados), Tema.Legenda, Tema.Texto, 0);
    }
}

using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Lista de aplicativos com o uso de internet de cada um: ícone do
//  programa, nome, total, barrinha proporcional e download/upload
// ---------------------------------------------------------------
public class ListaApps : Control
{
    const int AlturaLinha = 52;

    List<UsoApp> _itens = new();
    readonly Dictionary<string, Bitmap?> _icones = new(StringComparer.OrdinalIgnoreCase);
    public string TextoVazio { get; set; } = "Nenhum uso registrado neste período.";

    public ListaApps()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Card;
        Height = 60;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Uso de internet por aplicativo";
    }

    public void Definir(List<UsoApp> itens)
    {
        _itens = itens;
        float s = Tema.Escala(this);
        Height = itens.Count == 0 ? (int)Math.Round(60 * s) : (int)Math.Round(itens.Count * AlturaLinha * s);
        AccessibleDescription = string.Join("; ", itens.Take(10).Select(i => $"{i.App}: {FormatoBytes.Formatar(i.Total)}"));
        Invalidate();
    }

    Bitmap? Icone(string caminho)
    {
        if (string.IsNullOrEmpty(caminho)) return null;
        if (_icones.TryGetValue(caminho, out var pronto)) return pronto;
        Bitmap? bmp = null;
        try
        {
            string extensao = Path.GetExtension(caminho).ToLowerInvariant();
            if (extensao is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                // Logo de app da Microsoft Store (copia para não deixar o arquivo preso)
                using var imagem = Image.FromFile(caminho);
                bmp = new Bitmap(imagem);
            }
            else
            {
                using var icone = Icon.ExtractAssociatedIcon(caminho);
                bmp = icone?.ToBitmap();
            }
        }
        catch { /* sem acesso ao arquivo */ }
        _icones[caminho] = bmp;
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);

        if (_itens.Count == 0)
        {
            TextRenderer.DrawText(g, TextoVazio, Tema.Corpo, ClientRectangle, Tema.TextoSecundario,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        long maior = Math.Max(1, _itens.Max(i => i.Total));
        int hLinha = (int)Math.Round(AlturaLinha * s);
        int tamIcone = (int)Math.Round(24 * s);
        int xTexto = tamIcone + (int)Math.Round(14 * s);
        int larguraValores = (int)Math.Round(170 * s);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        using var separador = new Pen(Color.FromArgb(240, 240, 240));

        for (int i = 0; i < _itens.Count; i++)
        {
            var item = _itens[i];
            int y = i * hLinha;
            if (y > e.ClipRectangle.Bottom || y + hLinha < e.ClipRectangle.Top) continue;

            // Ícone (ou a inicial do nome, quando não dá para ler o ícone)
            var area = new Rectangle(0, y + (hLinha - tamIcone) / 2, tamIcone, tamIcone);
            var bmp = Icone(item.Caminho);
            if (bmp != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, area);
            }
            else
            {
                Tema.Caixa(g, area, 6 * s, Tema.ResumoFundo);
                TextRenderer.DrawText(g, item.App.Length > 0 ? item.App[..1].ToUpperInvariant() : "?", Tema.CorpoForte,
                    area, Tema.Destaque, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Linha 1: nome ............ total
            int linha1 = y + (int)Math.Round(8 * s);
            int hTexto = (int)Math.Round(20 * s);
            TextRenderer.DrawText(g, item.App, Tema.CorpoForte,
                new Rectangle(xTexto, linha1, Width - xTexto - larguraValores, hTexto), Tema.Texto, flags | TextFormatFlags.Left);
            TextRenderer.DrawText(g, FormatoBytes.Formatar(item.Total), Tema.Corpo,
                new Rectangle(Width - larguraValores, linha1, larguraValores, hTexto), Tema.Texto, flags | TextFormatFlags.Right);

            // Linha 2: barrinha proporcional ........ ↓ x · ↑ y
            int linha2 = y + (int)Math.Round(32 * s);
            int larguraBarra = Width - xTexto - larguraValores - (int)Math.Round(16 * s);
            if (larguraBarra > 0)
            {
                int alturaBarra = (int)Math.Round(4 * s);
                int yBarra = linha2 + ((int)Math.Round(14 * s) - alturaBarra) / 2;
                Tema.Caixa(g, new Rectangle(xTexto, yBarra, larguraBarra, alturaBarra), alturaBarra / 2f, Color.FromArgb(237, 237, 237));
                int preenchido = Math.Max(alturaBarra, (int)(larguraBarra * (item.Total / (double)maior)));
                Tema.Caixa(g, new Rectangle(xTexto, yBarra, preenchido, alturaBarra), alturaBarra / 2f, Tema.CorDownload);
            }
            TextRenderer.DrawText(g, $"↓ {FormatoBytes.Formatar(item.Recebidos)}  ·  ↑ {FormatoBytes.Formatar(item.Enviados)}",
                Tema.Legenda, new Rectangle(Width - larguraValores, linha2, larguraValores, (int)Math.Round(14 * s)),
                Tema.TextoSecundario, flags | TextFormatFlags.Right);

            if (i < _itens.Count - 1)
                g.DrawLine(separador, xTexto, y + hLinha - 1, Width, y + hLinha - 1);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var bmp in _icones.Values) bmp?.Dispose();
        base.Dispose(disposing);
    }
}

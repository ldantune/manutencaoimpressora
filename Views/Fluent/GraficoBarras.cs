using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Gráfico de barras empilhadas (download embaixo, upload em cima),
//  uma barra por dia, com eixo em GB/MB e destaque ao passar o mouse
// ---------------------------------------------------------------
public class GraficoBarras : Control
{
    List<UsoDia> _dias = new();
    int _hover = -1;

    public Color CorDownload { get; set; } = Tema.CorDownload;
    public Color CorUpload { get; set; } = Tema.CorUpload;

    // Dispara com o dia sob o mouse (ou null quando sai do gráfico)
    public event Action<UsoDia?>? DiaSobMouse;

    public GraficoBarras()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Card;
        Height = 190;
    }

    public void Definir(List<UsoDia> dias)
    {
        _dias = dias;
        if (_hover >= dias.Count) _hover = -1;
        Invalidate();
    }

    Rectangle AreaGrafico()
    {
        float s = Tema.Escala(this);
        int esquerda = (int)Math.Round(52 * s), topo = (int)Math.Round(8 * s), baixo = (int)Math.Round(22 * s);
        return new Rectangle(esquerda, topo, Width - esquerda - (int)Math.Round(4 * s), Height - topo - baixo);
    }

    static double Redondo(double x)
    {
        if (x <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(x)));
        double f = x / p;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p;
    }

    static (double unidade, string nome) Unidade(long maximo) =>
        maximo >= FormatoBytes.GB ? (FormatoBytes.GB, "GB")
        : maximo >= FormatoBytes.MB ? (FormatoBytes.MB, "MB")
        : (FormatoBytes.KB, "KB");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);
        var area = AreaGrafico();
        if (area.Width <= 0 || area.Height <= 0) return;

        long maximo = _dias.Count == 0 ? 0 : _dias.Max(d => d.Total);
        using var linhaBase = new Pen(Tema.BotaoBorda);

        if (maximo <= 0)
        {
            g.DrawLine(linhaBase, area.Left, area.Bottom, area.Right, area.Bottom);
            TextRenderer.DrawText(g, "Ainda não há uso registrado nestes dias.", Tema.Legenda, area,
                Tema.TextoSecundario, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        // Escala "redonda" (ex.: 0, 10, 20, 30 GB)
        var (unidade, nomeUnidade) = Unidade(maximo);
        double passo = Redondo(maximo / unidade / 3);
        double topo = Math.Ceiling(maximo / unidade / passo) * passo;
        double pxPorByte = area.Height / (topo * unidade);

        using var linhaGrade = new Pen(Color.FromArgb(239, 239, 239));
        for (double k = 0; k <= topo + passo / 1000; k += passo)
        {
            int y = area.Bottom - (int)Math.Round(k / topo * area.Height);
            g.DrawLine(k == 0 ? linhaBase : linhaGrade, area.Left, y, area.Right, y);
            string rotulo = k == 0 ? "0" : $"{k:0.##} {nomeUnidade}";
            TextRenderer.DrawText(g, rotulo, Tema.Legenda,
                new Rectangle(0, y - (int)(10 * s), area.Left - (int)(8 * s), (int)(20 * s)),
                Tema.TextoSecundario, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int n = _dias.Count;
        float faixa = area.Width / (float)n;
        float largura = Math.Max(2f, faixa * 0.66f);

        if (_hover >= 0)
        {
            using var destaque = new SolidBrush(Tema.ResumoFundo);
            g.FillRectangle(destaque, area.Left + _hover * faixa, area.Top, faixa, area.Height - 1);
        }

        for (int i = 0; i < n; i++)
        {
            var dia = _dias[i];
            int alfa = _hover < 0 || _hover == i ? 255 : 110;
            float x = area.Left + i * faixa + (faixa - largura) / 2;
            float hDown = (float)(dia.Recebidos * pxPorByte);
            float hUp = (float)(dia.Enviados * pxPorByte);

            if (hDown > 0)
            {
                using var b = new SolidBrush(Color.FromArgb(alfa, CorDownload));
                g.FillRectangle(b, x, area.Bottom - hDown, largura, hDown);
            }
            if (hUp > 0)
            {
                using var b = new SolidBrush(Color.FromArgb(alfa, CorUpload));
                g.FillRectangle(b, x, area.Bottom - hDown - hUp - (hDown > 0 ? 1 : 0), largura, hUp);
            }
        }

        // Datas embaixo: a cada 5 dias, sempre incluindo hoje (a última barra)
        int larguraRotulo = (int)Math.Round(48 * s);
        for (int i = 0; i < n; i++)
        {
            if ((n - 1 - i) % 5 != 0) continue;
            int centro = (int)(area.Left + i * faixa + faixa / 2);
            int xr = Math.Clamp(centro - larguraRotulo / 2, area.Left - larguraRotulo / 3, Width - larguraRotulo);
            TextRenderer.DrawText(g, _dias[i].Data.ToString("dd/MM"), Tema.Legenda,
                new Rectangle(xr, area.Bottom + (int)(5 * s), larguraRotulo, (int)(16 * s)),
                Tema.TextoSecundario, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var area = AreaGrafico();
        int indice = -1;
        if (_dias.Count > 0 && e.X >= area.Left && e.X <= area.Right && area.Width > 0)
            indice = Math.Clamp((int)((e.X - area.Left) / (area.Width / (float)_dias.Count)), 0, _dias.Count - 1);
        MudarHover(indice);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        MudarHover(-1);
    }

    void MudarHover(int indice)
    {
        if (indice == _hover) return;
        _hover = indice;
        Invalidate();
        DiaSobMouse?.Invoke(indice >= 0 ? _dias[indice] : null);
    }
}

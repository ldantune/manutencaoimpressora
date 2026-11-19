using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Cores, fontes e utilitários de desenho no estilo Windows 11
// ---------------------------------------------------------------
static class Tema
{
    public static readonly Color Fundo = Color.FromArgb(243, 243, 243);
    public static readonly Color Card = Color.White;
    public static readonly Color BordaCard = Color.FromArgb(229, 229, 229);
    public static readonly Color Texto = Color.FromArgb(26, 26, 26);
    public static readonly Color TextoSecundario = Color.FromArgb(92, 92, 92);
    public static readonly Color TextoDesabilitado = Color.FromArgb(160, 160, 160);

    public static readonly Color Destaque = Color.FromArgb(0, 95, 184);
    public static readonly Color DestaqueHover = Color.FromArgb(25, 110, 191);
    public static readonly Color DestaquePressionado = Color.FromArgb(49, 125, 198);

    public static readonly Color BotaoFundo = Color.FromArgb(251, 251, 251);
    public static readonly Color BotaoHover = Color.FromArgb(245, 245, 245);
    public static readonly Color BotaoPressionado = Color.FromArgb(237, 237, 237);
    public static readonly Color BotaoBorda = Color.FromArgb(209, 209, 209);
    public static readonly Color BordaControle = Color.FromArgb(138, 138, 138);

    public static readonly Color ResumoFundo = Color.FromArgb(243, 246, 250);

    public static readonly Font Corpo = new("Segoe UI", 9.75f);
    public static readonly Font CorpoForte = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Legenda = new("Segoe UI", 9f);
    public static readonly Font Subtitulo = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Titulo = new("Segoe UI Semibold", 17f);

    public static readonly string FonteIcones = DetectarFonteIcones();
    public static readonly Font Icone = new(FonteIcones, 10.5f);
    public static readonly Font IconeGrande = new(FonteIcones, 12f);

    static string DetectarFonteIcones()
    {
        using var fontes = new InstalledFontCollection();
        foreach (var nome in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            if (fontes.Families.Any(f => f.Name == nome)) return nome;
        return "Segoe UI Symbol";
    }

    public static float Escala(Control c) => c.DeviceDpi / 96f;

    public static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var p = new GraphicsPath();
        float d = raio * 2;
        if (d <= 0 || r.Width < d || r.Height < d)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Desenha um retângulo arredondado preenchido (e com borda, se informada)
    public static void Caixa(Graphics g, Rectangle area, float raio, Color fundo, Color? borda = null, float espessura = 1f)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(area.X + 0.5f, area.Y + 0.5f, area.Width - 1f, area.Height - 1f);
        using var path = Arredondado(r, raio);
        using (var b = new SolidBrush(fundo)) g.FillPath(b, path);
        if (borda is Color cor)
        {
            using var pen = new Pen(cor, espessura);
            g.DrawPath(pen, path);
        }
    }

    // Anel de foco (navegação por teclado)
    public static void AnelFoco(Graphics g, Control c, float raio)
    {
        float s = Escala(c);
        var r = new RectangleF(1f * s, 1f * s, c.Width - 2f * s - 1, c.Height - 2f * s - 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Arredondado(r, raio);
        using var pen = new Pen(Texto, 2f * s);
        g.DrawPath(pen, path);
    }

    // Deixa a barra de título da mesma cor do fundo (Windows 11; ignorado no Windows 10)
    public static void AplicarBarraTitulo(Form form)
    {
        try
        {
            const int DWMWA_CAPTION_COLOR = 35;
            int cor = Fundo.R | (Fundo.G << 8) | (Fundo.B << 16);
            DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref cor, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

// Glifos da fonte Segoe Fluent Icons / Segoe MDL2 Assets
static class Icones
{
    public const string Imprimir = "";
    public const string Historico = "";
    public const string Fechar = "";
    public const string Calendario = "";
    public const string Sucesso = "";
    public const string Aviso = "";
    public const string Info = "";
    public const string Pasta = "";
}

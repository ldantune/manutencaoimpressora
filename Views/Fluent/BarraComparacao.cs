using System.Drawing.Drawing2D;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Barra de progresso com um marcador (ex.: uso do mês atual com a
//  marca de quanto foi usado no mês passado)
// ---------------------------------------------------------------
public class BarraComparacao : Control
{
    float _valor;
    float? _marcador;

    public BarraComparacao()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 18;
        TabStop = false;
    }

    // valor e marcador entre 0 e 1
    public void Definir(float valor, float? marcador)
    {
        _valor = Math.Clamp(valor, 0, 1);
        _marcador = marcador is float m ? Math.Clamp(m, 0, 1) : null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Card);
        float s = Tema.Escala(this);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float altura = 8 * s;
        float margem = 2 * s;   // espaço para o marcador não ser cortado nas pontas
        var trilho = new RectangleF(margem, (Height - altura) / 2, Width - margem * 2, altura);
        using (var caminho = Tema.Arredondado(trilho, altura / 2))
        {
            using (var b = new SolidBrush(Color.FromArgb(237, 237, 237))) g.FillPath(b, caminho);
            if (_valor > 0)
            {
                var clipAnterior = g.Clip;
                g.SetClip(caminho, CombineMode.Intersect);
                using (var b = new SolidBrush(Tema.CorDownload)) g.FillRectangle(b, trilho.X, trilho.Y, trilho.Width * _valor, altura);
                g.Clip = clipAnterior;
            }
        }

        if (_marcador is float m)
        {
            float x = trilho.X + trilho.Width * m;
            using var b = new SolidBrush(Tema.Texto);
            g.FillRectangle(b, x - s, 0, 2 * s, Height);
        }
    }
}

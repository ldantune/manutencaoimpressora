using System.Drawing.Drawing2D;

namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Gráfico de linha simples (estilo Gerenciador de Tarefas) com
//  um buffer rolante de amostras, auto-escalado pelo valor máximo
// ---------------------------------------------------------------
public class GraficoLinha : Control
{
    readonly Queue<double> _amostras = new();

    public int MaximoAmostras { get; set; } = 60;
    public Color CorLinha { get; set; } = Tema.Destaque;

    public GraficoLinha()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Card;
    }

    public void AdicionarAmostra(double valor)
    {
        _amostras.Enqueue(valor);
        while (_amostras.Count > MaximoAmostras) _amostras.Dequeue();
        Invalidate();
    }

    public void DefinirAmostras(IEnumerable<double> valores)
    {
        _amostras.Clear();
        foreach (var v in valores) _amostras.Enqueue(v);
        while (_amostras.Count > MaximoAmostras) _amostras.Dequeue();
        Invalidate();
    }

    public void Limpar()
    {
        _amostras.Clear();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* tudo é pintado no OnPaint, evita piscar */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float escala = Tema.Escala(this);
        Tema.Caixa(g, ClientRectangle, 6 * escala, Tema.Card, Tema.BordaCard);

        if (_amostras.Count < 2) return;
        var valores = _amostras.ToArray();
        double maximo = Math.Max(valores.Max(), 1);

        float margem = 8 * escala;
        var area = new RectangleF(margem, margem, Width - margem * 2, Height - margem * 2);
        if (area.Width <= 0 || area.Height <= 0) return;

        var pontos = new PointF[valores.Length];
        for (int i = 0; i < valores.Length; i++)
        {
            float x = area.X + (valores.Length == 1 ? 0 : i * area.Width / (valores.Length - 1));
            float y = area.Bottom - (float)(valores[i] / maximo) * area.Height;
            pontos[i] = new PointF(x, y);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var caminho = new GraphicsPath();
        caminho.AddLines(pontos);
        caminho.AddLine(pontos[^1].X, area.Bottom, pontos[0].X, area.Bottom);
        caminho.CloseFigure();
        using var preenchimento = new SolidBrush(Color.FromArgb(40, CorLinha));
        g.FillPath(preenchimento, caminho);

        using var lapis = new Pen(CorLinha, 2f * escala);
        g.DrawLines(lapis, pontos);
    }
}

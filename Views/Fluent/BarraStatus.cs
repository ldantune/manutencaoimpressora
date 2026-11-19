namespace ManutencaoImpressora.Views.Fluent;

public enum Severidade { Neutro, Sucesso, Aviso }

// ---------------------------------------------------------------
//  Faixa de status (InfoBar do Windows 11): ícone, título e mensagem
// ---------------------------------------------------------------
public class BarraStatus : Control
{
    Severidade _severidade;
    string _titulo = "";
    string _mensagem = "";

    public BarraStatus()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0, 0, 0, 12);
        Height = 60;
        AccessibleRole = AccessibleRole.StatusBar;
    }

    public void Definir(Severidade severidade, string titulo, string mensagem)
    {
        _severidade = severidade;
        _titulo = titulo;
        _mensagem = mensagem;
        AccessibleName = $"{titulo}. {mensagem}";
        AjustarAltura();
        Invalidate();
    }

    (Color fundo, Color borda, Color icone, string glifo) Cores() => _severidade switch
    {
        Severidade.Sucesso => (Color.FromArgb(223, 246, 221), Color.FromArgb(198, 232, 195), Color.FromArgb(15, 123, 15), Icones.Sucesso),
        Severidade.Aviso => (Color.FromArgb(255, 244, 206), Color.FromArgb(244, 223, 160), Color.FromArgb(157, 93, 0), Icones.Aviso),
        _ => (Color.White, Tema.BordaCard, Tema.TextoSecundario, Icones.Info)
    };

    Rectangle AreaTexto()
    {
        float s = Tema.Escala(this);
        int esquerda = (int)Math.Round(46 * s);
        return new Rectangle(esquerda, (int)Math.Round(11 * s), Math.Max(1, Width - esquerda - (int)Math.Round(14 * s)), 0);
    }

    void AjustarAltura()
    {
        if (!IsHandleCreated) return;
        float s = Tema.Escala(this);
        var area = AreaTexto();
        int hTitulo = TextRenderer.MeasureText(_titulo, Tema.CorpoForte, new Size(area.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        int hMensagem = TextRenderer.MeasureText(_mensagem, Tema.Corpo, new Size(area.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        int nova = area.Y * 2 + hTitulo + (int)Math.Round(2 * s) + hMensagem;
        if (Height != nova) Height = nova;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AjustarAltura(); }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        AjustarAltura();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        float s = Tema.Escala(this);
        var (fundo, borda, corIcone, glifo) = Cores();

        Tema.Caixa(g, ClientRectangle, 6 * s, fundo, borda);

        var area = AreaTexto();
        TextRenderer.DrawText(g, glifo, Tema.IconeGrande,
            new Rectangle((int)Math.Round(14 * s), area.Y, (int)Math.Round(22 * s), (int)Math.Round(20 * s)),
            corIcone, TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left;
        var tamTitulo = TextRenderer.MeasureText(_titulo, Tema.CorpoForte, new Size(area.Width, int.MaxValue), flags);
        TextRenderer.DrawText(g, _titulo, Tema.CorpoForte,
            new Rectangle(area.X, area.Y, area.Width, tamTitulo.Height), Tema.Texto, flags);

        int y = area.Y + tamTitulo.Height + (int)Math.Round(2 * s);
        TextRenderer.DrawText(g, _mensagem, Tema.Corpo,
            new Rectangle(area.X, y, area.Width, Height - y), Tema.Texto, flags);
    }
}

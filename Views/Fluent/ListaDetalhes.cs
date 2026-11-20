namespace ManutencaoImpressora.Views.Fluent;

// Uma linha da lista: ícone (glifo), título, selo opcional ("Novo"), detalhe e texto à direita
public record ItemDetalhe(string Glifo, string Titulo, string Detalhe, string Direita,
    string? Selo = null, Color? CorSelo = null, Color? CorIcone = null, Color? FundoIcone = null,
    bool Apagado = false, string? Chave = null);

// ---------------------------------------------------------------
//  Lista genérica de linhas com ícone redondo (quedas, testes de
//  velocidade, aparelhos da rede). Pode ter um botão de editar por linha.
// ---------------------------------------------------------------
public class ListaDetalhes : Control
{
    const int AlturaLinha = 56;

    List<ItemDetalhe> _itens = new();
    int _hoverEditar = -1;

    public string TextoVazio { get; set; } = "Nada por aqui ainda.";
    public bool MostrarEditar { get; set; }
    public string GlifoAcao { get; set; } = "\uE70F";   // lápis (editar) por padrão
    public event Action<ItemDetalhe>? EditarClicado;

    public ListaDetalhes()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Card;
        Height = 56;
        AccessibleRole = AccessibleRole.List;
    }

    public void Definir(List<ItemDetalhe> itens)
    {
        _itens = itens;
        float s = Tema.Escala(this);
        Height = (int)Math.Round(Math.Max(1, itens.Count) * AlturaLinha * s);
        AccessibleDescription = string.Join("; ", itens.Select(i => $"{i.Titulo}, {i.Detalhe}, {i.Direita}"));
        Invalidate();
    }

    int LarguraEditar => MostrarEditar ? (int)Math.Round(40 * Tema.Escala(this)) : 0;

    Rectangle AreaEditar(int linha)
    {
        float s = Tema.Escala(this);
        int h = (int)Math.Round(AlturaLinha * s), tam = (int)Math.Round(32 * s);
        return new Rectangle(Width - tam, linha * h + (h - tam) / 2, tam, tam);
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

        int hLinha = (int)Math.Round(AlturaLinha * s);
        int tamIcone = (int)Math.Round(36 * s);
        int xTexto = tamIcone + (int)Math.Round(12 * s);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        using var separador = new Pen(Color.FromArgb(240, 240, 240));

        for (int i = 0; i < _itens.Count; i++)
        {
            var item = _itens[i];
            int y = i * hLinha;
            if (y > e.ClipRectangle.Bottom || y + hLinha < e.ClipRectangle.Top) continue;
            Color corTexto = item.Apagado ? Tema.TextoSecundario : Tema.Texto;

            // Ícone redondo
            var circulo = new Rectangle(0, y + (hLinha - tamIcone) / 2, tamIcone, tamIcone);
            Tema.Caixa(g, circulo, tamIcone / 2f, item.FundoIcone ?? Color.FromArgb(243, 243, 243));
            TextRenderer.DrawText(g, item.Glifo, Tema.Icone, circulo, item.Apagado ? Tema.TextoSecundario : item.CorIcone ?? Tema.Texto,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Texto da direita
            int larguraDireita = Math.Min(TextRenderer.MeasureText(item.Direita, Tema.Corpo, Size.Empty, TextFormatFlags.NoPadding).Width,
                                          (int)Math.Round(220 * s));
            int xDireita = Width - LarguraEditar - larguraDireita - (MostrarEditar ? (int)Math.Round(8 * s) : 0);
            TextRenderer.DrawText(g, item.Direita, Tema.Corpo, new Rectangle(xDireita, y, larguraDireita + 2, hLinha), Tema.TextoSecundario,
                flags | TextFormatFlags.Right);

            // Título (+ selo) e detalhe
            int larguraTexto = Math.Max(10, xDireita - xTexto - (int)Math.Round(12 * s));
            int hTitulo = (int)Math.Round(20 * s);
            int yTitulo = y + (int)Math.Round(9 * s);
            int larguraTitulo = Math.Min(larguraTexto, TextRenderer.MeasureText(item.Titulo, Tema.CorpoForte, Size.Empty, TextFormatFlags.NoPadding).Width + 2);
            TextRenderer.DrawText(g, item.Titulo, Tema.CorpoForte, new Rectangle(xTexto, yTitulo, larguraTitulo, hTitulo), corTexto, flags | TextFormatFlags.Left);

            if (item.Selo is string selo)
            {
                var tamSelo = TextRenderer.MeasureText(selo, Tema.Legenda, Size.Empty, TextFormatFlags.NoPadding);
                var areaSelo = new Rectangle(xTexto + larguraTitulo + (int)Math.Round(8 * s), yTitulo + (hTitulo - tamSelo.Height - (int)(4 * s)) / 2,
                    tamSelo.Width + (int)Math.Round(16 * s), tamSelo.Height + (int)Math.Round(4 * s));
                if (areaSelo.Right < xDireita)
                {
                    Tema.Caixa(g, areaSelo, areaSelo.Height / 2f, item.CorSelo ?? Tema.Destaque);
                    TextRenderer.DrawText(g, selo, Tema.Legenda, areaSelo, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            TextRenderer.DrawText(g, item.Detalhe, Tema.Legenda,
                new Rectangle(xTexto, y + (int)Math.Round(31 * s), larguraTexto, (int)Math.Round(16 * s)), Tema.TextoSecundario, flags | TextFormatFlags.Left);

            if (MostrarEditar && item.Chave != null)
            {
                var area = AreaEditar(i);
                if (_hoverEditar == i) Tema.Caixa(g, area, 4 * s, Tema.BotaoHover);
                TextRenderer.DrawText(g, GlifoAcao, Tema.Icone, area, Tema.TextoSecundario,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (i < _itens.Count - 1) g.DrawLine(separador, xTexto, y + hLinha - 1, Width, y + hLinha - 1);
        }
    }

    int LinhaEditarEm(Point p)
    {
        if (!MostrarEditar) return -1;
        for (int i = 0; i < _itens.Count; i++)
            if (_itens[i].Chave != null && AreaEditar(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int linha = LinhaEditarEm(e.Location);
        Cursor = linha >= 0 ? Cursors.Hand : Cursors.Default;
        if (linha != _hoverEditar) { _hoverEditar = linha; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverEditar != -1) { _hoverEditar = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int linha = LinhaEditarEm(e.Location);
        if (linha >= 0) EditarClicado?.Invoke(_itens[linha]);
    }
}

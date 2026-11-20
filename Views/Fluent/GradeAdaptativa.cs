namespace ManutencaoImpressora.Views.Fluent;

// ---------------------------------------------------------------
//  Grade que muda o número de colunas conforme a largura disponível
//  (layout adaptativo): ex.: 2 cartões lado a lado na tela grande e
//  um embaixo do outro na janela estreita
// ---------------------------------------------------------------
public class GradeAdaptativa : TableLayoutPanel
{
    readonly List<Control> _itens = new();
    int _colunasAtuais = -1;

    // Largura mínima de cada coluna (px a 100% de escala) e limite de colunas
    public int LarguraMinimaColuna { get; set; } = 360;
    public int MaximoColunas { get; set; } = 2;
    public int Espaco { get; set; } = 12;

    public GradeAdaptativa()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);
        ColumnCount = 1;
    }

    public void Adicionar(Control c)
    {
        c.Dock = DockStyle.Fill;
        _itens.Add(c);
        Controls.Add(c);
        _colunasAtuais = -1;
        Organizar();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Organizar();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _colunasAtuais = -1;   // refaz as margens já com a escala (DPI) correta
        Organizar();
    }

    void Organizar()
    {
        if (_itens.Count == 0) return;
        float s = Tema.Escala(this);
        int colunas = Math.Clamp(Width / Math.Max(1, (int)(LarguraMinimaColuna * s)), 1, Math.Max(1, MaximoColunas));
        colunas = Math.Min(colunas, _itens.Count);
        if (colunas == _colunasAtuais) return;
        _colunasAtuais = colunas;

        // Antes do handle existir, as margens ficam em px lógicos (o AutoScale do form ajusta)
        float sMargem = IsHandleCreated ? s : 1f;
        int meio = (int)Math.Round(Espaco * sMargem / 2f);
        int vertical = (int)Math.Round(Espaco * sMargem);
        int linhas = (_itens.Count + colunas - 1) / colunas;

        SuspendLayout();
        ColumnStyles.Clear();
        RowStyles.Clear();
        ColumnCount = colunas;
        RowCount = linhas;
        for (int c = 0; c < colunas; c++) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / colunas));
        for (int r = 0; r < linhas; r++) RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (int i = 0; i < _itens.Count; i++)
        {
            int col = i % colunas, lin = i / colunas;
            SetCellPosition(_itens[i], new TableLayoutPanelCellPosition(col, lin));
            _itens[i].Margin = new Padding(col == 0 ? 0 : meio, 0, col == colunas - 1 ? 0 : meio, lin == linhas - 1 ? 0 : vertical);
        }
        ResumeLayout(true);
    }
}

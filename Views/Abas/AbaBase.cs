using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Base das telas do painel: título, subtítulo e o conteúdo
//  empilhado, com a altura avisada para a janela principal
// ---------------------------------------------------------------
public abstract class AbaBase : UserControl, IAbaUtilitario
{
    protected readonly TableLayoutPanel Conteudo = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Padding = new Padding(24, 12, 24, 8)
    };

    public int AlturaPreferida => Conteudo.Height;
    public event EventHandler? AlturaMudou;

    protected AbaBase(string titulo, string subtitulo)
    {
        Dock = DockStyle.Fill;
        BackColor = Tema.Fundo;
        Conteudo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Adicionar(new Label { Text = titulo, Font = Tema.Titulo, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        Adicionar(new Label
        {
            Text = subtitulo, ForeColor = Tema.TextoSecundario, AutoSize = true,
            MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 12)
        });
        Controls.Add(Conteudo);
        Conteudo.SizeChanged += (s, e) => AlturaMudou?.Invoke(this, EventArgs.Empty);
    }

    protected void Adicionar(Control c) => AdicionarEm(Conteudo, c);

    public static void AdicionarEm(TableLayoutPanel tabela, Control c)
    {
        if (c is CardPanel) c.Dock = DockStyle.Fill;
        tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabela.RowCount = tabela.RowStyles.Count;
        tabela.Controls.Add(c, 0, tabela.RowCount - 1);
    }

    // Painel empilhado (usado nas visões de cada abinha)
    public static TableLayoutPanel NovoPainel() => new()
    {
        Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Margin = new Padding(0),
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) }
    };

    public static Label TituloCard(string texto) =>
        new() { Text = texto, Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left };
}

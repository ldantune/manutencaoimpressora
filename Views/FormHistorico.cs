using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views;

// ---------------------------------------------------------------
//  Tela de histórico (visual Windows 11), lê os registros do
//  banco SQLite através do HistoricoLog
// ---------------------------------------------------------------
public class FormHistorico : Form
{
    static readonly Color CorErro = Color.FromArgb(196, 43, 28);
    static readonly Color CorSucesso = Color.FromArgb(15, 123, 15);

    readonly HistoricoLog _log;
    readonly Label lblContagem = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Left };
    readonly Label lblVazio = new()
    {
        Text = "Nenhum registro ainda.\nAs impressões e alterações no agendamento aparecem aqui.",
        ForeColor = Tema.TextoSecundario, TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill, Visible = false
    };

    readonly DataGridView grade = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Tema.Card,
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        GridColor = Tema.BordaCard,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        EnableHeadersVisualStyles = false,
        ShowCellToolTips = true,
        StandardTab = true,
    };

    public FormHistorico(HistoricoLog log)
    {
        _log = log;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Histórico";
        Font = Tema.Corpo;
        ForeColor = Tema.Texto;
        BackColor = Tema.Fundo;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 520);

        ConfigurarGrade();

        // Conteúdo: título, subtítulo e cartão com a lista
        var conteudo = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 12, 24, 16)
        };
        conteudo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        conteudo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conteudo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conteudo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        conteudo.Controls.Add(new Label
        {
            Text = "Histórico", Font = Tema.Titulo, AutoSize = true, Margin = new Padding(0, 0, 0, 2)
        }, 0, 0);
        conteudo.Controls.Add(new Label
        {
            Text = "Impressões e alterações no agendamento, das mais recentes para as mais antigas.",
            ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);

        var card = new CardPanel { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(6), Margin = new Padding(0) };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowCount = 1;
        var area = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Tema.Card };
        area.Controls.Add(grade);
        area.Controls.Add(lblVazio);
        card.Controls.Add(area, 0, 0);
        conteudo.Controls.Add(card, 0, 2);

        Controls.Add(conteudo);
        Controls.Add(MontarRodape());

        CarregarRegistros();
        ResumeLayout(false);
        PerformLayout();

        // Sem linha pré-selecionada ao abrir
        Shown += (s, e) => { grade.ClearSelection(); grade.CurrentCell = null; };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarBarraTitulo(this);
    }

    void ConfigurarGrade()
    {
        grade.ColumnHeadersHeight = 36;
        grade.RowTemplate.Height = 38;

        var cab = grade.ColumnHeadersDefaultCellStyle;
        cab.BackColor = Tema.Card;
        cab.ForeColor = Tema.TextoSecundario;
        cab.SelectionBackColor = Tema.Card;
        cab.SelectionForeColor = Tema.TextoSecundario;
        cab.Font = Tema.Legenda;
        cab.Padding = new Padding(8, 0, 8, 0);
        cab.Alignment = DataGridViewContentAlignment.MiddleLeft;

        var cel = grade.DefaultCellStyle;
        cel.BackColor = Tema.Card;
        cel.ForeColor = Tema.Texto;
        cel.SelectionBackColor = Tema.ResumoFundo;
        cel.SelectionForeColor = Tema.Texto;
        cel.Padding = new Padding(8, 0, 8, 0);
        cel.WrapMode = DataGridViewTriState.False;

        var colIcone = new DataGridViewTextBoxColumn
        {
            Name = "Icone", HeaderText = "", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colIcone.DefaultCellStyle.Font = Tema.Icone;
        colIcone.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        colIcone.DefaultCellStyle.Padding = new Padding(4, 0, 0, 0);

        var colQuando = new DataGridViewTextBoxColumn
        {
            Name = "Quando", HeaderText = "Quando", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colQuando.DefaultCellStyle.ForeColor = Tema.TextoSecundario;
        colQuando.DefaultCellStyle.SelectionForeColor = Tema.TextoSecundario;

        var colMensagem = new DataGridViewTextBoxColumn
        {
            Name = "Mensagem", HeaderText = "O que aconteceu", SortMode = DataGridViewColumnSortMode.NotSortable
        };

        grade.Columns.AddRange(colIcone, colQuando, colMensagem);
    }

    Panel MontarRodape()
    {
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };
        rodape.Paint += (s, e) =>
        {
            using var pen = new Pen(Tema.BordaCard);
            e.Graphics.DrawLine(pen, 0, 0, rodape.Width, 0);
        };

        var btnAtualizar = new BotaoFluent { Text = "Atualizar", Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 8, 0) };
        var btnFechar = new BotaoFluent { Text = "Fechar", Estilo = EstiloBotao.Primario, Anchor = AnchorStyles.None };
        btnAtualizar.Click += (s, e) => { CarregarRegistros(); grade.ClearSelection(); };
        btnFechar.Click += (s, e) => Close();
        AcceptButton = btnFechar;
        CancelButton = btnFechar;

        var barra = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(24, 0, 24, 0) };
        barra.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barra.Controls.Add(lblContagem, 0, 0);
        barra.Controls.Add(btnAtualizar, 1, 0);
        barra.Controls.Add(btnFechar, 2, 0);
        rodape.Controls.Add(barra);
        return rodape;
    }

    void CarregarRegistros()
    {
        var registros = _log.Listar();
        grade.Rows.Clear();

        foreach (var r in registros)
        {
            var (glifo, cor, erro) = Classificar(r);
            int i = grade.Rows.Add(glifo, QuandoAmigavel(r.DataHora), r.Mensagem);
            var linha = grade.Rows[i];

            linha.Cells["Icone"].Style.ForeColor = cor;
            linha.Cells["Icone"].Style.SelectionForeColor = cor;
            linha.Cells["Quando"].ToolTipText = r.DataHora.ToString("dddd, dd/MM/yyyy 'às' HH:mm:ss");
            linha.Cells["Mensagem"].ToolTipText = r.Mensagem;
            if (erro)
            {
                linha.Cells["Mensagem"].Style.ForeColor = CorErro;
                linha.Cells["Mensagem"].Style.SelectionForeColor = CorErro;
            }
        }

        bool vazio = registros.Count == 0;
        grade.Visible = !vazio;
        lblVazio.Visible = vazio;
        lblContagem.Text = registros.Count switch
        {
            0 => "",
            1 => "1 registro",
            _ => $"{registros.Count} registros"
        };
    }

    // Ícone e cor de acordo com o tipo de evento
    static (string glifo, Color cor, bool erro) Classificar(RegistroHistorico r)
    {
        string m = r.Mensagem;
        if (m.StartsWith("ERRO", StringComparison.OrdinalIgnoreCase))
            return (Icones.Aviso, CorErro, true);
        if (m.Contains("Impressão", StringComparison.OrdinalIgnoreCase))
            return (Icones.Imprimir, Tema.Destaque, false);
        if (m.Contains("ativado", StringComparison.OrdinalIgnoreCase) && !m.Contains("desativado", StringComparison.OrdinalIgnoreCase))
            return (Icones.Sucesso, CorSucesso, false);
        return (Icones.Info, Tema.TextoSecundario, false);
    }

    static string QuandoAmigavel(DateTime d)
    {
        if (d.Date == DateTime.Today) return $"Hoje, {d:HH:mm}";
        if (d.Date == DateTime.Today.AddDays(-1)) return $"Ontem, {d:HH:mm}";
        return d.ToString("dd/MM/yyyy HH:mm");
    }
}

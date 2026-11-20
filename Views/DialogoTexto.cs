using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views;

// ---------------------------------------------------------------
//  Caixinha para digitar um texto (ex.: renomear um aparelho)
// ---------------------------------------------------------------
public class DialogoTexto : Form
{
    readonly TextBox txt = new() { Dock = DockStyle.Fill };

    public string Texto => txt.Text.Trim();

    public DialogoTexto(string titulo, string pergunta, string valorAtual, string? dica = null)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = titulo;
        Font = Tema.Corpo;
        BackColor = Tema.Fundo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 170);
        Icon = Tema.IconePrograma ?? Icon;

        var tabela = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), ColumnCount = 1, RowCount = 4 };
        tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabela.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabela.Controls.Add(new Label { Text = pergunta, AutoSize = true, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
        txt.Text = valorAtual;
        txt.Margin = new Padding(0);
        tabela.Controls.Add(txt, 0, 1);
        if (dica != null)
            tabela.Controls.Add(new Label { Text = dica, AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Margin = new Padding(0, 6, 0, 0) }, 0, 2);

        var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        var ok = new BotaoFluent { Text = "Salvar", Estilo = EstiloBotao.Primario, Margin = new Padding(8, 0, 0, 0), DialogResult = DialogResult.OK };
        var cancelar = new BotaoFluent { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        botoes.Controls.Add(ok);
        botoes.Controls.Add(cancelar);
        tabela.Controls.Add(botoes, 0, 3);
        Controls.Add(tabela);
        AcceptButton = ok;
        CancelButton = cancelar;
        Shown += (s, e) => { txt.Focus(); txt.SelectAll(); };
    }
}

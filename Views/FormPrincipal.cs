using System.Diagnostics;
using System.Drawing.Printing;
using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;

namespace ManutencaoImpressora.Views;

// ---------------------------------------------------------------
//  Tela de configuração
// ---------------------------------------------------------------
public class FormPrincipal : Form
{
    static readonly string[] NomesDias = { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" };

    readonly string _arquivoConfig;
    readonly HistoricoLog _log;
    readonly AgendadorTarefa _agendador;

    readonly ComboBox cbImpressora = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly TextBox txtDocumento = new() { ReadOnly = true, Width = 270 };
    readonly FlowLayoutPanel pnlDiasLinha1 = new()
        { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
    readonly FlowLayoutPanel pnlDiasLinha2 = new()
        { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
    readonly CheckBox[] chkDias = new CheckBox[7];
    readonly NumericUpDown numIntervalo = new() { Minimum = 1, Maximum = 52, Width = 55 };
    readonly DateTimePicker dtHora = new()
        { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    readonly DateTimePicker dtInicio = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    readonly CheckBox chkFim = new() { Text = "Terminar em:", AutoSize = true };
    readonly DateTimePicker dtFim = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    readonly Label lblStatus = new() { AutoSize = false, Size = new Size(450, 48) };

    public FormPrincipal(string arquivoConfig, HistoricoLog log, AgendadorTarefa agendador)
    {
        _arquivoConfig = arquivoConfig;
        _log = log;
        _agendador = agendador;

        Text = "Manutenção da Impressora";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 496);

        // Impressora
        Add(new Label { Text = "Impressora:", AutoSize = true }, 15, 15);
        Add(cbImpressora, 15, 38);
        var btnTeste = new Button { Text = "Imprimir teste", Size = new Size(105, 30) };
        btnTeste.Click += (s, e) => ImprimirTeste();
        Add(btnTeste, 360, 36);

        // Documento padrão (opcional): se vazio, imprime a página colorida de sempre
        Add(new Label { Text = "Documento padrão (opcional):", AutoSize = true }, 15, 82);
        Add(txtDocumento, 15, 105);
        var btnEscolher = new Button { Text = "Escolher...", Size = new Size(90, 28) };
        var btnLimpar = new Button { Text = "Limpar", Size = new Size(70, 28) };
        btnEscolher.Click += (s, e) => EscolherDocumento();
        btnLimpar.Click += (s, e) => txtDocumento.Text = "";
        Add(btnEscolher, 292, 104);
        Add(btnLimpar, 388, 104);

        // Dias da semana: 4 na primeira linha, 3 na segunda, ambas centralizadas
        Add(new Label { Text = "Dias da semana:", AutoSize = true }, 15, 140);
        for (int i = 0; i < 7; i++)
        {
            chkDias[i] = new CheckBox { Text = NomesDias[i], AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
            (i < 4 ? pnlDiasLinha1 : pnlDiasLinha2).Controls.Add(chkDias[i]);
        }
        Add(pnlDiasLinha1, 0, 163);
        Add(pnlDiasLinha2, 0, 191);
        pnlDiasLinha1.PerformLayout();
        pnlDiasLinha2.PerformLayout();
        pnlDiasLinha1.Left = (ClientSize.Width - pnlDiasLinha1.Width) / 2;
        pnlDiasLinha2.Left = (ClientSize.Width - pnlDiasLinha2.Width) / 2;

        // Frequência, horário, início e fim
        Add(new Label { Text = "Repetir a cada", AutoSize = true }, 15, 236);
        Add(numIntervalo, 130, 233);
        Add(new Label { Text = "semana(s)", AutoSize = true }, 190, 236);

        Add(new Label { Text = "Horário:", AutoSize = true }, 15, 273);
        Add(dtHora, 130, 270);

        Add(new Label { Text = "Começar em:", AutoSize = true }, 15, 310);
        Add(dtInicio, 130, 307);

        chkFim.CheckedChanged += (s, e) => dtFim.Enabled = chkFim.Checked;
        Add(chkFim, 15, 346);
        Add(dtFim, 130, 344);

        // Status
        Add(lblStatus, 15, 386);

        // Botões
        var btnSalvar = new Button { Text = "Salvar e ativar", Size = new Size(140, 34) };
        var btnDesativar = new Button { Text = "Desativar", Size = new Size(120, 34) };
        var btnHistorico = new Button { Text = "Ver histórico", Size = new Size(130, 34) };
        btnSalvar.Click += (s, e) => SalvarEAtivar();
        btnDesativar.Click += (s, e) => Desativar();
        btnHistorico.Click += (s, e) => AbrirHistorico();
        Add(btnSalvar, 15, 446);
        Add(btnDesativar, 162, 446);
        Add(btnHistorico, 297, 446);
        AcceptButton = btnSalvar;

        CarregarTela();
    }

    void Add(Control c, int x, int y)
    {
        c.Location = new Point(x, y);
        Controls.Add(c);
    }

    void CarregarTela()
    {
        var cfg = Config.Carregar(_arquivoConfig);

        foreach (string nome in PrinterSettings.InstalledPrinters)
            cbImpressora.Items.Add(nome);

        string padrao = new PrinterSettings().PrinterName;
        string escolhida = string.IsNullOrWhiteSpace(cfg.Impressora) ? padrao : cfg.Impressora;
        if (cbImpressora.Items.Contains(escolhida)) cbImpressora.SelectedItem = escolhida;
        else if (cbImpressora.Items.Count > 0) cbImpressora.SelectedIndex = 0;

        txtDocumento.Text = cfg.CaminhoDocumento;

        for (int i = 0; i < 7; i++)
            chkDias[i].Checked = cfg.Dias.Contains((DayOfWeek)i);

        numIntervalo.Value = Math.Clamp(cfg.IntervaloSemanas, 1, 52);
        dtHora.Value = DateTime.Today + cfg.Hora;
        dtInicio.Value = cfg.Inicio;
        chkFim.Checked = cfg.TemFim;
        dtFim.Value = cfg.Fim;
        dtFim.Enabled = cfg.TemFim;

        AtualizarStatus(cfg);
    }

    Config LerTela() => new()
    {
        Impressora = cbImpressora.SelectedItem?.ToString() ?? "",
        CaminhoDocumento = txtDocumento.Text,
        Dias = Enumerable.Range(0, 7).Where(i => chkDias[i].Checked).Select(i => (DayOfWeek)i).ToList(),
        IntervaloSemanas = (int)numIntervalo.Value,
        Hora = new TimeSpan(dtHora.Value.Hour, dtHora.Value.Minute, 0),
        Inicio = dtInicio.Value.Date,
        TemFim = chkFim.Checked,
        Fim = dtFim.Value.Date
    };

    void AtualizarStatus(Config cfg)
    {
        if (!_agendador.Existe())
        {
            lblStatus.ForeColor = Color.DimGray;
            lblStatus.Text = "Impressão automática desativada.";
            return;
        }

        var proxima = cfg.ProximaExecucao();
        if (proxima is null)
        {
            lblStatus.ForeColor = Color.DarkOrange;
            lblStatus.Text = "Agendamento ativo, mas a data de término já passou. Altere as datas e salve de novo.";
        }
        else
        {
            lblStatus.ForeColor = Color.DarkGreen;
            lblStatus.Text = $"Ativo. Próxima impressão: {proxima:dddd, dd/MM/yyyy} às {proxima:HH:mm}.";
        }
    }

    void SalvarEAtivar()
    {
        var cfg = LerTela();

        if (cfg.Dias.Count == 0)
        {
            MessageBox.Show("Marque pelo menos um dia da semana.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (cfg.TemFim && cfg.Fim < cfg.Inicio)
        {
            MessageBox.Show("A data de término precisa ser depois da data de início.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            cfg.Salvar(_arquivoConfig);
            _agendador.Criar(cfg);
            _log.Registrar("Agendamento salvo e ativado.");
            AtualizarStatus(cfg);
            MessageBox.Show("Pronto! A impressão automática está ativada.\n\n" +
                            "Você pode fechar este programa; o Windows cuida do resto.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void Desativar()
    {
        _agendador.Remover();
        _log.Registrar("Agendamento desativado.");
        AtualizarStatus(LerTela());
    }

    void EscolherDocumento()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Escolher documento padrão para imprimir",
            Filter = "Documentos e imagens|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.jpg;*.jpeg;*.png;*.bmp;*.txt|Todos os arquivos|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            txtDocumento.Text = dlg.FileName;
    }

    void ImprimirTeste()
    {
        try
        {
            new Impressora(cbImpressora.SelectedItem?.ToString() ?? "", txtDocumento.Text).Imprimir();
            _log.Registrar("Impressão de teste enviada.");
            MessageBox.Show("Página de teste enviada para a impressora.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Registrar("ERRO na impressão de teste: " + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void AbrirHistorico()
    {
        if (!File.Exists(_log.Arquivo))
        {
            MessageBox.Show("Ainda não há nada no histórico.", Text);
            return;
        }
        Process.Start(new ProcessStartInfo(_log.Arquivo) { UseShellExecute = true });
    }
}

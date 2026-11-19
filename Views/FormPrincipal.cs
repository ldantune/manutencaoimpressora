using System.Diagnostics;
using System.Drawing.Printing;
using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views;

// ---------------------------------------------------------------
//  Tela de configuração (visual Windows 11)
// ---------------------------------------------------------------
public class FormPrincipal : Form
{
    static readonly string[] NomesDias = { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" };
    static readonly string[] NomesDiasCompletos =
        { "Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado" };
    static readonly string[] NomesDiasPlural =
        { "domingos", "segundas", "terças", "quartas", "quintas", "sextas", "sábados" };

    readonly string _arquivoConfig;
    readonly HistoricoLog _log;
    readonly AgendadorTarefa _agendador;
    readonly ToolTip _dicas = new();

    readonly TableLayoutPanel conteudo = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Padding = new Padding(24, 12, 24, 8)
    };
    readonly Panel rodape = new() { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };

    readonly BarraStatus barraStatus = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    readonly ComboBox cbImpressora = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly TextBox txtDocumento = new()
    {
        ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right,
        PlaceholderText = "Página colorida padrão", BackColor = Color.FromArgb(250, 250, 250)
    };
    readonly BotaoDia[] chkDias = new BotaoDia[7];
    readonly NumericUpDown numIntervalo = new() { Minimum = 1, Maximum = 52, Width = 64, TextAlign = HorizontalAlignment.Center };
    readonly Label lblSemanas = new() { Text = "semana", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
    readonly DateTimePicker dtHora = new()
        { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Dock = DockStyle.Fill };
    readonly DateTimePicker dtInicio = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill };
    readonly Interruptor chkFim = new() { AccessibleName = "Definir data de término", Anchor = AnchorStyles.Right };
    readonly DateTimePicker dtFim = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, AccessibleName = "Data de término" };
    readonly CaixaResumo resumo = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    readonly NotifyIcon trayIcon = new() { Text = "Manutenção da Impressora" };
    bool _saindoDeVerdade;

    public FormPrincipal(string arquivoConfig, HistoricoLog log, AgendadorTarefa agendador)
    {
        _arquivoConfig = arquivoConfig;
        _log = log;
        _agendador = agendador;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"Manutenção da Impressora - v{versao?.Major}.{versao?.Minor}.{versao?.Build}";
        Font = Tema.Corpo;
        ForeColor = Tema.Texto;
        BackColor = Tema.Fundo;
        Icon = Tema.IconePrograma ?? Icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 760);

        conteudo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AdicionarConteudo(new Label
        {
            Text = "Manutenção da impressora", Font = Tema.Titulo, AutoSize = true, Margin = new Padding(0, 0, 0, 2)
        });
        AdicionarConteudo(new Label
        {
            Text = "Imprime uma página colorida de tempos em tempos para a tinta não secar nos cabeçotes.",
            ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 0, 0, 14)
        });
        AdicionarConteudo(barraStatus);
        AdicionarConteudo(MontarCardImpressora());
        AdicionarConteudo(MontarCardAgendamento());

        MontarRodape();

        Controls.Add(conteudo);
        Controls.Add(rodape);

        ConfigurarBandeja();
        cbImpressora.SelectedIndexChanged += (s, e) =>
        {
            if (!chkDias.Any(c => c is null)) AtualizarStatus(LerTela());
        };

        CarregarTela();
        ResumeLayout(false);
        PerformLayout();

        // A altura da janela acompanha o conteúdo
        conteudo.SizeChanged += (s, e) => AjustarAltura();
        Load += (s, e) => AjustarAltura();
        Load += async (s, e) => await VerificarAtualizacaoAsync();
        Load += (s, e) => VerificarFalhasRecentes();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarBarraTitulo(this);
    }

    void AjustarAltura() => ClientSize = new Size(ClientSize.Width, conteudo.Height + rodape.Height);

    // ---------------- Bandeja do sistema ----------------

    void ConfigurarBandeja()
    {
        trayIcon.Icon = Tema.IconePrograma ?? Icon;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (s, e) => MostrarJanela());
        menu.Items.Add("Sair", null, (s, e) => { _saindoDeVerdade = true; Close(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (s, e) => MostrarJanela();
        trayIcon.Visible = true;
    }

    void MostrarJanela()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_saindoDeVerdade && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            trayIcon.ShowBalloonTip(2000, Text,
                "O programa continua rodando em segundo plano. Clique no ícone para abrir de novo.",
                ToolTipIcon.Info);
            return;
        }

        trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    // ---------------- Montagem da tela ----------------

    void AdicionarConteudo(Control c)
    {
        if (c is CardPanel) c.Dock = DockStyle.Fill;
        conteudo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conteudo.RowCount = conteudo.RowStyles.Count;
        conteudo.Controls.Add(c, 0, conteudo.RowCount - 1);
    }

    static Label Rotulo(string texto, int margemInferior = 6) =>
        new() { Text = texto, AutoSize = true, Margin = new Padding(0, 0, 0, margemInferior) };

    static Label TituloCard(string texto) =>
        new() { Text = texto, Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };

    static TableLayoutPanel Linha(params (Control controle, bool expande)[] itens)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = itens.Length, RowCount = 1, Margin = new Padding(0)
        };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < itens.Length; i++)
        {
            t.ColumnStyles.Add(itens[i].expande ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            itens[i].controle.Margin = new Padding(i == 0 ? 0 : 8, 0, 0, 0);
            if (itens[i].controle.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
                itens[i].controle.Anchor = AnchorStyles.Left;
            t.Controls.Add(itens[i].controle, i, 0);
        }
        return t;
    }

    CardPanel MontarCardImpressora()
    {
        var card = new CardPanel();
        card.AdicionarLinha(TituloCard("Impressora"));

        var btnTeste = new BotaoFluent { Text = "Imprimir agora", Icone = Icones.Imprimir };
        btnTeste.Click += (s, e) => ImprimirTeste();
        _dicas.SetToolTip(btnTeste, "Imprime uma página agora para testar");
        var linhaImpressora = Linha((cbImpressora, true), (btnTeste, false));
        linhaImpressora.Margin = new Padding(0, 0, 0, 14);
        card.AdicionarLinha(linhaImpressora);

        // Documento padrão (opcional): se vazio, imprime a página colorida de sempre
        var rotuloDoc = Rotulo("Documento padrão (opcional)");
        card.AdicionarLinha(rotuloDoc);
        var btnEscolher = new BotaoFluent { Text = "Escolher…" };
        var btnLimpar = new BotaoFluent { Icone = Icones.Fechar, AccessibleName = "Limpar documento" };
        btnEscolher.Click += (s, e) => EscolherDocumento();
        btnLimpar.Click += (s, e) => txtDocumento.Text = "";
        _dicas.SetToolTip(btnLimpar, "Voltar para a página colorida padrão");
        card.AdicionarLinha(Linha((txtDocumento, true), (btnEscolher, false), (btnLimpar, false)));

        card.AdicionarLinha(new Label
        {
            Text = "Sem documento, é impressa uma página com faixas de todas as cores.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 6, 0, 0)
        });
        return card;
    }

    CardPanel MontarCardAgendamento()
    {
        var card = new CardPanel { Margin = new Padding(0) };
        card.AdicionarLinha(TituloCard("Agendamento"));

        // Dias da semana: 7 botões lado a lado
        card.AdicionarLinha(Rotulo("Dias da semana"));
        var gradeDias = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 7, RowCount = 1, Margin = new Padding(0, 0, 0, 16)
        };
        gradeDias.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < 7; i++)
        {
            gradeDias.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
            chkDias[i] = new BotaoDia { Text = NomesDias[i], AccessibleName = NomesDiasCompletos[i] };
            chkDias[i].CheckedChanged += (s, e) => AtualizarResumo();
            gradeDias.Controls.Add(chkDias[i], i, 0);
        }
        card.AdicionarLinha(gradeDias);

        // Frequência, horário, início e fim em duas colunas
        var grade = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 4, Margin = new Padding(0, 0, 0, 12)
        };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++) grade.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var repetir = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 8, 14)
        };
        numIntervalo.Margin = new Padding(0);
        numIntervalo.AccessibleName = "Repetir a cada quantas semanas";
        repetir.Controls.Add(numIntervalo);
        repetir.Controls.Add(lblSemanas);

        var cabecalhoFim = Linha((Rotulo("Terminar em", 0), true), (chkFim, false));
        cabecalhoFim.Margin = new Padding(8, 0, 0, 2);

        grade.Controls.Add(ComMargem(Rotulo("Repetir a cada"), esquerda: false), 0, 0);
        grade.Controls.Add(ComMargem(Rotulo("Horário"), esquerda: true), 1, 0);
        grade.Controls.Add(repetir, 0, 1);
        grade.Controls.Add(ComMargem(dtHora, esquerda: true, inferior: 14), 1, 1);
        grade.Controls.Add(ComMargem(Rotulo("Começar em"), esquerda: false, superior: 4), 0, 2);
        grade.Controls.Add(cabecalhoFim, 1, 2);
        grade.Controls.Add(ComMargem(dtInicio, esquerda: false), 0, 3);
        grade.Controls.Add(ComMargem(dtFim, esquerda: true), 1, 3);
        card.AdicionarLinha(grade);

        numIntervalo.ValueChanged += (s, e) => AtualizarResumo();
        dtHora.ValueChanged += (s, e) => AtualizarResumo();
        dtInicio.ValueChanged += (s, e) => AtualizarResumo();
        dtFim.ValueChanged += (s, e) => AtualizarResumo();
        chkFim.CheckedChanged += (s, e) => { dtFim.Enabled = chkFim.Checked; AtualizarResumo(); };

        card.AdicionarLinha(resumo);
        return card;
    }

    static Control ComMargem(Control c, bool esquerda, int superior = 0, int inferior = 6)
    {
        c.Margin = new Padding(esquerda ? 8 : 0, superior, esquerda ? 0 : 8, inferior);
        return c;
    }

    void MontarRodape()
    {
        rodape.Paint += (s, e) =>
        {
            using var pen = new Pen(Tema.BordaCard);
            e.Graphics.DrawLine(pen, 0, 0, rodape.Width, 0);
        };

        var btnHistorico = new BotaoFluent { Text = "Histórico", Icone = Icones.Historico, Estilo = EstiloBotao.Sutil };
        var btnDesativar = new BotaoFluent { Text = "Desativar" };
        var btnSalvar = new BotaoFluent { Text = "Salvar e ativar", Estilo = EstiloBotao.Primario };
        btnSalvar.Click += (s, e) => SalvarEAtivar();
        btnDesativar.Click += (s, e) => Desativar();
        btnHistorico.Click += (s, e) => AbrirHistorico();
        AcceptButton = btnSalvar;

        var barra = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(16, 0, 24, 0)
        };
        barra.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var b in new[] { btnHistorico, btnDesativar, btnSalvar }) b.Anchor = AnchorStyles.None;
        btnDesativar.Margin = new Padding(0, 0, 8, 0);
        barra.Controls.Add(btnHistorico, 0, 0);
        barra.Controls.Add(btnDesativar, 2, 0);
        barra.Controls.Add(btnSalvar, 3, 0);
        rodape.Controls.Add(barra);
    }

    // ---------------- Dados ----------------

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
        AtualizarResumo();
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

    // Frase que descreve o agendamento, atualizada a cada alteração
    void AtualizarResumo()
    {
        // Evita rodar enquanto a tela ainda está sendo montada
        if (chkDias.Any(c => c is null)) return;

        var cfg = LerTela();
        lblSemanas.Text = cfg.IntervaloSemanas == 1 ? "semana" : "semanas";

        if (cfg.Dias.Count == 0)
        {
            resumo.Text = "Marque pelo menos um dia da semana.";
            return;
        }

        var nomes = cfg.Dias.OrderBy(d => (int)d).Select(d => NomesDiasPlural[(int)d]).ToList();
        string dias = nomes.Count == 1 ? nomes[0] : string.Join(", ", nomes.Take(nomes.Count - 1)) + " e " + nomes[^1];
        string frequencia = cfg.IntervaloSemanas == 1 ? "Toda semana" : $"A cada {cfg.IntervaloSemanas} semanas";
        string inicio = cfg.Inicio.Date > DateTime.Today ? $", a partir de {cfg.Inicio:dd/MM/yyyy}" : "";
        string fim = cfg.TemFim ? $", até {cfg.Fim:dd/MM/yyyy}" : ", sem data de término";
        resumo.Text = $"{frequencia}: {dias}, às {cfg.Hora:hh\\:mm}{inicio}{fim}.";
    }

    void AtualizarStatus(Config cfg)
    {
        bool ativo = _agendador.Existe();

        if (!ativo)
        {
            barraStatus.Definir(Severidade.Neutro, "Impressão automática desativada",
                "Configure abaixo e clique em Salvar e ativar.");
        }
        else if (!StatusImpressora.EstaOnline(cfg.Impressora))
        {
            barraStatus.Definir(Severidade.Aviso, "Impressora desligada ou desconectada",
                "O agendamento está ativo, mas a impressão pode falhar até ela voltar a responder.");
        }
        else
        {
            var proxima = cfg.ProximaExecucao();
            if (proxima is null)
                barraStatus.Definir(Severidade.Aviso, "A data de término já passou",
                    "O agendamento está ativo, mas não vai imprimir. Altere as datas e salve de novo.");
            else
                barraStatus.Definir(Severidade.Sucesso, "Agendamento ativo",
                    $"Próxima impressão: {proxima:dddd, dd/MM/yyyy} às {proxima:HH:mm}");
        }

        trayIcon.Text = ativo ? "Manutenção da Impressora — ativo" : "Manutenção da Impressora — inativo";
    }

    // Avisa o usuário se as últimas impressões automáticas vêm falhando,
    // já que o agendamento roda sozinho e ninguém costuma olhar o histórico
    void VerificarFalhasRecentes()
    {
        int falhas = _log.FalhasAutomaticasConsecutivas();
        if (falhas < 2) return;

        MessageBox.Show(
            $"As últimas {falhas} impressões automáticas falharam.\n\n" +
            "Verifique se a impressora está ligada, conectada e com papel/tinta, " +
            "ou confira o histórico para ver o motivo do erro.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ---------------- Ações ----------------

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

    async Task VerificarAtualizacaoAsync()
    {
        var verificador = new VerificadorAtualizacao();
        var atualizacao = await verificador.VerificarAsync();
        if (atualizacao is null) return;

        var resposta = MessageBox.Show(
            $"Uma nova versão (v{atualizacao.Versao}) está disponível. Baixar e instalar agora?\n\n" +
            "O programa vai fechar para o instalador poder atualizar os arquivos.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (resposta != DialogResult.Yes) return;

        try
        {
            string destino = Path.Combine(Path.GetTempPath(), "ManutencaoImpressora_Instalador.exe");
            await verificador.BaixarInstaladorAsync(atualizacao.UrlInstalador, destino);
            Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true });
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível baixar a atualização: " + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        using var tela = new FormHistorico(_log);
        tela.ShowDialog(this);
    }
}

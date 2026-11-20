using System.Diagnostics;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Energia": manter o PC acordado (até desligar, por 1 hora, até os
//  downloads terminarem ou enquanto um programa estiver aberto),
//  desligar/reiniciar/suspender/hibernar depois, e o modo de energia.
// ---------------------------------------------------------------
public class AbaEnergia : AbaBase, INotificador
{
    enum ModoAcordado { AteDesligar, UmaHora, AteDownloads, EnquantoPrograma }
    enum QuandoAgendar { EmMinutos, NoHorario, AposDownloads }

    const double LimiteDownloadBytesPorSegundo = 100 * 1024;
    static readonly TimeSpan TempoParadoParaConcluir = TimeSpan.FromMinutes(5);

    // Manter acordado
    readonly Interruptor chkAcordado = new() { AccessibleName = "Manter acordado", Anchor = AnchorStyles.Right };
    readonly Label lblAcordado = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Margin = new Padding(0, 2, 0, 0) };
    readonly RadioButton[] radiosModo =
    {
        new() { Text = "Até eu desligar", AutoSize = true, Checked = true },
        new() { Text = "Por 1 hora", AutoSize = true },
        new() { Text = "Até os downloads terminarem (internet parada por 5 min)", AutoSize = true },
        new() { Text = "Enquanto este programa estiver aberto:", AutoSize = true }
    };
    readonly ComboBox cbPrograma = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly CheckBox chkTela = new() { Text = "Manter a tela ligada também", AutoSize = true, Checked = true };

    // Agendar
    readonly RadioButton[] radiosAcao =
    {
        new() { Text = "Desligar", AutoSize = true, Checked = true },
        new() { Text = "Reiniciar", AutoSize = true },
        new() { Text = "Suspender", AutoSize = true },
        new() { Text = "Hibernar", AutoSize = true }
    };
    readonly RadioButton rbMinutos = new() { Text = "Daqui a", AutoSize = true, Checked = true };
    readonly NumericUpDown numMinutos = new() { Minimum = 1, Maximum = 1440, Value = 60, Width = 70 };
    readonly RadioButton rbHorario = new() { Text = "Às", AutoSize = true };
    readonly DateTimePicker dtHorario = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    readonly RadioButton rbDownloads = new() { Text = "Quando os downloads terminarem", AutoSize = true };
    readonly BotaoFluent btnAgendar = new() { Text = "Agendar", Estilo = EstiloBotao.Primario };
    readonly TableLayoutPanel painelAgendar = NovoPainel();
    readonly TableLayoutPanel painelContagem = NovoPainel();
    readonly Label lblContagemTitulo = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
    readonly Label lblContagem = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 26f), Margin = new Padding(0, 0, 0, 8) };

    // Modo de energia
    readonly RadioButton[] radiosEnergia =
    {
        new() { Text = "Economia de energia", AutoSize = true },
        new() { Text = "Equilibrado", AutoSize = true },
        new() { Text = "Melhor desempenho", AutoSize = true }
    };
    readonly Label lblModoIndisponivel = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Visible = false,
        Text = "Este Windows não permite trocar o modo de energia por aqui. Use Configurações › Sistema › Energia." };
    readonly ListaDetalhes listaConfig = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    DateTime? _acordadoAte;
    DateTime? _agendadoPara;
    AcaoEnergia _acaoAgendada;
    bool _agendadoAposDownloads, _avisou2Min, _carregandoModo;
    DateTime _ultimoTrafego = DateTime.Now;
    long _bytesAnteriores = -1;
    DateTime _ultimaLeituraRede;

    public event Action<string, string, ToolTipIcon>? Notificar;

    public AbaEnergia() : base("Energia", "Impede o computador de dormir quando você precisa e agenda desligar ou reiniciar.")
    {
        var linha = new GradeAdaptativa { LarguraMinimaColuna = 400, MaximoColunas = 2, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        linha.Adicionar(MontarAcordado());
        linha.Adicionar(MontarAgendar());
        Adicionar(linha);
        Adicionar(MontarModo());

        var cardConfig = new CardPanel { Margin = new Padding(0) };
        cardConfig.AdicionarLinha(TituloCard("Configurações atuais do Windows"));
        cardConfig.AdicionarLinha(listaConfig);
        Adicionar(cardConfig);

        // Alguns botões de opção ficam em linhas diferentes (com campos ao lado): agrupa à mão
        Agrupar(radiosModo);
        Agrupar(rbMinutos, rbHorario, rbDownloads);

        chkAcordado.CheckedChanged += (s, e) => AplicarAcordado();
        foreach (var r in radiosModo) r.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) AplicarAcordado(); };
        chkTela.CheckedChanged += (s, e) => AplicarAcordado();
        cbPrograma.DropDown += (s, e) => CarregarProgramas();
        btnAgendar.Click += (s, e) => Agendar();
        for (int i = 0; i < radiosEnergia.Length; i++)
        {
            var modo = (ModoEnergia)i;
            radiosEnergia[i].CheckedChanged += (s, e) =>
            {
                if (_carregandoModo || !((RadioButton)s!).Checked) return;
                if (!Energia.DefinirModo(modo)) CarregarModo();
            };
        }
        dtHorario.Value = DateTime.Today.AddHours(DateTime.Now.Hour + 1);

        _timer.Tick += (s, e) => Tique();
        VisibleChanged += async (s, e) =>
        {
            AjustarRelogio();
            if (Visible) { CarregarModo(); await CarregarConfigAsync(); }
        };
        AtualizarTextos();
    }

    // ---------------- Montagem ----------------

    static void Agrupar(params RadioButton[] grupo)
    {
        foreach (var r in grupo)
            r.CheckedChanged += (s, e) =>
            {
                if (!((RadioButton)s!).Checked) return;
                foreach (var outro in grupo) if (outro != s) outro.Checked = false;
            };
    }

    static FlowLayoutPanel Fluxo(FlowDirection direcao = FlowDirection.LeftToRight) =>
        new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = direcao == FlowDirection.LeftToRight, FlowDirection = direcao, Margin = new Padding(0) };

    CardPanel MontarAcordado()
    {
        var card = new CardPanel();
        var topo = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var textos = Fluxo(FlowDirection.TopDown);
        textos.Controls.Add(new Label { Text = "Manter acordado", Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0) });
        textos.Controls.Add(lblAcordado);
        topo.Controls.Add(textos, 0, 0);
        topo.Controls.Add(chkAcordado, 1, 0);
        card.AdicionarLinha(topo);

        card.AdicionarLinha(new Label { Text = "Por quanto tempo", Font = Tema.CorpoForte, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        var modos = Fluxo(FlowDirection.TopDown);
        foreach (var r in radiosModo.Take(3)) { r.Margin = new Padding(0, 2, 0, 2); modos.Controls.Add(r); }
        var linhaPrograma = Fluxo();
        radiosModo[3].Margin = new Padding(0, 4, 6, 0);
        cbPrograma.AccessibleName = "Programa";
        linhaPrograma.Controls.Add(radiosModo[3]);
        linhaPrograma.Controls.Add(cbPrograma);
        modos.Controls.Add(linhaPrograma);
        card.AdicionarLinha(modos);
        chkTela.Margin = new Padding(0, 12, 0, 0);
        card.AdicionarLinha(chkTela);
        return card;
    }

    CardPanel MontarAgendar()
    {
        var card = new CardPanel();
        card.AdicionarLinha(new Label { Text = "Desligar ou reiniciar depois", Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0) });
        card.AdicionarLinha(new Label { Text = "Você recebe um aviso 2 minutos antes e pode cancelar.", ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 2, 0, 12) });

        painelAgendar.Controls.Clear();
        AdicionarEm(painelAgendar, new Label { Text = "O que fazer", Font = Tema.CorpoForte, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        var acoes = Fluxo();
        foreach (var r in radiosAcao) { r.Margin = new Padding(0, 0, 16, 0); acoes.Controls.Add(r); }
        acoes.Margin = new Padding(0, 0, 0, 12);
        AdicionarEm(painelAgendar, acoes);

        AdicionarEm(painelAgendar, new Label { Text = "Quando", Font = Tema.CorpoForte, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        var quando = Fluxo(FlowDirection.TopDown);
        var l1 = Fluxo(); rbMinutos.Margin = new Padding(0, 4, 6, 0); numMinutos.Margin = new Padding(0); l1.Controls.Add(rbMinutos); l1.Controls.Add(numMinutos);
        l1.Controls.Add(new Label { Text = "min", AutoSize = true, Margin = new Padding(6, 5, 0, 0) });
        var l2 = Fluxo(); rbHorario.Margin = new Padding(0, 4, 6, 0); dtHorario.Margin = new Padding(0); l2.Controls.Add(rbHorario); l2.Controls.Add(dtHorario);
        l1.Margin = l2.Margin = new Padding(0, 0, 0, 6);
        quando.Controls.Add(l1); quando.Controls.Add(l2); quando.Controls.Add(rbDownloads);
        quando.Margin = new Padding(0, 0, 0, 12);
        AdicionarEm(painelAgendar, quando);
        btnAgendar.Anchor = AnchorStyles.Left;
        AdicionarEm(painelAgendar, btnAgendar);
        card.AdicionarLinha(painelAgendar);

        AdicionarEm(painelContagem, lblContagemTitulo);
        AdicionarEm(painelContagem, lblContagem);
        var botoes = Fluxo();
        var cancelar = new BotaoFluent { Text = "Cancelar", Margin = new Padding(0, 0, 8, 0) };
        var adiar = new BotaoFluent { Text = "Adiar 30 min" };
        cancelar.Click += (s, e) => CancelarAgendamento();
        adiar.Click += (s, e) => { if (_agendadoPara is DateTime d) { _agendadoPara = d.AddMinutes(30); _avisou2Min = false; AtualizarTextos(); } };
        botoes.Controls.Add(cancelar); botoes.Controls.Add(adiar);
        AdicionarEm(painelContagem, botoes);
        painelContagem.Visible = false;
        card.AdicionarLinha(painelContagem);
        return card;
    }

    CardPanel MontarModo()
    {
        var card = new CardPanel();
        card.AdicionarLinha(TituloCard("Modo de energia"));
        var opcoes = Fluxo();
        foreach (var r in radiosEnergia) { r.Margin = new Padding(0, 0, 24, 0); opcoes.Controls.Add(r); }
        card.AdicionarLinha(opcoes);
        card.AdicionarLinha(lblModoIndisponivel);
        card.AdicionarLinha(new Label { Text = "É o mesmo ajuste de Configurações › Sistema › Energia. \"Equilibrado\" é o recomendado para o dia a dia.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        return card;
    }

    // ---------------- Manter acordado ----------------

    ModoAcordado Modo => (ModoAcordado)Array.FindIndex(radiosModo, r => r.Checked);

    void AplicarAcordado()
    {
        bool ligado = chkAcordado.Checked;
        if (ligado && Modo == ModoAcordado.EnquantoPrograma && cbPrograma.SelectedItem is null)
        {
            CarregarProgramas();
            if (cbPrograma.Items.Count > 0) cbPrograma.SelectedIndex = 0;
        }
        _acordadoAte = ligado && Modo == ModoAcordado.UmaHora ? DateTime.Now.AddHours(1) : null;
        _ultimoTrafego = DateTime.Now;
        Energia.ManterAcordado(ligado, chkTela.Checked);
        AtualizarTextos();
        AjustarRelogio();
    }

    void CarregarProgramas()
    {
        var atual = cbPrograma.SelectedItem as string;
        var nomes = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
            .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
        cbPrograma.Items.Clear();
        cbPrograma.Items.AddRange(nomes);
        if (atual != null && cbPrograma.Items.Contains(atual)) cbPrograma.SelectedItem = atual;
    }

    void DesligarAcordado(string motivo)
    {
        chkAcordado.Checked = false;   // dispara AplicarAcordado
        Notificar?.Invoke("Manter acordado desligado", motivo, ToolTipIcon.Info);
    }

    // ---------------- Agendar ----------------

    void Agendar()
    {
        _acaoAgendada = (AcaoEnergia)Array.FindIndex(radiosAcao, r => r.Checked);
        _agendadoAposDownloads = rbDownloads.Checked;
        _ultimoTrafego = DateTime.Now;
        if (rbMinutos.Checked) _agendadoPara = DateTime.Now.AddMinutes((double)numMinutos.Value);
        else if (rbHorario.Checked)
        {
            var hora = DateTime.Today + dtHorario.Value.TimeOfDay;
            _agendadoPara = hora > DateTime.Now ? hora : hora.AddDays(1);
        }
        else _agendadoPara = null;
        _avisou2Min = false;
        painelAgendar.Visible = false;
        painelContagem.Visible = true;
        AtualizarTextos();
        AjustarRelogio();
    }

    void CancelarAgendamento()
    {
        _agendadoPara = null;
        _agendadoAposDownloads = false;
        painelContagem.Visible = false;
        painelAgendar.Visible = true;
        AjustarRelogio();
    }

    // ---------------- A cada segundo ----------------

    void Tique()
    {
        // Tráfego da internet: só quando algo depende de "até os downloads terminarem"
        bool precisaRede = (chkAcordado.Checked && Modo == ModoAcordado.AteDownloads) || _agendadoAposDownloads;
        if (precisaRede)
        {
            long bytes = 0;
            try
            {
                foreach (var placa in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(RedeUtil.ContaParaUso))
                    bytes += placa.GetIPStatistics().BytesReceived;
            }
            catch { }
            var agora = DateTime.Now;
            double segundos = (agora - _ultimaLeituraRede).TotalSeconds;
            if (_bytesAnteriores >= 0 && segundos > 0 && (bytes - _bytesAnteriores) / segundos > LimiteDownloadBytesPorSegundo)
                _ultimoTrafego = agora;
            _bytesAnteriores = bytes;
            _ultimaLeituraRede = agora;
        }
        else _bytesAnteriores = -1;
        bool downloadsParados = DateTime.Now - _ultimoTrafego >= TempoParadoParaConcluir;

        if (chkAcordado.Checked)
        {
            if (Modo == ModoAcordado.UmaHora && _acordadoAte is DateTime ate && DateTime.Now >= ate)
                DesligarAcordado("Passou 1 hora. O computador volta a seguir as configurações normais.");
            else if (Modo == ModoAcordado.AteDownloads && downloadsParados)
                DesligarAcordado("Os downloads terminaram (internet parada há 5 minutos).");
            else if (Modo == ModoAcordado.EnquantoPrograma && cbPrograma.SelectedItem is string nome && Process.GetProcessesByName(nome).Length == 0)
                DesligarAcordado($"O programa \"{nome}\" foi fechado.");
        }

        if (_agendadoAposDownloads && downloadsParados && _agendadoPara is null)
        {
            _agendadoPara = DateTime.Now.AddMinutes(2);   // dá os 2 minutos de aviso
            _agendadoAposDownloads = false;
        }

        if (_agendadoPara is DateTime quando)
        {
            var falta = quando - DateTime.Now;
            if (falta <= TimeSpan.FromMinutes(2) && !_avisou2Min)
            {
                _avisou2Min = true;
                Notificar?.Invoke($"O computador vai {Energia.Verbo(_acaoAgendada)} em 2 minutos",
                    "Abra o L2 Utilitários › Energia para cancelar ou adiar.", ToolTipIcon.Warning);
            }
            if (falta <= TimeSpan.Zero)
            {
                var acao = _acaoAgendada;
                CancelarAgendamento();
                Energia.Executar(acao);
            }
        }

        if (Visible) AtualizarTextos();
        AjustarRelogio();
    }

    // 1 s com a tela aberta ou perto da hora agendada; 5 s acompanhando algo em segundo plano;
    // parado quando não há nada para acompanhar
    void AjustarRelogio()
    {
        bool agendado = painelContagem.Visible;
        bool pertoDaHora = _agendadoPara is DateTime quando && quando - DateTime.Now < TimeSpan.FromMinutes(3);
        bool condicional = chkAcordado.Checked && Modo != ModoAcordado.AteDesligar;
        int? intervalo = Visible || pertoDaHora ? 1_000 : agendado || condicional ? 5_000 : null;
        if (intervalo is null) { _timer.Stop(); return; }
        if (_timer.Interval != intervalo) _timer.Interval = intervalo.Value;
        _timer.Start();
    }

    void AtualizarTextos()
    {
        if (!chkAcordado.Checked) lblAcordado.Text = "Desligado · o Windows segue as configurações normais";
        else lblAcordado.Text = Modo switch
        {
            ModoAcordado.UmaHora when _acordadoAte is DateTime ate => $"Ligado · o computador não dorme até {ate:HH:mm}",
            ModoAcordado.AteDownloads => "Ligado · até a internet ficar parada por 5 minutos",
            ModoAcordado.EnquantoPrograma => $"Ligado · enquanto \"{cbPrograma.SelectedItem}\" estiver aberto",
            _ => "Ligado · até você desligar"
        };

        if (painelContagem.Visible)
        {
            string verbo = Energia.Verbo(_acaoAgendada);
            if (_agendadoPara is DateTime quando)
            {
                var falta = quando - DateTime.Now;
                lblContagemTitulo.Text = $"O computador vai {verbo} às {quando:HH:mm}";
                lblContagem.Text = falta.TotalHours >= 1 ? falta.ToString(@"h\:mm\:ss") : falta.ToString(@"mm\:ss");
            }
            else
            {
                lblContagemTitulo.Text = $"O computador vai {verbo} quando os downloads terminarem";
                lblContagem.Text = $"parada há {FormatoTempo.Duracao(DateTime.Now - _ultimoTrafego)}";
            }
        }
    }

    // ---------------- Modo e configurações ----------------

    void CarregarModo()
    {
        var modo = Energia.ModoAtual();
        _carregandoModo = true;
        lblModoIndisponivel.Visible = modo is null;
        foreach (var r in radiosEnergia) r.Enabled = modo is not null;
        if (modo is ModoEnergia m) radiosEnergia[(int)m].Checked = true;
        _carregandoModo = false;
    }

    DateTime _configLidaEm = DateTime.MinValue;
    (int? Tela, int? Suspender) _tempos;

    async Task CarregarConfigAsync()
    {
        if (DateTime.Now - _configLidaEm > TimeSpan.FromMinutes(1))
        {
            _configLidaEm = DateTime.Now;
            _tempos = await Task.Run(Energia.TemposAtuais);   // roda o powercfg (lento)
        }
        CarregarConfig();
    }

    void CarregarConfig()
    {
        var (tela, suspender) = _tempos;
        string Minutos(int? m) => m is null ? "não foi possível ler" : m == 0 ? "nunca" : $"{m} minutos";
        var bateria = SystemInformation.PowerStatus;
        string textoBateria = bateria.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)
            ? "Este computador não tem bateria"
            : $"{bateria.BatteryLifePercent:P0}" + (bateria.PowerLineStatus == PowerLineStatus.Online ? " · na tomada" : " · na bateria");
        listaConfig.Definir(new List<ItemDetalhe>
        {
            new("", Minutos(tela), "Desligar a tela após (na tomada)", ""),
            new("", Minutos(suspender) + (chkAcordado.Checked ? " (pausado: \"Manter acordado\" está ligado)" : ""), "Suspender o computador após (na tomada)", ""),
            new("", textoBateria, "Bateria", "")
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            Energia.ManterAcordado(false, false);
        }
        base.Dispose(disposing);
    }
}

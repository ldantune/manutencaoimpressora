using System.Net.NetworkInformation;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Aba "Rede": uso de internet em tempo real (download/upload) e
//  totais de hoje / últimos 7 dias / mês atual, sem depender de
//  programas de terceiro como o NetWorx
// ---------------------------------------------------------------
public class AbaRede : UserControl, IAbaUtilitario
{
    const int SegundosPorFlush = 30;

    readonly HistoricoRede _historicoRede;

    readonly TableLayoutPanel conteudo = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Padding = new Padding(24, 12, 24, 8)
    };

    readonly ComboBox cbInterface = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly Label lblDownload = new() { Text = "↓ —", Font = Tema.Titulo, AutoSize = true };
    readonly Label lblUpload = new() { Text = "↑ —", Font = Tema.Titulo, AutoSize = true };
    readonly GraficoLinha graficoDownload = new() { Height = 110, Dock = DockStyle.Fill, CorLinha = Tema.Destaque };
    readonly GraficoLinha graficoUpload = new() { Height = 110, Dock = DockStyle.Fill, CorLinha = Color.FromArgb(15, 123, 15) };
    readonly Label lblUsoHoje = new() { AutoSize = true };
    readonly Label lblUsoSemana = new() { AutoSize = true };
    readonly Label lblUsoMes = new() { AutoSize = true };
    // "Ao vivo" (velocidade e gráficos) e "Relatório" (resumo por período)
    readonly BotaoPivo pivoAoVivo = new() { Text = "Ao vivo", Checked = true };
    readonly BotaoPivo pivoRelatorio = new() { Text = "Relatório" };
    readonly BotaoPivo pivoApps = new() { Text = "Por app" };
    readonly AppsRede appsRede;
    readonly FonteUsoRede _fonte;
    bool _monitorRodando;
    readonly TableLayoutPanel painelAoVivo = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0)
    };
    readonly RelatorioRede relatorio;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    readonly Dictionary<string, (long Recebidos, long Enviados)> _anterioresPorInterface = new();
    long _pendenteRecebidos, _pendenteEnviados;
    DateTime _ultimoFlush = DateTime.Now;
    bool _primeiraAmostraSelecionada = true;

    public int AlturaPreferida => conteudo.Height;
    public event EventHandler? AlturaMudou;

    public AbaRede(HistoricoRede historicoRede)
    {
        _historicoRede = historicoRede;
        _fonte = new FonteUsoRede(historicoRede, new BancoUsoApps(BancoUsoApps.CaminhoPadrao));
        _monitorRodando = InstaladorServico.EstaRodando();
        relatorio = new RelatorioRede(_fonte) { Visible = false };
        appsRede = new AppsRede(_fonte) { Visible = false };

        SuspendLayout();
        Dock = DockStyle.Fill;
        BackColor = Tema.Fundo;

        conteudo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AdicionarConteudo(new Label
        {
            Text = "Uso de internet", Font = Tema.Titulo, AutoSize = true, Margin = new Padding(0, 0, 0, 2)
        });
        AdicionarConteudo(new Label
        {
            Text = "Velocidade em tempo real e relatório de uso, sem precisar de outro programa.",
            ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 0, 0, 14)
        });
        AdicionarConteudo(MontarPivo());

        painelAoVivo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AdicionarEm(painelAoVivo, MontarCardInterface());
        AdicionarEm(painelAoVivo, MontarCardTrafego());
        AdicionarEm(painelAoVivo, MontarCardUso());
        AdicionarConteudo(painelAoVivo);
        AdicionarConteudo(relatorio);
        AdicionarConteudo(appsRede);

        Controls.Add(conteudo);

        CarregarInterfaces();
        cbInterface.SelectedIndexChanged += (s, e) => ReiniciarAmostragemSelecionada();
        _timer.Tick += (s, e) => AtualizarAmostra();
        _timer.Start();
        VisibleChanged += (s, e) =>
        {
            if (Visible) _primeiraAmostraSelecionada = true;   // evita um "pico" falso depois de um intervalo longo
            AjustarRitmo();
        };
        AtualizarResumoUso();

        ResumeLayout(false);
        PerformLayout();
        conteudo.SizeChanged += (s, e) => AlturaMudou?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            Descarregar();
        }
        base.Dispose(disposing);
    }

    // ---------------- Montagem da tela ----------------

    void AdicionarConteudo(Control c) => AdicionarEm(conteudo, c);

    static void AdicionarEm(TableLayoutPanel tabela, Control c)
    {
        if (c is CardPanel) c.Dock = DockStyle.Fill;
        tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabela.RowCount = tabela.RowStyles.Count;
        tabela.Controls.Add(c, 0, tabela.RowCount - 1);
    }

    Control MontarPivo()
    {
        var pivo = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        pivo.Controls.Add(pivoAoVivo);
        pivo.Controls.Add(pivoRelatorio);
        pivo.Controls.Add(pivoApps);
        foreach (var opcao in new[] { pivoAoVivo, pivoRelatorio, pivoApps })
            opcao.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) MostrarVisao(); };
        return pivo;
    }

    void MostrarVisao()
    {
        SuspendLayout();
        painelAoVivo.Visible = pivoAoVivo.Checked;
        relatorio.Visible = pivoRelatorio.Checked;
        appsRede.Visible = pivoApps.Checked;
        if (relatorio.Visible) relatorio.Atualizar(PendenteRecebidos, PendenteEnviados);
        if (appsRede.Visible) appsRede.Atualizar();
        ResumeLayout(true);
    }

    static Label TituloCard(string texto) =>
        new() { Text = texto, Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };

    CardPanel MontarCardInterface()
    {
        var card = new CardPanel();
        card.AdicionarLinha(TituloCard("Interface de rede"));
        cbInterface.Margin = new Padding(0);
        card.AdicionarLinha(cbInterface);
        return card;
    }

    CardPanel MontarCardTrafego()
    {
        var card = new CardPanel { Margin = new Padding(0) };
        card.AdicionarLinha(TituloCard("Tráfego em tempo real"));

        var linhaDownload = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0)
        };
        linhaDownload.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linhaDownload.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        lblDownload.ForeColor = Tema.Destaque;
        linhaDownload.Controls.Add(lblDownload, 0, 0);
        linhaDownload.Controls.Add(graficoDownload, 0, 1);

        var linhaUpload = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 0)
        };
        linhaUpload.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linhaUpload.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        lblUpload.ForeColor = Color.FromArgb(15, 123, 15);
        linhaUpload.Controls.Add(lblUpload, 0, 0);
        linhaUpload.Controls.Add(graficoUpload, 0, 1);
        var grade = new GradeAdaptativa { LarguraMinimaColuna = 320, MaximoColunas = 2, Espaco = 16 };
        grade.Adicionar(linhaDownload);
        grade.Adicionar(linhaUpload);
        card.AdicionarLinha(grade);

        return card;
    }

    CardPanel MontarCardUso()
    {
        var card = new CardPanel { Margin = new Padding(0) };
        card.AdicionarLinha(TituloCard("Uso de dados"));

        var grade = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 0, 0, 8)
        };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) grade.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        void AdicionarLinhaUso(Label rotulo, Label valor, int linha)
        {
            rotulo.ForeColor = Tema.TextoSecundario;
            rotulo.Margin = new Padding(0, 0, 16, linha == 2 ? 0 : 8);
            valor.Margin = new Padding(0, 0, 0, linha == 2 ? 0 : 8);
            grade.Controls.Add(rotulo, 0, linha);
            grade.Controls.Add(valor, 1, linha);
        }

        AdicionarLinhaUso(new Label { Text = "Hoje", AutoSize = true }, lblUsoHoje, 0);
        AdicionarLinhaUso(new Label { Text = "Últimos 7 dias", AutoSize = true }, lblUsoSemana, 1);
        AdicionarLinhaUso(new Label { Text = "Este mês", AutoSize = true }, lblUsoMes, 2);
        card.AdicionarLinha(grade);

        card.AdicionarLinha(new Label
        {
            Text = "Com o monitor de rede ativo, conta o tempo todo; sem ele, só enquanto o programa estiver aberto.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true,
            MaximumSize = new Size(440, 0), Margin = new Padding(0, 2, 0, 0)
        });
        return card;
    }

    // ---------------- Dados ----------------

    void CarregarInterfaces()
    {
        _interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                      && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                      && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            // A conexão real com a internet primeiro: com gateway padrão e não virtual
            // (ex.: "vEthernet (Default Switch)" do Hyper-V vai para o fim da lista)
            .OrderByDescending(TemGateway)
            .ThenBy(EhVirtual)
            .ThenByDescending(ni => ni.Speed)
            .ToArray();

        cbInterface.Items.Clear();
        foreach (var ni in _interfaces) cbInterface.Items.Add(ni.Name);
        if (cbInterface.Items.Count > 0) cbInterface.SelectedIndex = 0;
    }

    static bool TemGateway(NetworkInterface ni)
    {
        try
        {
            return ni.GetIPProperties().GatewayAddresses
                .Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                          && !g.Address.Equals(System.Net.IPAddress.Any));
        }
        catch { return false; }
    }

    static bool EhVirtual(NetworkInterface ni) =>
        ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || ni.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase);

    void ReiniciarAmostragemSelecionada()
    {
        _primeiraAmostraSelecionada = true;
        graficoDownload.Limpar();
        graficoUpload.Limpar();
        lblDownload.Text = "↓ —";
        lblUpload.Text = "↑ —";
    }

    // A cada segundo: mede todas as interfaces ativas (para contar o uso
    // total certo), mas só mostra o gráfico/velocidade da selecionada
    // Ritmo da medição: 1 s com a tela aberta (gráfico ao vivo); escondida, 5 s se for a tela
    // que conta o uso, ou 1 minuto (só conferindo o monitor) se o serviço já está contando
    void AjustarRitmo()
    {
        int intervalo = Visible ? 1_000 : _monitorRodando ? 60_000 : 5_000;
        if (_timer.Interval != intervalo) _timer.Interval = intervalo;
    }

    void AtualizarAmostra()
    {
        if (!Visible && _monitorRodando)
        {
            // O monitor está contando: aqui só confere se ele continua rodando
            _monitorRodando = InstaladorServico.EstaRodando();
            _anterioresPorInterface.Clear();
            AjustarRitmo();
            return;
        }

        int indiceSelecionado = cbInterface.SelectedIndex;
        long totalRecebidosTick = 0, totalEnviadosTick = 0;

        for (int i = 0; i < _interfaces.Length; i++)
        {
            var ni = _interfaces[i];
            IPv4InterfaceStatistics estatisticas;
            try { estatisticas = ni.GetIPv4Statistics(); }
            catch { continue; }

            long recebidos = estatisticas.BytesReceived;
            long enviados = estatisticas.BytesSent;

            if (_anterioresPorInterface.TryGetValue(ni.Id, out var anterior))
            {
                long down = Math.Max(0, recebidos - anterior.Recebidos);
                long up = Math.Max(0, enviados - anterior.Enviados);
                totalRecebidosTick += down;
                totalEnviadosTick += up;

                if (Visible && i == indiceSelecionado && !_primeiraAmostraSelecionada)
                {
                    lblDownload.Text = "↓ " + FormatarTaxa(down);
                    lblUpload.Text = "↑ " + FormatarTaxa(up);
                    graficoDownload.AdicionarAmostra(down);
                    graficoUpload.AdicionarAmostra(up);
                }
                if (i == indiceSelecionado) _primeiraAmostraSelecionada = false;
            }

            _anterioresPorInterface[ni.Id] = (recebidos, enviados);
        }

        _pendenteRecebidos += totalRecebidosTick;
        _pendenteEnviados += totalEnviadosTick;
        if ((DateTime.Now - _ultimoFlush).TotalSeconds >= SegundosPorFlush)
        {
            Descarregar();
            AtualizarResumoUso();
            if (relatorio.Visible) relatorio.Atualizar(PendenteRecebidos, PendenteEnviados);
            if (appsRede.Visible) appsRede.Atualizar();
        }
    }

    // Com o monitor de rede rodando, ele é quem conta (sempre, mesmo com a janela fechada);
    // a contagem da tela só é gravada quando o monitor está parado, para não contar em dobro
    long PendenteRecebidos => _monitorRodando ? 0 : _pendenteRecebidos;
    long PendenteEnviados => _monitorRodando ? 0 : _pendenteEnviados;

    // Grava no banco o que foi acumulado desde o último flush
    void Descarregar()
    {
        _monitorRodando = InstaladorServico.EstaRodando();
        if (!_monitorRodando && (_pendenteRecebidos > 0 || _pendenteEnviados > 0))
            _historicoRede.Acumular(DateTime.Today, _pendenteRecebidos, _pendenteEnviados);
        _pendenteRecebidos = 0;
        _pendenteEnviados = 0;
        _ultimoFlush = DateTime.Now;
        AjustarRitmo();
    }

    void AtualizarResumoUso()
    {
        var hoje = DateTime.Today;
        var semana = _fonte.TotalEntre(hoje.AddDays(-6), hoje);
        var mes = _fonte.TotalEntre(new DateTime(hoje.Year, hoje.Month, 1), hoje);
        var doDia = _fonte.TotalEntre(hoje, hoje);

        // Inclui o que ainda não foi gravado, para o número não "atrasar"
        lblUsoHoje.Text = FormatarUso(doDia.Recebidos + PendenteRecebidos, doDia.Enviados + PendenteEnviados);
        lblUsoSemana.Text = FormatarUso(semana.Recebidos + PendenteRecebidos, semana.Enviados + PendenteEnviados);
        lblUsoMes.Text = FormatarUso(mes.Recebidos + PendenteRecebidos, mes.Enviados + PendenteEnviados);
    }

    static string FormatarUso(long recebidos, long enviados) =>
        $"↓ {FormatarBytes(recebidos)}   ↑ {FormatarBytes(enviados)}";

    static string FormatarTaxa(double bytesPorSegundo)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytesPorSegundo >= GB) return $"{bytesPorSegundo / GB:0.00} GB/s";
        if (bytesPorSegundo >= MB) return $"{bytesPorSegundo / MB:0.0} MB/s";
        if (bytesPorSegundo >= KB) return $"{bytesPorSegundo / KB:0} KB/s";
        return $"{bytesPorSegundo:0} B/s";
    }

    static string FormatarBytes(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024, TB = GB * 1024;
        if (bytes >= TB) return $"{bytes / TB:0.00} TB";
        if (bytes >= GB) return $"{bytes / GB:0.00} GB";
        if (bytes >= MB) return $"{bytes / MB:0.0} MB";
        if (bytes >= KB) return $"{bytes / KB:0} KB";
        return $"{bytes} B";
    }
}

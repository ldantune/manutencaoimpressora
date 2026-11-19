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
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    readonly Dictionary<string, (long Recebidos, long Enviados)> _anterioresPorInterface = new();
    long _pendenteRecebidos, _pendenteEnviados;
    int _segundosDesdeUltimoFlush;
    bool _primeiraAmostraSelecionada = true;

    public int AlturaPreferida => conteudo.Height;
    public event EventHandler? AlturaMudou;

    public AbaRede(HistoricoRede historicoRede)
    {
        _historicoRede = historicoRede;

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
            Text = "Velocidade de download e upload em tempo real, sem precisar de outro programa.",
            ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 0, 0, 14)
        });
        AdicionarConteudo(MontarCardInterface());
        AdicionarConteudo(MontarCardTrafego());
        AdicionarConteudo(MontarCardUso());

        Controls.Add(conteudo);

        CarregarInterfaces();
        cbInterface.SelectedIndexChanged += (s, e) => ReiniciarAmostragemSelecionada();
        _timer.Tick += (s, e) => AtualizarAmostra();
        _timer.Start();
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

    void AdicionarConteudo(Control c)
    {
        if (c is CardPanel) c.Dock = DockStyle.Fill;
        conteudo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conteudo.RowCount = conteudo.RowStyles.Count;
        conteudo.Controls.Add(c, 0, conteudo.RowCount - 1);
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
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 16)
        };
        linhaDownload.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linhaDownload.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        lblDownload.ForeColor = Tema.Destaque;
        linhaDownload.Controls.Add(lblDownload, 0, 0);
        linhaDownload.Controls.Add(graficoDownload, 0, 1);
        card.AdicionarLinha(linhaDownload);

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
        card.AdicionarLinha(linhaUpload);

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
            Text = "Contado enquanto este programa estiver aberto (ele continua rodando na bandeja do sistema).",
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
            .OrderByDescending(ni => ni.Speed)
            .ToArray();

        cbInterface.Items.Clear();
        foreach (var ni in _interfaces) cbInterface.Items.Add(ni.Name);
        if (cbInterface.Items.Count > 0) cbInterface.SelectedIndex = 0;
    }

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
    void AtualizarAmostra()
    {
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

                if (i == indiceSelecionado && !_primeiraAmostraSelecionada)
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
        if (++_segundosDesdeUltimoFlush >= SegundosPorFlush)
        {
            Descarregar();
            AtualizarResumoUso();
        }
    }

    // Grava no banco o que foi acumulado desde o último flush
    void Descarregar()
    {
        if (_pendenteRecebidos > 0 || _pendenteEnviados > 0)
            _historicoRede.Acumular(DateTime.Today, _pendenteRecebidos, _pendenteEnviados);
        _pendenteRecebidos = 0;
        _pendenteEnviados = 0;
        _segundosDesdeUltimoFlush = 0;
    }

    void AtualizarResumoUso()
    {
        var hoje = _historicoRede.TotalHoje();
        var semana = _historicoRede.TotalUltimos7Dias();
        var mes = _historicoRede.TotalMesAtual();

        // Inclui o que ainda não foi gravado, para o número não "atrasar"
        lblUsoHoje.Text = FormatarUso(hoje.Recebidos + _pendenteRecebidos, hoje.Enviados + _pendenteEnviados);
        lblUsoSemana.Text = FormatarUso(semana.Recebidos + _pendenteRecebidos, semana.Enviados + _pendenteEnviados);
        lblUsoMes.Text = FormatarUso(mes.Recebidos + _pendenteRecebidos, mes.Enviados + _pendenteEnviados);
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

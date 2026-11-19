using System.Net.NetworkInformation;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Aba "Rede": uso de internet em tempo real (download/upload),
//  sem depender de programas de terceiro como o NetWorx
// ---------------------------------------------------------------
public class AbaRede : UserControl, IAbaUtilitario
{
    readonly TableLayoutPanel conteudo = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Padding = new Padding(24, 12, 24, 8)
    };

    readonly ComboBox cbInterface = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly Label lblDownload = new() { Text = "↓ —", Font = Tema.Titulo, AutoSize = true };
    readonly Label lblUpload = new() { Text = "↑ —", Font = Tema.Titulo, AutoSize = true };
    readonly Label lblTotalDownload = new() { AutoSize = true, ForeColor = Tema.TextoSecundario };
    readonly Label lblTotalUpload = new() { AutoSize = true, ForeColor = Tema.TextoSecundario };
    readonly GraficoLinha graficoDownload = new() { Height = 110, Dock = DockStyle.Fill, CorLinha = Tema.Destaque };
    readonly GraficoLinha graficoUpload = new() { Height = 110, Dock = DockStyle.Fill, CorLinha = Color.FromArgb(15, 123, 15) };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    long _bytesRecebidosAnterior, _bytesEnviadosAnterior;
    bool _primeiraAmostra = true;

    public int AlturaPreferida => conteudo.Height;
    public event EventHandler? AlturaMudou;

    public AbaRede()
    {
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

        Controls.Add(conteudo);

        CarregarInterfaces();
        cbInterface.SelectedIndexChanged += (s, e) => ReiniciarAmostragem();
        _timer.Tick += (s, e) => AtualizarAmostra();
        _timer.Start();

        ResumeLayout(false);
        PerformLayout();
        conteudo.SizeChanged += (s, e) => AlturaMudou?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
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
        card.AdicionarLinha(lblTotalDownload);

        var linhaUpload = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 16, 0, 16)
        };
        linhaUpload.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linhaUpload.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        lblUpload.ForeColor = Color.FromArgb(15, 123, 15);
        linhaUpload.Controls.Add(lblUpload, 0, 0);
        linhaUpload.Controls.Add(graficoUpload, 0, 1);
        card.AdicionarLinha(linhaUpload);
        card.AdicionarLinha(lblTotalUpload);

        card.AdicionarLinha(new Label
        {
            Text = "Os totais são desde a última vez que a conexão foi reiniciada (não desde que este programa abriu).",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true,
            MaximumSize = new Size(440, 0), Margin = new Padding(0, 4, 0, 0)
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

    void ReiniciarAmostragem()
    {
        _primeiraAmostra = true;
        graficoDownload.Limpar();
        graficoUpload.Limpar();
        lblDownload.Text = "↓ —";
        lblUpload.Text = "↑ —";
    }

    void AtualizarAmostra()
    {
        int indice = cbInterface.SelectedIndex;
        if (indice < 0 || indice >= _interfaces.Length) return;

        IPv4InterfaceStatistics estatisticas;
        try { estatisticas = _interfaces[indice].GetIPv4Statistics(); }
        catch { return; }

        long recebidos = estatisticas.BytesReceived;
        long enviados = estatisticas.BytesSent;

        if (!_primeiraAmostra)
        {
            double down = Math.Max(0, recebidos - _bytesRecebidosAnterior);
            double up = Math.Max(0, enviados - _bytesEnviadosAnterior);
            lblDownload.Text = "↓ " + FormatarTaxa(down);
            lblUpload.Text = "↑ " + FormatarTaxa(up);
            graficoDownload.AdicionarAmostra(down);
            graficoUpload.AdicionarAmostra(up);
        }
        _primeiraAmostra = false;
        _bytesRecebidosAnterior = recebidos;
        _bytesEnviadosAnterior = enviados;

        lblTotalDownload.Text = "Total recebido: " + FormatarBytes(recebidos);
        lblTotalUpload.Text = "Total enviado: " + FormatarBytes(enviados);
    }

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

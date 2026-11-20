using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Visão "Por app" da aba Rede: quanto cada programa usou de internet
//  hoje / nos últimos 7 dias / no mês. Os dados vêm do monitor de rede
//  (serviço) e, para dias antigos, do histórico do próprio Windows.
// ---------------------------------------------------------------
public class AppsRede : TableLayoutPanel
{
    const int MaximoApps = 15;

    readonly FonteUsoRede _fonte;

    readonly BotaoPivo pivoHoje = new() { Text = "Hoje", Checked = true };
    readonly BotaoPivo pivoSemana = new() { Text = "7 dias" };
    readonly BotaoPivo pivoMes = new() { Text = "Este mês" };

    readonly BarraStatus barraMonitor = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    readonly BotaoFluent btnAtivar = new() { Text = "Ativar monitor de rede", Estilo = EstiloBotao.Primario };
    readonly TableLayoutPanel painelMonitor = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Margin = new Padding(0, 0, 0, 12)
    };

    readonly ListaApps lista = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    readonly Label lblTotal = new() { AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right };
    readonly Label lblHistorico = new()
    {
        AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 0, 0, 4)
    };

    Task? _importacao;
    DateTime _ultimaImportacao = DateTime.MinValue;

    public AppsRede(FonteUsoRede fonte)
    {
        _fonte = fonte;

        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AdicionarLinha(MontarAvisoMonitor());
        AdicionarLinha(MontarCardLista());
        AdicionarLinha(new Label
        {
            Text = "O monitor de rede atualiza a cada 30 segundos, mesmo com o programa fechado. " +
                   "Navegadores aparecem como um app só (sem divisão por site).",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 2)
        });
        AdicionarLinha(lblHistorico);

        foreach (var pivo in new[] { pivoHoje, pivoSemana, pivoMes })
            pivo.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) Atualizar(); };
        btnAtivar.Click += async (s, e) => await AtivarMonitorAsync();
    }

    void AdicionarLinha(Control c)
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowCount = RowStyles.Count;
        Controls.Add(c, 0, RowCount - 1);
    }

    Control MontarAvisoMonitor()
    {
        painelMonitor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        painelMonitor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        painelMonitor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        barraMonitor.Margin = new Padding(0, 0, 0, 8);
        barraMonitor.Definir(Severidade.Aviso, "Monitor de rede desligado",
            "Para saber quanto cada programa usa, o L2 precisa de um serviço do Windows que roda em segundo plano. " +
            "Ele é instalado uma vez só e pede permissão de administrador.");
        btnAtivar.Anchor = AnchorStyles.Left;
        painelMonitor.Controls.Add(barraMonitor, 0, 0);
        painelMonitor.Controls.Add(btnAtivar, 0, 1);
        return painelMonitor;
    }

    CardPanel MontarCardLista()
    {
        var card = new CardPanel { Dock = DockStyle.Fill };

        var cabecalho = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8)
        };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var periodos = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0)
        };
        periodos.Controls.Add(pivoHoje);
        periodos.Controls.Add(pivoSemana);
        periodos.Controls.Add(pivoMes);
        cabecalho.Controls.Add(periodos, 0, 0);
        cabecalho.Controls.Add(lblTotal, 1, 0);
        card.AdicionarLinha(cabecalho);

        card.AdicionarLinha(lista);
        return card;
    }

    DateTime InicioPeriodo()
    {
        var hoje = DateTime.Today;
        if (pivoSemana.Checked) return hoje.AddDays(-6);
        if (pivoMes.Checked) return new DateTime(hoje.Year, hoje.Month, 1);
        return hoje;
    }

    public void Atualizar()
    {
        bool rodando = InstaladorServico.EstaRodando();
        bool desatualizado = InstaladorServico.PrecisaAtualizar();
        painelMonitor.Visible = !rodando || desatualizado;
        if (desatualizado)
        {
            barraMonitor.Definir(Severidade.Aviso, "Monitor de rede desatualizado",
                "O serviço está rodando outra cópia do programa (uma versão antiga). Atualize para ele passar a usar esta versão. " +
                "Pede permissão de administrador.");
            btnAtivar.Text = "Atualizar monitor de rede";
        }
        else if (!rodando)
        {
            barraMonitor.Definir(Severidade.Aviso, "Monitor de rede desligado",
                "Para saber quanto cada programa usa, o L2 precisa de um serviço do Windows que roda em segundo plano. " +
                "Ele é instalado uma vez só e pede permissão de administrador.");
            btnAtivar.Text = "Ativar monitor de rede";
        }

        var apps = _fonte.ListarApps(InicioPeriodo());
        long total = apps.Sum(a => a.Total);

        // Mostra os maiores e junta o resto em "Outros"
        var visiveis = apps.Take(MaximoApps).ToList();
        var resto = apps.Skip(MaximoApps).ToList();
        if (resto.Count > 0)
            visiveis.Add(new UsoApp($"Outros ({resto.Count} apps)", "", resto.Sum(a => a.Recebidos), resto.Sum(a => a.Enviados)));

        lista.TextoVazio = rodando || _fonte.Monitor.Existe
            ? "Nenhum uso registrado neste período ainda."
            : "Ative o monitor de rede para começar a contar o uso por aplicativo.";
        lista.Definir(visiveis);
        lblTotal.Text = apps.Count == 0 ? "" : $"{apps.Count} apps · {FormatoBytes.Formatar(total)}";

        // Uma vez por hora, busca no Windows os dias de antes do monitor
        if (_importacao is null && DateTime.Now - _ultimaImportacao > TimeSpan.FromHours(1))
            _importacao = ImportarHistoricoAsync();
    }

    async Task ImportarHistoricoAsync()
    {
        _ultimaImportacao = DateTime.Now;
        lblHistorico.Text = "Buscando o histórico de uso que o Windows já tem…";
        try
        {
            await ImportadorHistoricoWindows.ImportarAsync(_fonte);
            var primeiro = _fonte.Local.PrimeiroDiaWindows();
            lblHistorico.Text = primeiro is DateTime d
                ? $"Dias anteriores ao monitor vêm do histórico do Windows (desde {d:dd/MM/yyyy})."
                : "";
        }
        catch
        {
            lblHistorico.Text = "Não foi possível ler o histórico de uso do Windows neste computador.";
        }
        finally
        {
            _importacao = null;
        }
        if (Visible) Atualizar();   // a importação acabou de rodar, não repete
    }

    async Task AtivarMonitorAsync()
    {
        btnAtivar.Enabled = false;
        btnAtivar.Text = "Ativando…";
        var resultado = await InstaladorServico.InstalarComPermissaoAsync();
        btnAtivar.Enabled = true;

        if (resultado == InstaladorServico.ResultadoInstalacao.Recusado)
            MessageBox.Show(FindForm(),
                "O monitor de rede não foi ativado porque o pedido de administrador foi recusado.",
                "Monitor de rede", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else if (resultado == InstaladorServico.ResultadoInstalacao.Falhou)
        {
            string detalhes = LogMonitor.UltimasLinhas(6);
            MessageBox.Show(FindForm(),
                "O serviço do monitor foi instalado, mas não conseguiu iniciar." +
                (detalhes.Length > 0 ? "\n\nÚltimos registros:\n" + detalhes : "") +
                "\n\nRegistro completo em C:\\ProgramData\\L2Utilitarios\\monitor.log",
                "Monitor de rede", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Atualizar();
    }
}

using System.Diagnostics;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Abas;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views;

// ---------------------------------------------------------------
//  Janela principal: painel de utilitários (visual Windows 11).
//  Cada ferramenta vive na sua própria aba; esta classe só cuida da
//  janela em si (título, bandeja do sistema, atualização automática).
// ---------------------------------------------------------------
public class FormPrincipal : Form
{
    const string NomeApp = "L2 Utilitários";

    readonly TabControl abas = new() { Dock = DockStyle.Fill };
    readonly AbaImpressora abaImpressora;
    readonly AbaRede abaRede = new();
    readonly NotifyIcon trayIcon = new() { Text = NomeApp };
    readonly Dictionary<TabPage, IAbaUtilitario> _abasPorPagina = new();
    bool _saindoDeVerdade;

    public FormPrincipal(string arquivoConfig, HistoricoLog log, AgendadorTarefa agendador)
    {
        abaImpressora = new AbaImpressora(arquivoConfig, log, agendador);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"{NomeApp} - v{versao?.Major}.{versao?.Minor}.{versao?.Build}";
        Font = Tema.Corpo;
        ForeColor = Tema.Texto;
        BackColor = Tema.Fundo;
        Icon = Tema.IconePrograma ?? Icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 760);

        AdicionarAba("Impressora", abaImpressora);
        AdicionarAba("Rede", abaRede);
        Controls.Add(abas);

        ConfigurarBandeja();
        abaImpressora.StatusMudou += ativo =>
            trayIcon.Text = ativo ? $"{NomeApp} — impressão ativa" : $"{NomeApp} — impressão inativa";

        ResumeLayout(false);
        PerformLayout();

        abas.SelectedIndexChanged += (s, e) => AjustarAltura();
        Load += (s, e) => AjustarAltura();
        Load += async (s, e) => await VerificarAtualizacaoAsync();
        Load += (s, e) => abaImpressora.VerificarFalhasRecentes();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarBarraTitulo(this);
    }

    void AdicionarAba(string titulo, Control controle)
    {
        var pagina = new TabPage(titulo) { BackColor = Tema.Fundo, Padding = new Padding(0) };
        pagina.Controls.Add(controle);
        abas.TabPages.Add(pagina);

        if (controle is IAbaUtilitario aba)
        {
            _abasPorPagina[pagina] = aba;
            aba.AlturaMudou += (s, e) => AjustarAltura();
        }
    }

    // A altura da janela acompanha o conteúdo da aba selecionada
    void AjustarAltura()
    {
        if (!abas.IsHandleCreated || abas.SelectedTab is null) return;
        if (!_abasPorPagina.TryGetValue(abas.SelectedTab, out var aba)) return;

        int cabecalhoAbas = abas.Height - abas.DisplayRectangle.Height;
        int altura = aba.AlturaPreferida + cabecalhoAbas;
        if (ClientSize.Height != altura) ClientSize = new Size(ClientSize.Width, altura);
    }

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

    // ---------------- Atualização automática ----------------

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
}

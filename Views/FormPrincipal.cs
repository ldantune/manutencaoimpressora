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

    const int LarguraMenu = 200;
    const int LarguraConteudo = 520;

    // Menu lateral (um item por utilitário) e a área onde a ferramenta aparece
    readonly Panel menuLateral = new() { Dock = DockStyle.Left, Width = LarguraMenu, Padding = new Padding(8, 8, 4, 8) };
    readonly Panel areaConteudo = new() { Dock = DockStyle.Fill };
    readonly List<(BotaoNavegacao botao, Control aba)> _itens = new();
    Control? _abaAtual;
    readonly AbaImpressora abaImpressora;
    readonly AbaRede abaRede;
    readonly NotifyIcon trayIcon = new() { Text = NomeApp };
    bool _saindoDeVerdade;

    public FormPrincipal(string arquivoConfig, HistoricoLog log, AgendadorTarefa agendador, HistoricoRede historicoRede)
    {
        abaImpressora = new AbaImpressora(arquivoConfig, log, agendador);
        abaRede = new AbaRede(historicoRede);

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
        ClientSize = new Size(LarguraMenu + LarguraConteudo, 760);

        AdicionarAba("Rede", Icones.Rede, abaRede);
        AdicionarAba("Impressora", Icones.Imprimir, abaImpressora);
        Controls.Add(menuLateral);
        Controls.Add(areaConteudo);
        areaConteudo.BringToFront();   // o Fill precisa ser encaixado por último
        _itens[0].botao.Checked = true;

        ConfigurarBandeja();
        abaImpressora.StatusMudou += ativo =>
            trayIcon.Text = ativo ? $"{NomeApp} — impressão ativa" : $"{NomeApp} — impressão inativa";

        ResumeLayout(false);
        PerformLayout();

        Load += (s, e) => AjustarAltura();
        Load += async (s, e) => await VerificarAtualizacaoAsync();
        Load += (s, e) => abaImpressora.VerificarFalhasRecentes();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarBarraTitulo(this);
    }

    void AdicionarAba(string titulo, string icone, Control aba)
    {
        var botao = new BotaoNavegacao { Text = titulo, Icone = icone, AccessibleName = titulo };
        menuLateral.Controls.Add(botao);
        botao.BringToFront();          // mantém os itens na ordem em que foram adicionados

        aba.Dock = DockStyle.Fill;
        aba.Visible = false;
        areaConteudo.Controls.Add(aba);

        botao.CheckedChanged += (s, e) => { if (botao.Checked) MostrarAba(aba); };
        if (aba is IAbaUtilitario utilitario)
            utilitario.AlturaMudou += (s, e) => { if (aba == _abaAtual) AjustarAltura(); };

        _itens.Add((botao, aba));
    }

    void MostrarAba(Control aba)
    {
        if (_abaAtual == aba) return;
        areaConteudo.SuspendLayout();
        if (_abaAtual != null) _abaAtual.Visible = false;
        aba.Visible = true;
        _abaAtual = aba;
        areaConteudo.ResumeLayout(true);
        AjustarAltura();
    }

    // A altura da janela acompanha o conteúdo do utilitário selecionado
    void AjustarAltura()
    {
        if (!IsHandleCreated || _abaAtual is not IAbaUtilitario aba) return;
        int altura = aba.AlturaPreferida;
        if (altura > 0 && ClientSize.Height != altura) ClientSize = new Size(ClientSize.Width, altura);
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

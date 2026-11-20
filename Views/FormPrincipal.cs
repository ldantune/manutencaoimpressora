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
    const int LarguraMaximaConteudo = 1200;   // acima disso o conteúdo fica centralizado

    // Menu lateral (um item por utilitário) e a área onde a ferramenta aparece
    readonly PainelSuave menuLateral = new() { Dock = DockStyle.Left, Width = LarguraMenu, Padding = new Padding(8, 8, 4, 8) };
    readonly PainelSuave areaConteudo = new() { Dock = DockStyle.Fill, AutoScroll = true };
    FormWindowState _estadoVisivel = FormWindowState.Maximized;
    bool _posicionando;
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
        // Abre maximizada; ao restaurar, fica com um tamanho confortável e pode ser redimensionada
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LarguraMenu + 980, 800);
        MinimumSize = new Size(LarguraMenu + 560, 560);
        WindowState = FormWindowState.Maximized;

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

        areaConteudo.Resize += (s, e) => PosicionarAba();
        Load += (s, e) => RestaurarEstadoJanela();
        Load += (s, e) => PosicionarAba();
        Shown += (s, e) => Invalidate(true);
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

        aba.Dock = DockStyle.None;     // posicionada à mão: centralizada e com rolagem
        aba.Visible = false;
        areaConteudo.Controls.Add(aba);

        botao.CheckedChanged += (s, e) => { if (botao.Checked) MostrarAba(aba); };
        if (aba is IAbaUtilitario utilitario)
            utilitario.AlturaMudou += (s, e) => { if (aba == _abaAtual) PosicionarAba(); };

        _itens.Add((botao, aba));
    }

    void MostrarAba(Control aba)
    {
        if (_abaAtual == aba) return;
        areaConteudo.SuspendLayout();
        if (_abaAtual != null) _abaAtual.Visible = false;
        aba.Visible = true;
        _abaAtual = aba;
        areaConteudo.AutoScrollPosition = Point.Empty;
        areaConteudo.ResumeLayout(true);
        PosicionarAba();
    }

    // Layout adaptativo: o utilitário ocupa a largura disponível (até um limite,
    // centralizado) e a área rola quando ele é mais alto que a janela
    void PosicionarAba()
    {
        if (_posicionando || _abaAtual is null || !IsHandleCreated) return;
        _posicionando = true;
        try
        {
            float escala = DeviceDpi / 96f;
            int disponivel = areaConteudo.ClientSize.Width;
            int largura = Math.Max(1, Math.Min(disponivel, (int)(LarguraMaximaConteudo * escala)));
            int x = Math.Max(0, (disponivel - largura) / 2);

            // Mudar a largura reorganiza o conteúdo; só depois a altura preferida é lida
            if (_abaAtual.Width != largura) _abaAtual.Width = largura;
            int altura = _abaAtual is IAbaUtilitario aba ? aba.AlturaPreferida : areaConteudo.ClientSize.Height;

            var rolagem = areaConteudo.AutoScrollPosition;
            _abaAtual.Bounds = new Rectangle(x + rolagem.X, rolagem.Y, largura, Math.Max(1, altura));
        }
        finally
        {
            _posicionando = false;
        }
    }

    // ---------------- Tamanho e posição da janela ----------------

    // Reabre do jeito que a pessoa deixou (na primeira vez, maximizada)
    void RestaurarEstadoJanela()
    {
        var estado = EstadoJanela.Carregar();
        if (estado is null) return;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        Bounds = estado.Retangulo;
        WindowState = estado.Maximizada ? FormWindowState.Maximized : FormWindowState.Normal;
        _estadoVisivel = WindowState;
    }

    void SalvarEstadoJanela()
    {
        var area = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        bool maximizada = WindowState == FormWindowState.Maximized ||
                          (WindowState == FormWindowState.Minimized && _estadoVisivel == FormWindowState.Maximized);
        new EstadoJanela(area.X, area.Y, area.Width, area.Height, maximizada).Salvar();
    }

    // ---------------- Bandeja do sistema ----------------

    void ConfigurarBandeja()
    {
        trayIcon.Icon = Tema.IconeBandeja ?? Tema.IconePrograma ?? Icon;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (s, e) => MostrarJanela());
        menu.Items.Add("Sair", null, (s, e) => { _saindoDeVerdade = true; Close(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (s, e) => MostrarJanela();
        trayIcon.Visible = true;
    }

    // Outra execução do programa pediu para mostrar esta janela (instância única)
    public void EscutarPedidosParaMostrar(EventWaitHandle pedido)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                pedido.WaitOne();
                if (IsDisposed) return;
                try { BeginInvoke(MostrarJanela); }
                catch (InvalidOperationException) { return; }   // janela já fechada
            }
        })
        { IsBackground = true, Name = "Pedidos para mostrar a janela" };
        thread.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Tira o ícone da bandeja na hora (senão ele fica "fantasma" até passar o mouse)
        trayIcon.Visible = false;
        trayIcon.Dispose();
        base.OnFormClosed(e);
    }

    void MostrarJanela()
    {
        Show();
        WindowState = _estadoVisivel;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Visible) SalvarEstadoJanela();
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
        else
        {
            _estadoVisivel = WindowState;
            Invalidate(true);   // repinta tudo ao maximizar/restaurar (sem restos da borda antiga)
        }
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

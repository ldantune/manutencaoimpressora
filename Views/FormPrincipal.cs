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
    readonly AbaConexao abaConexao;
    readonly AbaDispositivos abaDispositivos;
    readonly AbaSaude abaSaude = new();
    readonly AbaInicializacao abaInicializacao = new();
    readonly AbaEnergia abaEnergia = new();
    readonly AbaLimpeza abaLimpeza = new();
    readonly NotifyIcon trayIcon = new() { Text = NomeApp };
    bool _saindoDeVerdade;

    public FormPrincipal(string arquivoConfig, HistoricoLog log, AgendadorTarefa agendador, HistoricoRede historicoRede)
    {
        abaImpressora = new AbaImpressora(arquivoConfig, log, agendador);
        abaRede = new AbaRede(historicoRede);
        abaConexao = new AbaConexao();
        abaDispositivos = new AbaDispositivos();

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

        AdicionarGrupo("Internet");
        AdicionarAba("Rede", Icones.Rede, abaRede);
        AdicionarAba("Conexão", Icones.Conexao, abaConexao);
        AdicionarAba("Dispositivos", Icones.Dispositivos, abaDispositivos);
        AdicionarGrupo("Computador");
        AdicionarAba("Saúde do PC", Icones.Saude, abaSaude);
        AdicionarAba("Inicialização", Icones.Inicializacao, abaInicializacao);
        AdicionarAba("Energia", Icones.Energia, abaEnergia);
        AdicionarAba("Limpeza", Icones.Limpeza, abaLimpeza);
        AdicionarGrupo("Impressão");
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

    // Título pequeno separando os grupos do menu lateral
    void AdicionarGrupo(string titulo)
    {
        var rotulo = new Label
        {
            Text = titulo, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Dock = DockStyle.Top,
            AutoSize = false, Height = 32, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(14, 0, 0, 4)
        };
        menuLateral.Controls.Add(rotulo);
        rotulo.BringToFront();   // mesma ordem dos itens
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
        // Avisos na bandeja (internet caiu/voltou, aparelho novo na rede)
        if (aba is INotificador notificador)
            notificador.Notificar += (titulo, texto, icone) => trayIcon.ShowBalloonTip(6000, titulo, texto, icone);

        if (aba is IAbaUtilitario utilitario)
            utilitario.AlturaMudou += (s, e) => { if (aba == _abaAtual) PosicionarAba(); };

        _itens.Add((botao, aba));
    }

    void MostrarAba(Control aba)
    {
        if (_abaAtual == aba) return;
        // Congela o desenho da área durante a troca e mostra a tela nova já pronta, de uma vez
        bool congelou = areaConteudo.IsHandleCreated;
        if (congelou) SendMessage(areaConteudo.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            areaConteudo.SuspendLayout();
            if (_abaAtual != null) _abaAtual.Visible = false;
            aba.Visible = true;
            _abaAtual = aba;
            areaConteudo.AutoScrollPosition = Point.Empty;
            areaConteudo.ResumeLayout(true);
            PosicionarAba();
        }
        finally
        {
            if (congelou)
            {
                SendMessage(areaConteudo.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                areaConteudo.Invalidate(true);
                areaConteudo.Update();
            }
        }
    }

    // Na bandeja a janela não é desenhada: depois de alguns segundos escondida, recolhe a memória
    // que as telas usaram e devolve ao Windows o que não está em uso
    async void LiberarMemoriaAoEsconder()
    {
        await Task.Delay(5_000);
        if (Visible) return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: true);
        try { SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr processo, nint minimo, nint maximo);

    const int WM_SETREDRAW = 0x000B;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr janela, int mensagem, IntPtr wParam, IntPtr lParam);

    // Pinta a janela inteira num buffer antes de mostrar (sem os "pedaços brancos")
    protected override CreateParams CreateParams
    {
        get
        {
            var parametros = base.CreateParams;
            parametros.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
            return parametros;
        }
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
            LiberarMemoriaAoEsconder();
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
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            LiberarMemoriaAoEsconder();
        }
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

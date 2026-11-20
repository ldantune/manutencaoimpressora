using System.Globalization;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Saúde do PC": processador, memória, discos, tempo ligado, o que
//  mais pesa agora e dados do computador. Avisa na bandeja se o disco
//  do Windows ficar quase cheio.
// ---------------------------------------------------------------
public class AbaSaude : AbaBase, INotificador
{
    const int SegundosEntreLeituras = 2;
    const double LimiteDisco = 0.90, LimiteMemoria = 0.90;
    static readonly Color CorAlerta = Color.FromArgb(157, 93, 0);

    readonly InfoSistema _info = new();
    readonly AmostradorProcessos _processos = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = SegundosEntreLeituras * 1000 };
    readonly System.Windows.Forms.Timer _timerDisco = new() { Interval = 10 * 60 * 1000 };

    readonly BarraStatus barra = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, 12) };
    readonly CartaoValor cartaoCpu = new(), cartaoMemoria = new(), cartaoGpu = new(), cartaoDisco = new(), cartaoLigado = new();
    readonly InfoPlacaVideo _placa = new();
    readonly GraficoLinha graficoGpu = new() { Height = 100, Dock = DockStyle.Fill, MaximoAmostras = 300, CorLinha = Color.FromArgb(118, 185, 0), Margin = new Padding(0) };
    readonly Label lblGpu = new() { AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right };
    PlacaVideo? _ultimaPlaca;
    bool _lendoPlaca;
    bool _lendoProcessos;
    static readonly string NomeProcessador = InfoSistema.Processador();
    readonly GraficoLinha graficoCpu = new() { Height = 100, Dock = DockStyle.Fill, MaximoAmostras = 300, CorLinha = Tema.Destaque, Margin = new Padding(0) };
    readonly GraficoLinha graficoMemoria = new() { Height = 100, Dock = DockStyle.Fill, MaximoAmostras = 300, CorLinha = Color.FromArgb(135, 100, 184), Margin = new Padding(0) };
    readonly Label lblCpu = new() { AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right };
    readonly Label lblMemoria = new() { AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right };
    readonly ListaDetalhes listaDiscos = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    readonly ListaDetalhes listaProcessos = new() { Dock = DockStyle.Fill, Margin = new Padding(0), TextoVazio = "Medindo…" };
    readonly ListaDetalhes listaSistema = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };

    double _picoCpu;
    bool _avisouDisco;
    string? _ultimaAtualizacao;

    public event Action<string, string, ToolTipIcon>? Notificar;

    public AbaSaude() : base("Saúde do PC", "Uso do processador, memória e discos, e o que está pesando no computador agora.")
    {
        Adicionar(barra);

        var cartoes = new GradeAdaptativa { LarguraMinimaColuna = 190, MaximoColunas = 5, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        foreach (var c in new[] { cartaoCpu, cartaoMemoria, cartaoGpu, cartaoDisco, cartaoLigado }) cartoes.Adicionar(c);
        Adicionar(cartoes);

        var graficos = new GradeAdaptativa { LarguraMinimaColuna = 300, MaximoColunas = 3, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        graficos.Adicionar(CardGrafico("Processador", lblCpu, graficoCpu));
        graficos.Adicionar(CardGrafico("Memória", lblMemoria, graficoMemoria));
        graficos.Adicionar(CardGrafico("Placa de vídeo", lblGpu, graficoGpu));
        Adicionar(graficos);

        var listas = new GradeAdaptativa { LarguraMinimaColuna = 360, MaximoColunas = 2, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        var cardDiscos = new CardPanel();
        cardDiscos.AdicionarLinha(TituloCard("Discos"));
        cardDiscos.AdicionarLinha(listaDiscos);
        listas.Adicionar(cardDiscos);

        var cardProcessos = new CardPanel();
        var cabecalho = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0) };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.Controls.Add(TituloCard("O que mais pesa agora"), 0, 0);
        var linkGerenciador = new LinkLabel { Text = "Abrir Gerenciador de Tarefas", AutoSize = true, Anchor = AnchorStyles.Right, LinkColor = Tema.Destaque, Margin = new Padding(0, 0, 0, 8) };
        linkGerenciador.LinkClicked += (s, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
        };
        cabecalho.Controls.Add(linkGerenciador, 1, 0);
        cardProcessos.AdicionarLinha(cabecalho);
        cardProcessos.AdicionarLinha(listaProcessos);
        listas.Adicionar(cardProcessos);
        Adicionar(listas);

        var cardSistema = new CardPanel { Margin = new Padding(0) };
        cardSistema.AdicionarLinha(TituloCard("Sobre este computador"));
        cardSistema.AdicionarLinha(listaSistema);
        Adicionar(cardSistema);

        _info.UsoProcessador();   // primeira leitura só marca o ponto de partida
        _timer.Tick += (s, e) => Ler();
        // Com a tela escondida não mede nada: só confere o disco a cada 10 minutos (aviso na bandeja)
        _timerDisco.Tick += (s, e) => VerificarDiscoCheio();
        _timerDisco.Start();
        VisibleChanged += (s, e) =>
        {
            if (Visible) { _timer.Start(); Ler(); }
            else _timer.Stop();
        };
        _ = CarregarUltimaAtualizacaoAsync();
        AtualizarSistema();
    }

    static CardPanel CardGrafico(string titulo, Label legenda, GraficoLinha grafico)
    {
        var card = new CardPanel();
        var cabecalho = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0) };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalho.Controls.Add(TituloCard(titulo), 0, 0);
        cabecalho.Controls.Add(legenda, 1, 0);
        card.AdicionarLinha(cabecalho);
        card.AdicionarLinha(grafico);
        return card;
    }

    static string Gb(double bytes) => (bytes / FormatoBytes.GB).ToString(bytes >= 100 * FormatoBytes.GB ? "0" : "0.0", CultureInfo.CurrentCulture);

    // A cada 2 s: os gráficos sempre (para terem histórico); o resto só com a tela aberta
    void Ler()
    {
        double cpu = _info.UsoProcessador();
        var (total, livre) = InfoSistema.Memoria();
        double usoMemoria = total == 0 ? 0 : 1 - (double)livre / total;
        _ = LerPlacaAsync();
        graficoCpu.AdicionarAmostra(cpu);
        graficoMemoria.AdicionarAmostra(usoMemoria * 100);
        _picoCpu = Math.Max(_picoCpu * 0.995, cpu);

        var discoWindows = InfoSistema.Discos().FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase));
        double usoDisco = discoWindows is null || discoWindows.TotalSize == 0 ? 0 : 1 - (double)discoWindows.AvailableFreeSpace / discoWindows.TotalSize;
        VerificarDiscoCheio();
        if (!Visible) return;

        // Situação
        if (usoDisco >= LimiteDisco && discoWindows != null)
            barra.Definir(Severidade.Aviso, $"O disco {discoWindows.Name.TrimEnd('\\')} está quase cheio ({usoDisco:P0})",
                "Com pouco espaço o Windows fica lento e as atualizações podem falhar. Veja o que dá para apagar em Limpeza.");
        else if (_ultimaPlaca?.Temperatura is double temp && temp >= 85)
            barra.Definir(Severidade.Aviso, $"A placa de vídeo está quente ({temp:0} °C)",
                "Acima de 85 °C ela reduz o desempenho para se proteger. Vale limpar a poeira e conferir as ventoinhas.");
        else if (usoMemoria >= LimiteMemoria)
            barra.Definir(Severidade.Aviso, $"A memória está quase toda em uso ({usoMemoria:P0})",
                "Fechar programas que não estão sendo usados (veja \"O que mais pesa agora\") deixa o computador mais rápido.");
        else
            barra.Definir(Severidade.Sucesso, "Tudo certo com o computador",
                "Nada fora do normal agora. O programa avisa na bandeja se o disco ficar quase cheio.");

        cartaoCpu.Definir("Processador", "agora", $"{cpu:0}%", NomeProcessador.Split(" · ")[0]);
        cartaoMemoria.Definir("Memória", "agora", $"{Gb(total - livre)} de {Gb(total)} GB",
            $"{usoMemoria:P0} em uso · {Gb(livre)} GB livres", usoMemoria >= LimiteMemoria ? CorAlerta : null);
        if (discoWindows != null)
            cartaoDisco.Definir($"Disco {discoWindows.Name.TrimEnd('\\')}", "espaço usado", $"{usoDisco:P0}",
                $"{Gb(discoWindows.AvailableFreeSpace)} GB livres de {Gb(discoWindows.TotalSize)} GB", usoDisco >= LimiteDisco ? CorAlerta : null);
        var ligado = InfoSistema.TempoLigado();
        cartaoLigado.Definir("Ligado há", "sem reiniciar", FormatoTempo.Duracao(ligado),
            ligado.TotalDays >= 7 ? "Reiniciar ajuda a liberar memória e concluir atualizações" : $"desde {DateTime.Now - ligado:ddd, dd/MM 'às' HH:mm}",
            ligado.TotalDays >= 7 ? CorAlerta : null);

        lblCpu.Text = $"últimos 10 min · pico {_picoCpu:0}%";
        lblMemoria.Text = $"últimos 10 min · {usoMemoria:P0} agora";
        MostrarPlaca();

        listaDiscos.Definir(InfoSistema.Discos().Select(d =>
        {
            double uso = d.TotalSize == 0 ? 0 : 1 - (double)d.AvailableFreeSpace / d.TotalSize;
            string nome = string.IsNullOrWhiteSpace(d.VolumeLabel) ? $"Disco local ({d.Name.TrimEnd('\\')})" : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
            return new ItemDetalhe("", nome, $"{Gb(d.AvailableFreeSpace)} GB livres de {Gb(d.TotalSize)} GB · {d.DriveFormat}", $"{uso:P0} usado",
                Selo: uso >= LimiteDisco ? "Quase cheio" : null, CorSelo: CorAlerta, CorIcone: uso >= LimiteDisco ? CorAlerta : null);
        }).ToList());

        _ = LerProcessosAsync();
    }

    void VerificarDiscoCheio()
    {
        if (_avisouDisco) return;
        var disco = InfoSistema.Discos().FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase));
        if (disco is null || disco.TotalSize == 0) return;
        double uso = 1 - (double)disco.AvailableFreeSpace / disco.TotalSize;
        if (uso < 0.95) return;
        _avisouDisco = true;
        Notificar?.Invoke("Disco quase cheio", $"O disco {disco.Name.TrimEnd('\\')} está com {uso:P0} de uso. A Limpeza pode ajudar a liberar espaço.", ToolTipIcon.Warning);
    }

    // Percorrer todos os processos leva alguns milissegundos: fora da janela
    async Task LerProcessosAsync()
    {
        if (_lendoProcessos) return;
        _lendoProcessos = true;
        try
        {
            var lista = await Task.Run(() => _processos.Ler(6));
            listaProcessos.Definir(lista.Select(p => new ItemDetalhe("", p.Nome,
                $"Memória: {FormatoBytes.Formatar(p.Memoria)}", $"{p.Cpu:0}% do processador")).ToList());
        }
        finally { _lendoProcessos = false; }
    }

    // nvidia-smi / contadores rodam fora da tela para não travar a janela
    async Task LerPlacaAsync()
    {
        if (_lendoPlaca) return;
        _lendoPlaca = true;
        try
        {
            var placa = await Task.Run(_placa.Ler);
            bool primeira = _ultimaPlaca is null && placa is not null;
            _ultimaPlaca = placa;
            graficoGpu.AdicionarAmostra(placa?.Uso ?? 0);
            if (primeira) AtualizarSistema();
        }
        finally { _lendoPlaca = false; }
    }

    void MostrarPlaca()
    {
        var p = _ultimaPlaca;
        if (p is null)
        {
            cartaoGpu.Definir("Placa de vídeo", "", "—", "não foi possível ler");
            lblGpu.Text = "";
            return;
        }
        var detalhes = new List<string>();
        if (p.Temperatura is double t) detalhes.Add($"{t:0} °C");
        if (p.MemoriaUsadaMb is double usada)
            detalhes.Add(p.MemoriaTotalMb is double total ? $"{usada / 1024:0.0} de {total / 1024:0} GB" : $"{usada / 1024:0.0} GB em uso");
        if (p.Ventoinha is double v) detalhes.Add($"ventoinha {v:0}%");
        if (p.Watts is double w) detalhes.Add($"{w:0} W");
        bool quente = p.Temperatura >= 85;
        cartaoGpu.Definir("Placa de vídeo", NomeCurto(p.Nome), p.Uso is double uso ? $"{uso:0}%" : "—",
            detalhes.Count > 0 ? string.Join(" · ", detalhes) : "uso não disponível", quente ? CorAlerta : null);
        lblGpu.Text = p.Temperatura is double temp ? $"últimos 10 min · {temp:0} °C agora" : "últimos 10 min";
    }

    // "NVIDIA GeForce GTX 1660 SUPER" -> "GTX 1660 SUPER"
    static string NomeCurto(string nome) =>
        nome.Replace("NVIDIA", "").Replace("GeForce", "").Replace("AMD", "").Replace("Radeon(TM)", "Radeon").Trim();

    async Task CarregarUltimaAtualizacaoAsync()
    {
        var data = await Task.Run(InfoSistema.UltimaAtualizacaoWindows);
        _ultimaAtualizacao = data is DateTime d ? $"{d:dd/MM/yyyy} (há {(DateTime.Today - d.Date).TotalDays:0} dias)" : "não foi possível ler";
        AtualizarSistema();
    }

    void AtualizarSistema()
    {
        var (total, _) = InfoSistema.Memoria();
        var linhas = new (string Rotulo, string Valor)[]
        {
            ("Windows", InfoSistema.VersaoWindows()),
            ("Computador", Environment.MachineName),
            ("Processador", NomeProcessador),
            ("Memória instalada", $"{Gb(total)} GB"),
            ("Placa de vídeo", _ultimaPlaca is null ? "procurando…" :
                _ultimaPlaca.Nome + (_ultimaPlaca.MemoriaTotalMb is double mem ? $" · {mem / 1024:0} GB" : "") +
                (_ultimaPlaca.Driver is string drv ? $" · driver {drv}" : "")),
            ("Ligado desde", $"{DateTime.Now - InfoSistema.TempoLigado():dddd, dd/MM 'às' HH:mm}"),
            ("Última atualização do Windows", _ultimaAtualizacao ?? "procurando…")
        };
        listaSistema.Definir(linhas.Select(l => new ItemDetalhe("", l.Valor, l.Rotulo, "")).ToList());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _timerDisco.Dispose();
            _placa.Dispose();
        }
        base.Dispose(disposing);
    }
}

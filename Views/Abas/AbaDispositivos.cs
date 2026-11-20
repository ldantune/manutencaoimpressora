using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Dispositivos": aparelhos conectados na rede local, os que já
//  passaram por ela e aviso (na bandeja) quando aparece um novo.
//  A rede é verificada sozinha a cada 30 minutos.
// ---------------------------------------------------------------
public class AbaDispositivos : AbaBase, INotificador
{
    const string GlifoRoteador = "", GlifoComputador = "", GlifoCelular = "", GlifoAparelho = "";

    readonly BancoLocal _banco = new(BancoLocal.CaminhoPadrao);
    readonly Label lblStatus = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 12, 0) };
    readonly BotaoFluent btnProcurar = new() { Text = "Procurar agora", Icone = Icones.Atualizar, Anchor = AnchorStyles.Right };
    readonly BarraComparacao barraProgresso = new() { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    readonly CartaoValor cartaoAgora = new(), cartaoNovos = new(), cartaoRede = new();
    readonly BarraStatus barraNovo = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    readonly Label lblAgora = AbaBase.TituloCard("Conectados agora");
    readonly Label lblAntes = AbaBase.TituloCard("Vistos antes");
    readonly ListaDetalhes listaAgora = new() { Dock = DockStyle.Fill, Margin = new Padding(0), MostrarEditar = true, TextoVazio = "Clique em \"Procurar agora\" para ver os aparelhos da rede." };
    readonly ListaDetalhes listaAntes = new() { Dock = DockStyle.Fill, Margin = new Padding(0), MostrarEditar = true, TextoVazio = "Nenhum outro aparelho visto até agora." };
    readonly System.Windows.Forms.Timer _timerAutomatico = new() { Interval = 30 * 60 * 1000 };

    List<DispositivoEncontrado> _encontrados = new();
    readonly HashSet<string> _novosDestaSessao = new();
    string _rede = "";
    bool _procurando, _jaProcurou;

    public event Action<string, string, ToolTipIcon>? Notificar;

    public AbaDispositivos()
        : base("Dispositivos", "Quem está conectado na sua rede, com aviso quando aparece um aparelho novo.")
    {
        var acoes = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12)
        };
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acoes.Controls.Add(new Label(), 0, 0);
        acoes.Controls.Add(lblStatus, 1, 0);
        acoes.Controls.Add(btnProcurar, 2, 0);
        Adicionar(acoes);
        Adicionar(barraProgresso);

        var cartoes = new GradeAdaptativa { LarguraMinimaColuna = 220, MaximoColunas = 3, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        foreach (var c in new[] { cartaoAgora, cartaoNovos, cartaoRede }) cartoes.Adicionar(c);
        Adicionar(cartoes);
        Adicionar(barraNovo);

        var cardAgora = new CardPanel();
        cardAgora.AdicionarLinha(lblAgora);
        cardAgora.AdicionarLinha(listaAgora);
        Adicionar(cardAgora);

        var cardAntes = new CardPanel();
        cardAntes.AdicionarLinha(lblAntes);
        cardAntes.AdicionarLinha(listaAntes);
        Adicionar(cardAntes);

        Adicionar(new Label
        {
            Text = "A rede é verificada sozinha a cada 30 minutos enquanto o programa estiver aberto. Celulares costumam usar um " +
                   "endereço \"privado\" que muda de tempos em tempos, por isso às vezes aparecem como novos.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 4)
        });

        btnProcurar.Click += async (s, e) => await ProcurarAsync(automatico: false);
        listaAgora.EditarClicado += Renomear;
        listaAntes.EditarClicado += Renomear;
        _timerAutomatico.Tick += async (s, e) => await ProcurarAsync(automatico: true);
        _timerAutomatico.Start();
        VisibleChanged += async (s, e) =>
        {
            if (!Visible) return;
            if (!_jaProcurou) await ProcurarAsync(automatico: false);
            else Mostrar();
        };
        Mostrar();
    }

    async Task ProcurarAsync(bool automatico)
    {
        if (_procurando) return;
        _procurando = true;
        _jaProcurou = true;
        btnProcurar.Enabled = false;
        barraProgresso.Visible = !automatico || Visible;
        barraProgresso.Definir(0, null);
        lblStatus.Text = "Procurando aparelhos na rede…";
        try
        {
            var progresso = new Progress<int>(p =>
            {
                barraProgresso.Definir(p / 100f, null);
                lblStatus.Text = $"Procurando aparelhos na rede… {p}%";
            });
            var (encontrados, rede) = await ScannerRede.ProcurarAsync(progresso, CancellationToken.None);
            await FabricantesMac.CarregarAsync();

            bool primeiraVez = _banco.QuantidadeDispositivos() == 0;
            var novos = _banco.RegistrarVistos(
                encontrados.Select(d => (d.Mac, d.Ip.ToString(), d.Nome, FabricantesMac.Buscar(d.Mac))), DateTime.Now);
            _encontrados = encontrados;
            _rede = rede;

            // Na primeira busca tudo é "novo"; só avisa a partir da segunda
            if (!primeiraVez && novos.Count > 0)
            {
                _novosDestaSessao.UnionWith(novos);
                var nomes = encontrados.Where(d => novos.Contains(d.Mac)).Select(d => NomeAmigavel(d, null)).ToList();
                Notificar?.Invoke(novos.Count == 1 ? "Aparelho novo na rede" : $"{novos.Count} aparelhos novos na rede",
                    string.Join(", ", nomes.Take(3)) + (nomes.Count > 3 ? "…" : ""), ToolTipIcon.Warning);
            }
            lblStatus.Text = $"Atualizado às {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Não foi possível procurar: " + ex.GetBaseException().Message;
        }
        finally
        {
            _procurando = false;
            btnProcurar.Enabled = true;
            barraProgresso.Visible = false;
            Mostrar();
        }
    }

    static string Glifo(DispositivoEncontrado? agora, string mac)
    {
        if (agora?.Roteador == true) return GlifoRoteador;
        if (agora?.EsteComputador == true) return GlifoComputador;
        if (FabricantesMac.EnderecoPrivado(mac)) return GlifoCelular;
        return GlifoAparelho;
    }

    static string NomeAmigavel(DispositivoEncontrado? agora, DispositivoConhecido? conhecido)
    {
        if (!string.IsNullOrWhiteSpace(conhecido?.Apelido)) return conhecido!.Apelido!;
        if (agora?.EsteComputador == true) return $"Este computador ({Environment.MachineName})";
        if (agora?.Roteador == true) return "Roteador";
        string? nome = agora?.Nome ?? conhecido?.Nome;
        if (!string.IsNullOrWhiteSpace(nome)) return nome!;
        string mac = agora?.Mac ?? conhecido?.Mac ?? "";
        if (FabricantesMac.EnderecoPrivado(mac)) return "Celular ou tablet (endereço privado)";
        string? fabricante = conhecido?.Fabricante ?? FabricantesMac.Buscar(mac);
        return fabricante is null ? "Aparelho desconhecido" : $"Aparelho {fabricante}";
    }

    void Mostrar()
    {
        var conhecidos = _banco.ListarDispositivos().ToDictionary(d => d.Mac);
        var hoje = DateTime.Today;

        var itensAgora = _encontrados.Select(d =>
        {
            conhecidos.TryGetValue(d.Mac, out var c);
            bool novo = _novosDestaSessao.Contains(d.Mac);
            string fabricante = c?.Fabricante ?? FabricantesMac.Buscar(d.Mac) ?? (FabricantesMac.EnderecoPrivado(d.Mac) ? "endereço privado" : "fabricante desconhecido");
            string desde = c is null ? "" : c.PrimeiraVez.Date == hoje ? $"desde hoje às {c.PrimeiraVez:HH:mm}" : $"desde {c.PrimeiraVez:dd/MM}";
            return new ItemDetalhe(Glifo(d, d.Mac), NomeAmigavel(d, c), $"{d.Ip} · {d.Mac} · {fabricante}", desde,
                Selo: novo ? "Novo" : null,
                CorIcone: novo ? Tema.Destaque : null, FundoIcone: novo ? Color.FromArgb(234, 243, 252) : null,
                Chave: d.Mac);
        }).ToList();

        var macsAgora = _encontrados.Select(d => d.Mac).ToHashSet();
        var itensAntes = conhecidos.Values.Where(c => !macsAgora.Contains(c.Mac)).OrderByDescending(c => c.UltimaVez).Take(30)
            .Select(c => new ItemDetalhe(Glifo(null, c.Mac), NomeAmigavel(null, c),
                string.Join(" · ", new[] { c.UltimoIp, c.Mac, c.Fabricante }.Where(t => !string.IsNullOrWhiteSpace(t))),
                "visto " + FormatoTempo.Relativo(c.UltimaVez), Apagado: true, Chave: c.Mac))
            .ToList();

        listaAgora.Definir(itensAgora);
        listaAntes.Definir(itensAntes);
        lblAgora.Text = _encontrados.Count == 0 ? "Conectados agora" : $"Conectados agora ({_encontrados.Count})";
        lblAntes.Text = itensAntes.Count == 0 ? "Vistos antes" : $"Vistos antes ({itensAntes.Count})";

        int novos7 = conhecidos.Values.Count(c => c.PrimeiraVez >= hoje.AddDays(-7));
        var ultimoNovo = conhecidos.Values.Where(c => c.PrimeiraVez >= hoje.AddDays(-7)).OrderByDescending(c => c.PrimeiraVez).FirstOrDefault();
        cartaoAgora.Definir("Conectados agora", _encontrados.Count == 0 ? "" : "nesta rede",
            _encontrados.Count == 0 ? "—" : _encontrados.Count.ToString(), $"de {conhecidos.Count} aparelhos já vistos");
        cartaoNovos.Definir("Novos", "últimos 7 dias", novos7.ToString(),
            ultimoNovo is null ? "nenhum aparelho novo" : $"o mais recente {FormatoTempo.Quando(ultimoNovo.PrimeiraVez)}",
            novos7 > 0 ? Tema.Destaque : null);
        var roteador = _encontrados.FirstOrDefault(d => d.Roteador);
        cartaoRede.Definir("Sua rede", "", roteador?.Ip.ToString() ?? "—", _rede.Length > 0 ? _rede : "procure para ver os detalhes");

        var novosAgora = _encontrados.Where(d => _novosDestaSessao.Contains(d.Mac)).ToList();
        barraNovo.Visible = novosAgora.Count > 0;
        if (novosAgora.Count > 0)
            barraNovo.Definir(Severidade.Aviso,
                novosAgora.Count == 1 ? "Um aparelho novo entrou na rede" : $"{novosAgora.Count} aparelhos novos entraram na rede",
                "Se você não reconhece, troque a senha do Wi-Fi no roteador. Dá para dar um nome aos aparelhos conhecidos no lápis ao lado de cada um.");
    }

    void Renomear(ItemDetalhe item)
    {
        if (item.Chave is null) return;
        var atual = _banco.ListarDispositivos().FirstOrDefault(d => d.Mac == item.Chave);
        using var dialogo = new DialogoTexto("Renomear aparelho", $"Nome para o aparelho {item.Chave}:",
            atual?.Apelido ?? "", "Deixe em branco para voltar ao nome automático.");
        if (dialogo.ShowDialog(FindForm()) != DialogResult.OK) return;
        _banco.Renomear(item.Chave, dialogo.Texto);
        Mostrar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timerAutomatico.Dispose();
        base.Dispose(disposing);
    }
}

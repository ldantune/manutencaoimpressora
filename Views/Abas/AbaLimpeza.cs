using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Limpeza": mostra quanto cada tipo de arquivo descartável ocupa,
//  a pessoa marca o que quer apagar e o programa limpa. Também lista
//  os maiores arquivos da pasta do usuário (só para mostrar).
// ---------------------------------------------------------------
public class AbaLimpeza : AbaBase
{
    record Categoria(CategoriaLimpeza Tipo, string Glifo, string Nome, string Dica, bool MarcadoPorPadrao);

    static readonly Categoria[] Categorias =
    {
        new(CategoriaLimpeza.Temporarios, "", "Arquivos temporários", "Sobras de instalações e programas (pasta Temp)", true),
        new(CategoriaLimpeza.AtualizacoesWindows, "", "Restos de atualizações do Windows", "Arquivos baixados de atualizações já instaladas", true),
        new(CategoriaLimpeza.Lixeira, "", "Lixeira", "Itens que você já apagou", true),
        new(CategoriaLimpeza.Miniaturas, "", "Miniaturas de imagens", "O Windows recria quando precisar (algumas ficam em uso)", true),
        new(CategoriaLimpeza.RelatoriosErro, "", "Relatórios de erro", "Registros de travamentos antigos", true),
        new(CategoriaLimpeza.CacheNavegadores, "", "Cache dos navegadores", "Chrome e Edge: você continua conectado nos sites; feche o navegador para limpar mais", false),
        new(CategoriaLimpeza.DownloadsAntigos, "", "Downloads com mais de 90 dias", "Vão para a Lixeira (dá para recuperar)", false)
    };

    readonly Label lblTitulo = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Margin = new Padding(0) };
    readonly Label lblTotal = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 26f), Margin = new Padding(0) };
    readonly Label lblDisco = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Margin = new Padding(0, 0, 0, 10) };
    readonly BarraComparacao barraDisco = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
    readonly BotaoFluent btnAnalisar = new() { Text = "Analisar de novo", Icone = Icones.Atualizar, Margin = new Padding(0, 0, 8, 0) };
    readonly BotaoFluent btnLimpar = new() { Text = "Limpar selecionados", Estilo = EstiloBotao.Primario };
    readonly TableLayoutPanel linhas = NovoPainel();
    readonly ListaDetalhes listaGrandes = new() { Dock = DockStyle.Fill, Margin = new Padding(0), TextoVazio = "Procurando os maiores arquivos…" };

    readonly Dictionary<CategoriaLimpeza, CheckBox> _caixas = new();
    readonly Dictionary<CategoriaLimpeza, Label> _tamanhos = new();
    readonly Dictionary<CategoriaLimpeza, long> _bytes = new();
    bool _analisando, _jaAnalisou;

    public AbaLimpeza()
        : base("Limpeza", "Libera espaço apagando arquivos que o Windows e os programas não precisam mais. Você escolhe o que entra.")
    {
        var resumo = new CardPanel();
        var topo = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0) };
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var textos = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        textos.Controls.AddRange(new Control[] { lblTitulo, lblTotal, lblDisco });
        topo.Controls.Add(textos, 0, 0);
        var botoes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(0, 0, 0, 10) };
        botoes.Controls.AddRange(new Control[] { btnAnalisar, btnLimpar });
        topo.Controls.Add(botoes, 1, 0);
        resumo.AdicionarLinha(topo);
        resumo.AdicionarLinha(barraDisco);
        resumo.AdicionarLinha(new Label { Text = "Azul: espaço usado. A marca preta mostra quanto fica usado depois da limpeza.", Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0) });
        Adicionar(resumo);

        var card = new CardPanel();
        card.AdicionarLinha(TituloCard("O que pode ser apagado"));
        foreach (var c in Categorias) AdicionarEm(linhas, CriarLinha(c));
        card.AdicionarLinha(linhas);
        Adicionar(card);

        var cardGrandes = new CardPanel();
        cardGrandes.AdicionarLinha(TituloCard("Maiores arquivos na sua pasta de usuário"));
        cardGrandes.AdicionarLinha(listaGrandes);
        cardGrandes.AdicionarLinha(new Label { Text = "O programa não apaga estes, só mostra onde estão. Clique no ícone de pasta para abrir o local.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        Adicionar(cardGrandes);
        Adicionar(new Label { Text = "Arquivos em uso por algum programa são pulados. Só \"Downloads antigos\" vai para a Lixeira; o resto é apagado de vez.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 4) });

        listaGrandes.MostrarEditar = true;   // o botão da linha aqui abre o local do arquivo
        listaGrandes.GlifoAcao = "\uE838";
        listaGrandes.EditarClicado += item =>
        {
            if (item.Chave is string caminho && File.Exists(caminho))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{caminho}\"");
        };
        btnAnalisar.Click += async (s, e) => await AnalisarAsync();
        btnLimpar.Click += async (s, e) => await LimparAsync();
        VisibleChanged += async (s, e) => { if (Visible && !_jaAnalisou) await AnalisarAsync(); };
        AtualizarResumo();
    }

    Control CriarLinha(Categoria c)
    {
        var linha = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4,
            Margin = new Padding(0), Padding = new Padding(0, 8, 0, 8)
        };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(240, 240, 240));
            e.Graphics.DrawLine(pen, 0, 0, linha.Width, 0);
        };

        var caixa = new CheckBox { Checked = c.MarcadoPorPadrao, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0), AccessibleName = c.Nome };
        caixa.CheckedChanged += (s, e) => AtualizarResumo();
        _caixas[c.Tipo] = caixa;
        linha.Controls.Add(caixa, 0, 0);

        linha.Controls.Add(new Label
        {
            Text = c.Glifo, Font = Tema.IconeGrande, AutoSize = false, Size = new Size(36, 36), TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(243, 243, 243), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0)
        }, 1, 0);

        var textos = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        string nome = c.Nome + (Limpador.PedeAdministrador(c.Tipo) ? "  ·  pede administrador" : "");
        var lblNome = new Label { Text = nome, Font = Tema.CorpoForte, AutoSize = true, Margin = new Padding(0), Cursor = Cursors.Hand };
        lblNome.Click += (s, e) => caixa.Checked = !caixa.Checked;
        textos.Controls.Add(lblNome);
        textos.Controls.Add(new Label { Text = c.Dica, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        linha.Controls.Add(textos, 2, 0);

        var tamanho = new Label { Text = "…", Font = Tema.CorpoForte, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0) };
        _tamanhos[c.Tipo] = tamanho;
        linha.Controls.Add(tamanho, 3, 0);
        return linha;
    }

    long Selecionado() => _bytes.Where(kv => _caixas[kv.Key].Checked).Sum(kv => kv.Value);

    void AtualizarResumo()
    {
        long selecionado = Selecionado();
        lblTitulo.Text = _analisando ? "Analisando…" : "Dá para liberar";
        lblTotal.Text = _jaAnalisou ? FormatoBytes.Formatar(selecionado) : "—";
        btnLimpar.Enabled = !_analisando && selecionado > 0;

        var disco = InfoSistema.Discos().FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:", StringComparison.OrdinalIgnoreCase));
        if (disco != null && disco.TotalSize > 0)
        {
            long usado = disco.TotalSize - disco.AvailableFreeSpace;
            lblDisco.Text = $"Disco {disco.Name.TrimEnd('\\')} com {FormatoBytes.Formatar(disco.AvailableFreeSpace)} livres de {FormatoBytes.Formatar(disco.TotalSize)}" +
                            (selecionado > 0 ? $" · ficaria com {FormatoBytes.Formatar(disco.AvailableFreeSpace + selecionado)} livres" : "");
            barraDisco.Definir((float)usado / disco.TotalSize, selecionado > 0 ? (float)(usado - selecionado) / disco.TotalSize : null);
        }
    }

    async Task AnalisarAsync()
    {
        if (_analisando) return;
        _analisando = true;
        btnAnalisar.Enabled = false;
        foreach (var l in _tamanhos.Values) l.Text = "…";
        AtualizarResumo();

        foreach (var c in Categorias)
        {
            var resultado = await Task.Run(() => Limpador.Analisar(c.Tipo));
            _bytes[c.Tipo] = resultado.Bytes;
            _tamanhos[c.Tipo].Text = resultado.Bytes == 0 ? "vazio" : FormatoBytes.Formatar(resultado.Bytes);
            _jaAnalisou = true;
            AtualizarResumo();
        }

        _analisando = false;
        btnAnalisar.Enabled = true;
        AtualizarResumo();

        var grandes = await Task.Run(() => Limpador.MaioresArquivos(8, CancellationToken.None));
        string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        listaGrandes.TextoVazio = "Nenhum arquivo acima de 100 MB na sua pasta.";
        listaGrandes.Definir(grandes.Select(a => new ItemDetalhe("",
            Path.GetFileName(a.Caminho), Path.GetRelativePath(perfil, Path.GetDirectoryName(a.Caminho) ?? ""),
            $"{FormatoBytes.Formatar(a.Bytes)} · {FormatoTempo.Relativo(a.Modificado)}", Chave: a.Caminho)).ToList());
    }

    async Task LimparAsync()
    {
        var escolhidas = Categorias.Where(c => _caixas[c.Tipo].Checked && _bytes.GetValueOrDefault(c.Tipo) > 0).ToList();
        if (escolhidas.Count == 0) return;
        if (escolhidas.Any(c => c.Tipo == CategoriaLimpeza.DownloadsAntigos) &&
            MessageBox.Show(FindForm(), "Os downloads com mais de 90 dias vão para a Lixeira. Continuar?", "Limpeza",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        btnLimpar.Enabled = btnAnalisar.Enabled = false;
        long liberado = 0;
        foreach (var c in escolhidas)
        {
            lblTitulo.Text = $"Limpando: {c.Nome}…";
            if (Limpador.PedeAdministrador(c.Tipo))
            {
                long antes = _bytes[c.Tipo];
                if (await Elevado.ExecutarAsync("/limpar-atualizacoes"))
                    liberado += antes - (await Task.Run(() => Limpador.Analisar(c.Tipo))).Bytes;
            }
            else
                liberado += await Task.Run(() => Limpador.Limpar(c.Tipo));
        }
        await AnalisarAsync();
        lblTitulo.Text = $"Liberado agora: {FormatoBytes.Formatar(Math.Max(0, liberado))}";
    }
}

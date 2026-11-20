using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Inicialização": programas que abrem sozinhos com o Windows, com
//  um interruptor para cada um (a mesma marcação do Gerenciador de
//  Tarefas: desligar não apaga nada e dá para ligar de novo)
// ---------------------------------------------------------------
public class AbaInicializacao : AbaBase
{
    readonly CartaoValor cartaoLigados = new(), cartaoDesligados = new(), cartaoInicio = new();
    readonly BotaoPivo pivoTodos = new() { Text = "Todos", Checked = true };
    readonly BotaoPivo pivoLigados = new() { Text = "Ligados" };
    readonly BotaoPivo pivoDesligados = new() { Text = "Desligados" };
    readonly TextBox txtBusca = new() { PlaceholderText = "Procurar programa", Width = 240, Anchor = AnchorStyles.Right };
    readonly TableLayoutPanel linhas = NovoPainel();
    readonly Label lblVazio = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Margin = new Padding(0, 8, 0, 8) };
    readonly ToolTip _dicas = new();

    List<ItemInicializacao> _itens = new();

    public AbaInicializacao()
        : base("Inicialização", "Programas que abrem sozinhos quando o Windows liga. Desligar aqui não desinstala nada: o programa só deixa de abrir sozinho.")
    {
        var cartoes = new GradeAdaptativa { LarguraMinimaColuna = 220, MaximoColunas = 3, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        foreach (var c in new[] { cartaoLigados, cartaoDesligados, cartaoInicio }) cartoes.Adicionar(c);
        Adicionar(cartoes);

        var card = new CardPanel();
        var cabecalho = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var filtros = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        filtros.Controls.AddRange(new Control[] { pivoTodos, pivoLigados, pivoDesligados });
        cabecalho.Controls.Add(filtros, 0, 0);
        txtBusca.AccessibleName = "Procurar programa";
        cabecalho.Controls.Add(txtBusca, 1, 0);
        card.AdicionarLinha(cabecalho);
        card.AdicionarLinha(lblVazio);
        card.AdicionarLinha(linhas);
        Adicionar(card);

        Adicionar(new Label
        {
            Text = "As mudanças valem a partir do próximo início do Windows. Programas do sistema (antivírus, drivers) e apps da Microsoft Store não aparecem aqui.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 4)
        });

        foreach (var p in new[] { pivoTodos, pivoLigados, pivoDesligados })
            p.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) MontarLinhas(); };
        txtBusca.TextChanged += (s, e) => MontarLinhas();
        VisibleChanged += async (s, e) => { if (Visible) await CarregarAsync(); };
    }

    DateTime _carregadoEm = DateTime.MinValue;
    bool _carregando;
    readonly Dictionary<string, Image?> _icones = new(StringComparer.OrdinalIgnoreCase);

    async Task CarregarAsync(bool forcar = false)
    {
        if (_carregando || (!forcar && DateTime.Now - _carregadoEm < TimeSpan.FromSeconds(30))) return;
        _carregando = true;
        try
        {
            // Registro, descrição dos .exe e ícones: tudo fora da janela
            var (itens, icones) = await Task.Run(() =>
            {
                var lista = ItensInicializacao.Listar();
                var mapa = new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase);
                foreach (var caminho in lista.Select(i => i.Caminho).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (_icones.ContainsKey(caminho)) continue;
                    try { using var ico = File.Exists(caminho) ? Icon.ExtractAssociatedIcon(caminho) : null; mapa[caminho] = ico?.ToBitmap(); }
                    catch { mapa[caminho] = null; }
                }
                return (lista, mapa);
            });
            foreach (var (caminho, imagem) in icones) _icones[caminho] = imagem;
            _itens = itens;
            _carregadoEm = DateTime.Now;
            Carregar();
        }
        finally { _carregando = false; }
    }

    void Carregar()
    {
        int ligados = _itens.Count(i => i.Ligado);
        cartaoLigados.Definir("Abrem com o Windows", $"de {_itens.Count} programas", ligados.ToString(), "Menos programas aqui = Windows pronto mais rápido");
        cartaoDesligados.Definir("Desligados", "não abrem sozinhos", (_itens.Count - ligados).ToString(), "Continuam instalados normalmente");
        var inicio = DateTime.Now - InfoSistema.TempoLigado();
        cartaoInicio.Definir("Último início do Windows", "", FormatoTempo.Quando(inicio), $"ligado há {FormatoTempo.Duracao(InfoSistema.TempoLigado())}");
        MontarLinhas();
    }

    void MontarLinhas()
    {
        string busca = txtBusca.Text.Trim();
        var visiveis = _itens.Where(i =>
                (pivoTodos.Checked || (pivoLigados.Checked == i.Ligado)) &&
                (busca.Length == 0 || i.Nome.Contains(busca, StringComparison.CurrentCultureIgnoreCase) ||
                 i.Fabricante.Contains(busca, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        linhas.SuspendLayout();
        var antigas = linhas.Controls.Cast<Control>().ToList();
        linhas.Controls.Clear();
        foreach (var c in antigas) c.Dispose();
        linhas.RowStyles.Clear();
        linhas.RowCount = 0;
        foreach (var item in visiveis) AdicionarEm(linhas, CriarLinha(item));
        linhas.ResumeLayout(true);

        lblVazio.Visible = visiveis.Count == 0;
        lblVazio.Text = _itens.Count == 0 ? "Nenhum programa abre sozinho com o Windows." : "Nenhum programa neste filtro.";
    }

    Control CriarLinha(ItemInicializacao item)
    {
        var linha = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4,
            Margin = new Padding(0), Padding = new Padding(0, 8, 0, 8)
        };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(240, 240, 240));
            e.Graphics.DrawLine(pen, 0, 0, linha.Width, 0);
        };

        var icone = new PictureBox { Size = LogicalToDeviceUnits(new Size(32, 32)), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        icone.Image = _icones.GetValueOrDefault(item.Caminho);
        linha.Controls.Add(icone, 0, 0);

        var textos = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        textos.Controls.Add(new Label { Text = item.Nome, Font = Tema.CorpoForte, AutoSize = true, Margin = new Padding(0),
            ForeColor = item.Ligado ? Tema.Texto : Tema.TextoSecundario });
        string detalhe = string.IsNullOrWhiteSpace(item.Fabricante) ? item.Caminho : $"{item.Fabricante} · {item.Caminho}";
        var lblDetalhe = new Label { Text = detalhe, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, AutoEllipsis = true,
            MaximumSize = new Size(560, 0), Margin = new Padding(0, 2, 0, 0) };
        _dicas.SetToolTip(lblDetalhe, item.Comando);
        textos.Controls.Add(lblDetalhe);
        linha.Controls.Add(textos, 1, 0);

        linha.Controls.Add(new Label
        {
            Text = item.OrigemTexto + (item.PedeAdministrador ? " · pede administrador" : ""),
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 16, 0)
        }, 2, 0);

        var interruptor = new Interruptor { Checked = item.Ligado, Anchor = AnchorStyles.Right, AccessibleName = $"{item.Nome}: abrir com o Windows" };
        interruptor.Size = LogicalToDeviceUnits(new Size(44, 24));   // criado depois do ajuste de zoom da janela
        interruptor.CheckedChanged += async (s, e) => await AlternarAsync(item, interruptor);
        linha.Controls.Add(interruptor, 3, 0);
        return linha;
    }

    async Task AlternarAsync(ItemInicializacao item, Interruptor interruptor)
    {
        bool ligar = interruptor.Checked;
        if (ligar == item.Ligado) return;
        string chave = ItensInicializacao.ChaveDoItem(item);

        bool ok = ItensInicializacao.Definir(item.Origem, chave, ligar);
        if (!ok && item.PedeAdministrador)
            ok = await Elevado.ExecutarAsync($"/inicializacao {item.Origem} {(ligar ? "on" : "off")} \"{chave}\"");

        if (!ok)
        {
            MessageBox.Show(FindForm(), "Não foi possível mudar este item. Ele vale para todos os usuários e precisa de permissão de administrador.",
                "Inicialização", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        BeginInvoke(async () => await CarregarAsync(forcar: true));   // recarrega depois que o clique terminar (a linha é recriada)
    }
}

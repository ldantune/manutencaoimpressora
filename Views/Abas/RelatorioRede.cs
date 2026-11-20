using System.Globalization;
using System.Text;
using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Visão "Relatório" da aba Rede: resumo de hoje / semana / mês /
//  total, gráfico dos últimos 30 dias, comparação com o mês passado
//  e dias de maior uso, com filtro por conexão (Wi-Fi, Ethernet...)
// ---------------------------------------------------------------
public class RelatorioRede : TableLayoutPanel
{
    const string TodasAsConexoes = "Todas as conexões";
    const string TextoDicaGrafico = "Passe o mouse nas barras para ver o dia";

    readonly FonteUsoRede _fonte;

    readonly ComboBox cbConexao = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Anchor = AnchorStyles.Left };

    readonly CartaoUso cartaoHoje = new();
    readonly CartaoUso cartaoSemana = new();
    readonly CartaoUso cartaoMes = new();
    readonly CartaoUso cartaoTotal = new();

    readonly GraficoBarras grafico = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    readonly Label lblDiaGrafico = new()
    {
        AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right,
        Text = TextoDicaGrafico
    };

    readonly Label lblMesTotal = new() { AutoSize = true, Font = Tema.Numero, Margin = new Padding(0, 0, 0, 2) };
    readonly Label lblMesPassado = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Margin = new Padding(0, 0, 0, 10) };
    readonly BarraComparacao barraMes = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
    readonly Label lblPrevisao = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0) };

    readonly ListaRanking listaTopDias = new() { Dock = DockStyle.Fill, Margin = new Padding(0), TextoVazio = "Ainda não há uso registrado neste mês." };

    readonly Label lblRodape = new()
    {
        Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(520, 0),
        Anchor = AnchorStyles.Left, Margin = new Padding(0)
    };

    long _pendenteRecebidos, _pendenteEnviados;
    bool _carregandoConexoes;

    public RelatorioRede(FonteUsoRede fonte)
    {
        _fonte = fonte;

        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AdicionarLinha(MontarFiltro());
        AdicionarLinha(MontarCartoes());
        AdicionarLinha(MontarCardGrafico());

        var linhaInferior = new GradeAdaptativa { LarguraMinimaColuna = 380, MaximoColunas = 2, Espaco = 12 };
        linhaInferior.Adicionar(MontarCardComparacao());
        linhaInferior.Adicionar(MontarCardTopDias());
        linhaInferior.Margin = new Padding(0, 0, 0, 12);
        AdicionarLinha(linhaInferior);

        AdicionarLinha(MontarRodape());

        grafico.DiaSobMouse += MostrarDia;
        cbConexao.SelectedIndexChanged += (s, e) => { if (!_carregandoConexoes) Recarregar(); };
    }

    void AdicionarLinha(Control c)
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowCount = RowStyles.Count;
        Controls.Add(c, 0, RowCount - 1);
    }

    static Label TituloCard(string texto) =>
        new() { Text = texto, Font = Tema.Subtitulo, AutoSize = true, Margin = new Padding(0), Anchor = AnchorStyles.Left };

    Control MontarFiltro()
    {
        var linha = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 12)
        };
        linha.Controls.Add(new Label { Text = "Conexão", AutoSize = true, Margin = new Padding(0, 6, 10, 0) });
        cbConexao.Margin = new Padding(0);
        cbConexao.AccessibleName = "Conexão";
        linha.Controls.Add(cbConexao);
        cbConexao.Items.Add(TodasAsConexoes);
        cbConexao.SelectedIndex = 0;
        return linha;
    }

    Control MontarCartoes()
    {
        var grade = new GradeAdaptativa { LarguraMinimaColuna = 200, MaximoColunas = 4, Espaco = 12 };
        foreach (var cartao in new[] { cartaoHoje, cartaoSemana, cartaoMes, cartaoTotal })
            grade.Adicionar(cartao);
        grade.Margin = new Padding(0, 0, 0, 12);
        return grade;
    }

    CardPanel MontarCardGrafico()
    {
        var card = new CardPanel { Dock = DockStyle.Fill };

        var cabecalho = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 4)
        };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalho.Controls.Add(TituloCard("Últimos 30 dias"), 0, 0);
        cabecalho.Controls.Add(lblDiaGrafico, 1, 0);
        card.AdicionarLinha(cabecalho);

        var legenda = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 8)
        };
        legenda.Controls.Add(ItemLegenda("Download", Tema.CorDownload));
        legenda.Controls.Add(ItemLegenda("Upload", Tema.CorUpload));
        card.AdicionarLinha(legenda);

        card.AdicionarLinha(grafico);
        return card;
    }

    static Control ItemLegenda(string texto, Color cor)
    {
        var painel = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 16, 0)
        };
        painel.Controls.Add(new Label { Text = "■", ForeColor = cor, AutoSize = true, Margin = new Padding(0, 0, 2, 0), Font = Tema.Corpo });
        painel.Controls.Add(new Label { Text = texto, Font = Tema.Legenda, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
        return painel;
    }

    CardPanel MontarCardComparacao()
    {
        var card = new CardPanel();
        var titulo = TituloCard("Este mês x mês passado");
        titulo.Margin = new Padding(0, 0, 0, 8);
        card.AdicionarLinha(titulo);
        card.AdicionarLinha(lblMesTotal);
        card.AdicionarLinha(lblMesPassado);
        card.AdicionarLinha(barraMes);
        card.AdicionarLinha(lblPrevisao);
        return card;
    }

    CardPanel MontarCardTopDias()
    {
        var card = new CardPanel();
        var titulo = TituloCard("Dias de maior uso no mês");
        titulo.Margin = new Padding(0, 0, 0, 8);
        card.AdicionarLinha(titulo);
        card.AdicionarLinha(listaTopDias);
        return card;
    }

    Control MontarRodape()
    {
        var linha = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 4)
        };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.Controls.Add(lblRodape, 0, 0);

        var btnExportar = new BotaoFluent { Text = "Exportar CSV", Anchor = AnchorStyles.Right };
        btnExportar.Click += (s, e) => ExportarCsv();
        linha.Controls.Add(btnExportar, 1, 0);
        return linha;
    }

    // ---------------- Dados ----------------

    string? ConexaoSelecionada =>
        cbConexao.SelectedIndex <= 0 ? null : cbConexao.SelectedItem?.ToString();

    // Mantém a lista de conexões em dia sem perder a seleção
    void AtualizarConexoes()
    {
        var atual = ConexaoSelecionada;
        var conexoes = _fonte.Interfaces();
        var existentes = cbConexao.Items.Cast<object>().Skip(1).Select(o => o.ToString()).ToList();
        if (existentes.SequenceEqual(conexoes)) return;

        _carregandoConexoes = true;
        cbConexao.Items.Clear();
        cbConexao.Items.Add(TodasAsConexoes);
        foreach (var c in conexoes) cbConexao.Items.Add(c);
        int indice = atual is null ? 0 : cbConexao.Items.IndexOf(atual);
        cbConexao.SelectedIndex = indice < 0 ? 0 : indice;
        _carregandoConexoes = false;
    }

    // pendenteRecebidos/Enviados: o que a tela mediu e ainda não gravou (só quando o monitor está parado)
    public void Atualizar(long pendenteRecebidos, long pendenteEnviados)
    {
        _pendenteRecebidos = pendenteRecebidos;
        _pendenteEnviados = pendenteEnviados;
        AtualizarConexoes();
        Recarregar();
    }

    void Recarregar()
    {
        var hoje = DateTime.Today;
        var cultura = CultureInfo.CurrentCulture;
        string? conexao = ConexaoSelecionada;
        // O pendente da tela não tem conexão identificada: só entra em "Todas"
        long pendR = conexao is null ? _pendenteRecebidos : 0;
        long pendE = conexao is null ? _pendenteEnviados : 0;

        int recuo = ((int)hoje.DayOfWeek - (int)cultura.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var inicioSemana = hoje.AddDays(-recuo);
        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1);
        var primeiroDia = _fonte.PrimeiroDia(conexao) ?? hoje;

        // Uma consulta cobre tudo: do primeiro dia registrado (ou 30 dias atrás) até hoje
        var desde = new[] { primeiroDia, hoje.AddDays(-29), inicioMes.AddMonths(-1) }.Min();
        var dias = _fonte.ListarDias(desde, hoje, conexao);
        if (dias.Count > 0)
        {
            var ultimo = dias[^1];
            dias[^1] = ultimo with { Recebidos = ultimo.Recebidos + pendR, Enviados = ultimo.Enviados + pendE };
        }

        (long R, long E) Somar(DateTime de, DateTime ate)
        {
            var periodo = dias.Where(d => d.Data >= de && d.Data <= ate).ToList();
            return (periodo.Sum(d => d.Recebidos), periodo.Sum(d => d.Enviados));
        }

        var tHoje = Somar(hoje, hoje);
        var tSemana = Somar(inicioSemana, hoje);
        var tMes = Somar(inicioMes, hoje);
        var tTotal = Somar(primeiroDia, hoje);

        cartaoHoje.Definir("Hoje", hoje.ToString("dd/MM"), tHoje.R, tHoje.E);
        cartaoSemana.Definir("Esta semana", $"desde {inicioSemana:dd/MM}", tSemana.R, tSemana.E);
        cartaoMes.Definir("Este mês", hoje.ToString("MMMM", cultura), tMes.R, tMes.E);
        cartaoTotal.Definir("Total", $"desde {primeiroDia:dd/MM/yyyy}", tTotal.R, tTotal.E);

        grafico.Definir(dias.Where(d => d.Data >= hoje.AddDays(-29)).ToList());

        var mesPassado = Somar(inicioMes.AddMonths(-1), inicioMes.AddDays(-1));
        AtualizarComparacao(inicioMes, tMes.R + tMes.E, mesPassado.R + mesPassado.E, cultura);

        listaTopDias.Definir(dias
            .Where(d => d.Data >= inicioMes && d.Total > 0)
            .OrderByDescending(d => d.Total)
            .Take(5)
            .Select(d => (d.Data.ToString("ddd, dd/MM", cultura), d.Total))
            .ToList());

        lblRodape.Text = conexao is null
            ? "Soma de todas as conexões (placas virtuais, como as do Hyper-V, ficam de fora para não contar em dobro)."
            : $"Somente a conexão \"{conexao}\". Dias de antes do monitor de rede só aparecem em \"Todas as conexões\".";
    }

    void AtualizarComparacao(DateTime inicioMes, long usoMes, long usoPassado, CultureInfo cultura)
    {
        string nomeMesPassado = inicioMes.AddMonths(-1).ToString("MMMM", cultura);

        lblMesTotal.Text = FormatoBytes.Formatar(usoMes);
        lblMesPassado.Text = usoPassado > 0
            ? $"de {FormatoBytes.Formatar(usoPassado)} em {nomeMesPassado}"
            : $"Sem dados de {nomeMesPassado} para comparar.";

        // Previsão de fechamento: só a partir de 2 dias de mês, para não exagerar
        double diasDecorridos = (DateTime.Now - inicioMes).TotalDays;
        int diasNoMes = DateTime.DaysInMonth(inicioMes.Year, inicioMes.Month);
        long? previsao = diasDecorridos >= 2 ? (long)(usoMes / diasDecorridos * diasNoMes) : null;

        long escala = Math.Max(Math.Max(usoMes, usoPassado), previsao ?? 0);
        barraMes.Definir(escala > 0 ? (float)usoMes / escala : 0,
                         usoPassado > 0 && escala > 0 ? (float)usoPassado / escala : null);

        if (previsao is long p)
        {
            string comparacao = "";
            if (usoPassado > 0)
            {
                double diferenca = (p - usoPassado) / (double)usoPassado;
                comparacao = diferenca >= 0
                    ? $" ({diferenca.ToString("P0", cultura)} a mais que em {nomeMesPassado})"
                    : $" ({(-diferenca).ToString("P0", cultura)} a menos que em {nomeMesPassado})";
            }
            lblPrevisao.Text = $"No ritmo atual, o mês fecha com cerca de {FormatoBytes.Formatar(p)}{comparacao}.";
        }
        else
        {
            lblPrevisao.Text = "Ainda é cedo para prever como o mês vai fechar.";
        }
    }

    void MostrarDia(UsoDia? dia)
    {
        lblDiaGrafico.Text = dia is null
            ? TextoDicaGrafico
            : $"{dia.Data:ddd, dd/MM}: {FormatoBytes.Formatar(dia.Total)}  " +
              $"(↓ {FormatoBytes.Formatar(dia.Recebidos)} · ↑ {FormatoBytes.Formatar(dia.Enviados)})";
    }

    void ExportarCsv()
    {
        string? conexao = ConexaoSelecionada;
        var primeiroDia = _fonte.PrimeiroDia(conexao);
        if (primeiroDia is null)
        {
            MessageBox.Show("Ainda não há uso registrado para exportar.", "Exportar CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string sufixo = conexao is null ? "" : "_" + string.Concat(conexao.Split(Path.GetInvalidFileNameChars()));
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar uso de internet",
            Filter = "CSV (separado por ponto e vírgula)|*.csv",
            FileName = $"uso_internet{sufixo}_{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            // Ponto e vírgula + vírgula decimal: abre direto no Excel em português
            var csv = new StringBuilder();
            csv.AppendLine("Data;Download (MB);Upload (MB);Total (MB)");
            foreach (var d in _fonte.ListarDias(primeiroDia.Value, DateTime.Today, conexao))
                csv.AppendLine($"{d.Data:dd/MM/yyyy};{d.Recebidos / FormatoBytes.MB:0.00};" +
                               $"{d.Enviados / FormatoBytes.MB:0.00};{d.Total / FormatoBytes.MB:0.00}");
            File.WriteAllText(dlg.FileName, csv.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível salvar o arquivo: " + ex.Message, "Exportar CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

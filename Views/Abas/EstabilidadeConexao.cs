using System.Globalization;
using System.Text;
using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Conexão › Estabilidade: situação agora, resumo (quedas, disponibilidade,
//  ping, perda de pacotes), ping das últimas 24 h e lista de quedas
// ---------------------------------------------------------------
public class EstabilidadeConexao : TableLayoutPanel
{
    static readonly Color CorErro = Color.FromArgb(196, 43, 28);

    readonly BancoUsoApps _monitor;
    readonly BarraStatus barra = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, 12) };
    readonly CartaoValor cartaoQuedas = new(), cartaoDisponibilidade = new(), cartaoPing = new(), cartaoPerda = new();
    readonly GraficoLinha grafico = new() { Height = 150, Dock = DockStyle.Fill, MaximoAmostras = 24 * 60, CorLinha = Tema.Destaque, Margin = new Padding(0) };
    readonly Label lblGrafico = new() { AutoSize = true, Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Right };
    readonly ListaDetalhes listaQuedas = new() { Dock = DockStyle.Fill, Margin = new Padding(0), TextoVazio = "Nenhuma queda registrada nos últimos 30 dias." };

    public EstabilidadeConexao(BancoUsoApps monitor)
    {
        _monitor = monitor;
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AbaBase.AdicionarEm(this, barra);

        var cartoes = new GradeAdaptativa { LarguraMinimaColuna = 200, MaximoColunas = 4, Espaco = 12, Margin = new Padding(0, 0, 0, 12) };
        foreach (var c in new[] { cartaoQuedas, cartaoDisponibilidade, cartaoPing, cartaoPerda }) cartoes.Adicionar(c);
        AbaBase.AdicionarEm(this, cartoes);

        var cardGrafico = new CardPanel();
        var cabecalho = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0)
        };
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalho.Controls.Add(AbaBase.TituloCard("Ping nas últimas 24 horas"), 0, 0);
        cabecalho.Controls.Add(lblGrafico, 1, 0);
        cardGrafico.AdicionarLinha(cabecalho);
        cardGrafico.AdicionarLinha(grafico);
        cardGrafico.AdicionarLinha(new Label
        {
            Text = "Quanto mais baixo, mais rápida a resposta. Os trechos no zero são momentos sem resposta (quedas).",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, Margin = new Padding(0, 6, 0, 0)
        });
        AbaBase.AdicionarEm(this, cardGrafico);

        var cardQuedas = new CardPanel { Margin = new Padding(0) };
        var cabecalhoQuedas = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4)
        };
        cabecalhoQuedas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cabecalhoQuedas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalhoQuedas.Controls.Add(AbaBase.TituloCard("Quedas recentes"), 0, 0);
        var btnExportar = new BotaoFluent { Text = "Exportar para o provedor (CSV)", Anchor = AnchorStyles.Right };
        btnExportar.Click += (s, e) => Exportar();
        cabecalhoQuedas.Controls.Add(btnExportar, 1, 0);
        cardQuedas.AdicionarLinha(cabecalhoQuedas);
        cardQuedas.AdicionarLinha(listaQuedas);
        AbaBase.AdicionarEm(this, cardQuedas);
    }

    public void Atualizar()
    {
        var agora = DateTime.Now;
        var hoje = DateTime.Today;
        bool rodando = InstaladorServico.EstaRodando();
        var minutos = _monitor.ListarLatencia(agora.AddHours(-24));
        var quedas = _monitor.ListarQuedas(hoje.AddDays(-30), 500);
        var aberta = quedas.FirstOrDefault(q => q.Fim is null);

        // Situação agora
        if (!rodando)
            barra.Definir(Severidade.Neutro, "Monitor de rede desligado",
                "As quedas são medidas pelo monitor de rede. Ative-o em Rede › Por app para acompanhar a internet o tempo todo.");
        else if (aberta != null)
            barra.Definir(Severidade.Aviso, $"Sem internet há {FormatoTempo.Duracao(agora - aberta.Inicio)} (desde {aberta.Inicio:HH:mm})",
                aberta.Motivo + ". O programa avisa na bandeja quando a internet voltar.");
        else
        {
            var ultimoPing = minutos.LastOrDefault(m => m.Respostas > 0);
            var ultimaQueda = quedas.FirstOrDefault();
            string detalhe = ultimoPing?.Media is double ms ? $"Tempo de resposta (ping) agora: {ms:0} ms" : "Medindo o tempo de resposta…";
            if (ultimaQueda is { Fim: DateTime fim })
                detalhe += $" · última queda {FormatoTempo.Quando(ultimaQueda.Inicio)}, durou {FormatoTempo.Duracao(fim - ultimaQueda.Inicio)}";
            barra.Definir(Severidade.Sucesso, "Internet funcionando", detalhe);
        }

        // Quedas hoje
        var quedasHoje = quedas.Where(q => (q.Fim ?? agora) >= hoje).ToList();
        var foraHoje = TimeSpan.FromSeconds(quedasHoje.Sum(q => ((q.Fim ?? agora) - (q.Inicio < hoje ? hoje : q.Inicio)).TotalSeconds));
        cartaoQuedas.Definir("Quedas hoje", hoje.ToString("dd/MM"), quedasHoje.Count.ToString(),
            quedasHoje.Count == 0 ? "Nenhuma queda" : $"{FormatoTempo.Duracao(foraHoje)} sem internet",
            quedasHoje.Count > 0 ? CorErro : null);

        // Disponibilidade nos últimos 7 dias (só o tempo em que o monitor estava medindo)
        var primeiro = _monitor.PrimeiroMinutoLatencia();
        var inicioJanela = new[] { agora.AddDays(-7), primeiro ?? agora }.Max();
        double janela = (agora - inicioJanela).TotalSeconds;
        var quedas7 = quedas.Where(q => (q.Fim ?? agora) >= inicioJanela).ToList();
        double fora7 = quedas7.Sum(q => ((q.Fim ?? agora) - (q.Inicio < inicioJanela ? inicioJanela : q.Inicio)).TotalSeconds);
        cartaoDisponibilidade.Definir("Disponibilidade", "últimos 7 dias",
            janela < 60 ? "—" : (1 - fora7 / janela).ToString("P1", CultureInfo.CurrentCulture),
            $"{quedas7.Count} {(quedas7.Count == 1 ? "queda" : "quedas")} · {FormatoTempo.Duracao(TimeSpan.FromSeconds(fora7))} sem internet");

        // Ping e perda de hoje
        var deHoje = minutos.Where(m => m.Minuto >= hoje).ToList();
        int respostas = deHoje.Sum(m => m.Respostas), perdas = deHoje.Sum(m => m.Perdas);
        double soma = deHoje.Sum(m => m.Soma);
        var pior = deHoje.Where(m => m.Respostas > 0).OrderByDescending(m => m.Maximo).FirstOrDefault();
        cartaoPing.Definir("Ping médio", "hoje", respostas > 0 ? $"{soma / respostas:0} ms" : "—",
            pior is null ? "" : $"máximo {pior.Maximo:0} ms às {pior.Minuto:HH:mm}");
        int medicoes = respostas + perdas;
        cartaoPerda.Definir("Perda de pacotes", "hoje", medicoes > 0 ? ((double)perdas / medicoes).ToString("P1", CultureInfo.CurrentCulture) : "—",
            medicoes > 0 ? $"{perdas:N0} de {medicoes:N0} medições" : "");

        // Gráfico: um ponto por minuto nas últimas 24 h (sem resposta = 0)
        var porMinuto = minutos.ToDictionary(m => m.Minuto);
        var inicio24 = agora.AddHours(-24);
        var minuto0 = new DateTime(inicio24.Year, inicio24.Month, inicio24.Day, inicio24.Hour, inicio24.Minute, 0);
        var amostras = new List<double>(24 * 60 + 2);
        double? anterior = null;
        for (var m = minuto0; m <= agora; m = m.AddMinutes(1))
        {
            if (porMinuto.TryGetValue(m, out var medido))
                anterior = medido.Media ?? 0;
            else if (aberta != null && m >= aberta.Inicio)
                anterior = 0;
            amostras.Add(anterior ?? 0);
        }
        grafico.DefinirAmostras(amostras);
        double maximo24 = minutos.Count == 0 ? 0 : minutos.Max(m => m.Maximo);
        lblGrafico.Text = maximo24 > 0 ? $"pico de {maximo24:0} ms" : "";

        // Lista de quedas
        listaQuedas.Definir(quedas.Take(20).Select(q => new ItemDetalhe(
            Icones.Aviso,
            char.ToUpper(FormatoTempo.Quando(q.Inicio)[0]) + FormatoTempo.Quando(q.Inicio)[1..],
            q.Motivo,
            q.Fim is DateTime f ? FormatoTempo.Duracao(f - q.Inicio) : $"há {FormatoTempo.Duracao(agora - q.Inicio)}",
            Selo: q.Fim is null ? "Agora" : null, CorSelo: CorErro,
            CorIcone: q.Fim is null ? CorErro : Tema.TextoSecundario,
            FundoIcone: q.Fim is null ? Color.FromArgb(253, 231, 233) : null)).ToList());
    }

    // CSV com as quedas dos últimos 30 dias, para mandar ao provedor
    void Exportar()
    {
        var quedas = _monitor.ListarQuedas(DateTime.Today.AddDays(-30), 5000).OrderBy(q => q.Inicio).ToList();
        if (quedas.Count == 0)
        {
            MessageBox.Show(FindForm(), "Nenhuma queda registrada nos últimos 30 dias.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar quedas da internet",
            Filter = "CSV (separado por ponto e vírgula)|*.csv",
            FileName = $"quedas_internet_{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var agora = DateTime.Now;
            var csv = new StringBuilder();
            csv.AppendLine("Início;Fim;Duração (minutos);Motivo");
            foreach (var q in quedas)
                csv.AppendLine($"{q.Inicio:dd/MM/yyyy HH:mm:ss};{(q.Fim is DateTime f ? f.ToString("dd/MM/yyyy HH:mm:ss") : "em andamento")};" +
                               $"{q.Duracao(agora).TotalMinutes:0.0};{q.Motivo.Replace(';', ',')}");
            csv.AppendLine();
            csv.AppendLine($"Total de quedas;{quedas.Count}");
            csv.AppendLine($"Tempo total sem internet (minutos);{quedas.Sum(q => q.Duracao(agora).TotalMinutes):0.0}");
            File.WriteAllText(dlg.FileName, csv.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Não foi possível salvar o arquivo: " + ex.Message, "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

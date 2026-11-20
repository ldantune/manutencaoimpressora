using System.Globalization;
using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  Conexão › Velocidade: teste de download/upload/ping, comparação
//  com o plano contratado e histórico dos testes
// ---------------------------------------------------------------
public class VelocidadeConexao : TableLayoutPanel
{
    const string ChavePlano = "plano_contratado_mbps";
    static readonly Color CorAbaixo = Color.FromArgb(157, 93, 0);

    readonly BancoLocal _banco;
    readonly CartaoValor cartaoDownload = new(), cartaoUpload = new(), cartaoPing = new();
    readonly BotaoFluent btnTestar = new() { Text = "Testar agora", Estilo = EstiloBotao.Primario, Icone = Icones.Velocidade };
    readonly BotaoFluent btnCancelar = new() { Text = "Cancelar", Visible = false };
    readonly Label lblStatus = new() { AutoSize = true, ForeColor = Tema.TextoSecundario, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 0, 0) };
    readonly BarraComparacao barraProgresso = new() { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0, 0, 0, 12) };
    readonly NumericUpDown numPlano = new() { Minimum = 0, Maximum = 10_000, Increment = 50, Width = 90, TextAlign = HorizontalAlignment.Right };
    readonly ListaDetalhes historico = new() { Dock = DockStyle.Fill, Margin = new Padding(0), TextoVazio = "Nenhum teste ainda. Clique em \"Testar agora\"." };

    CancellationTokenSource? _cancelar;
    bool _carregado;

    public VelocidadeConexao(BancoLocal banco)
    {
        _banco = banco;
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var cardTeste = new CardPanel();
        var cartoes = new GradeAdaptativa { LarguraMinimaColuna = 180, MaximoColunas = 3, Espaco = 12, Margin = new Padding(0, 0, 0, 14) };
        foreach (var c in new[] { cartaoDownload, cartaoUpload, cartaoPing }) cartoes.Adicionar(c);
        cardTeste.AdicionarLinha(cartoes);
        cardTeste.AdicionarLinha(barraProgresso);

        var acoes = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, Margin = new Padding(0)
        };
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acoes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        btnTestar.Anchor = AnchorStyles.Left;
        btnCancelar.Anchor = AnchorStyles.Left;
        btnCancelar.Margin = new Padding(8, 0, 0, 0);
        acoes.Controls.Add(btnTestar, 0, 0);
        acoes.Controls.Add(btnCancelar, 1, 0);
        acoes.Controls.Add(lblStatus, 2, 0);
        acoes.Controls.Add(new Label { Text = "Plano contratado", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) }, 3, 0);
        var plano = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        numPlano.AccessibleName = "Plano contratado em Mbps";
        numPlano.Margin = new Padding(0);
        plano.Controls.Add(numPlano);
        plano.Controls.Add(new Label { Text = "Mbps", AutoSize = true, Margin = new Padding(6, 5, 0, 0) });
        acoes.Controls.Add(plano, 4, 0);
        cardTeste.AdicionarLinha(acoes);
        AbaBase.AdicionarEm(this, cardTeste);

        var cardHistorico = new CardPanel { Margin = new Padding(0, 12, 0, 0) };
        cardHistorico.AdicionarLinha(AbaBase.TituloCard("Histórico de testes"));
        cardHistorico.AdicionarLinha(historico);
        cardHistorico.AdicionarLinha(new Label
        {
            Text = "Usa os servidores públicos de teste da Cloudflare. Para um resultado fiel, prefira testar com o computador no cabo e sem downloads rodando.",
            Font = Tema.Legenda, ForeColor = Tema.TextoSecundario, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 8, 0, 0)
        });
        AbaBase.AdicionarEm(this, cardHistorico);

        btnTestar.Click += async (s, e) => await TestarAsync();
        btnCancelar.Click += (s, e) => _cancelar?.Cancel();
        numPlano.ValueChanged += (s, e) =>
        {
            if (!_carregado) return;
            _banco.GravarMeta(ChavePlano, ((int)numPlano.Value).ToString(CultureInfo.InvariantCulture));
            Atualizar();
        };
    }

    double Plano => (double)numPlano.Value;

    static string Mbps(double v) => v >= 100 ? $"{v:0}" : $"{v:0.0}";

    public void Atualizar()
    {
        if (!_carregado)
        {
            if (int.TryParse(_banco.LerMeta(ChavePlano), NumberStyles.Integer, CultureInfo.InvariantCulture, out int plano))
                numPlano.Value = Math.Clamp(plano, 0, 10_000);
            _carregado = true;
        }
        if (_cancelar != null) return;   // teste em andamento: não mexe nos números

        var testes = _banco.ListarTestes(20);
        var ultimo = testes.FirstOrDefault();
        MostrarResultado(ultimo);
        lblStatus.Text = ultimo is null ? "Nenhum teste ainda." : $"Último teste {FormatoTempo.Quando(ultimo.Momento)}";

        historico.Definir(testes.Select(t =>
        {
            bool abaixo = Plano > 0 && t.Download < Plano * 0.8;
            return new ItemDetalhe(Icones.Velocidade,
                char.ToUpper(FormatoTempo.Quando(t.Momento)[0]) + FormatoTempo.Quando(t.Momento)[1..],
                $"Ping {t.Ping:0} ms · variação {t.Jitter:0} ms",
                $"↓ {Mbps(t.Download)} · ↑ {Mbps(t.Upload)} Mbps",
                Selo: abaixo ? "Abaixo do plano" : null, CorSelo: CorAbaixo,
                CorIcone: abaixo ? CorAbaixo : Tema.Destaque);
        }).ToList());
    }

    void MostrarResultado(TesteSalvo? t)
    {
        if (t is null)
        {
            cartaoDownload.Definir("↓ Download", "Mbps", "—", "");
            cartaoUpload.Definir("↑ Upload", "Mbps", "—", "");
            cartaoPing.Definir("Ping", "ms", "—", "");
            return;
        }
        string detalhePlano = Plano > 0 ? $"{t.Download / Plano:P0} do plano de {Plano:0} Mbps" : "Informe o plano para comparar";
        bool abaixo = Plano > 0 && t.Download < Plano * 0.8;
        cartaoDownload.Definir("↓ Download", "Mbps", Mbps(t.Download), detalhePlano, abaixo ? CorAbaixo : null);
        cartaoUpload.Definir("↑ Upload", "Mbps", Mbps(t.Upload), "");
        cartaoPing.Definir("Ping", "ms", $"{t.Ping:0}", $"variação (jitter) {t.Jitter:0} ms");
    }

    async Task TestarAsync()
    {
        _cancelar = new CancellationTokenSource();
        btnTestar.Enabled = false;
        btnCancelar.Visible = true;
        barraProgresso.Visible = true;
        barraProgresso.Definir(0, null);
        cartaoDownload.Definir("↓ Download", "Mbps", "—", "aguardando");
        cartaoUpload.Definir("↑ Upload", "Mbps", "—", "aguardando");
        cartaoPing.Definir("Ping", "ms", "—", "medindo…");

        var progresso = new Progress<(TesteVelocidade.Fase Fase, double Valor, double Progresso)>(p =>
        {
            barraProgresso.Definir((float)(p.Progresso / 100), null);
            switch (p.Fase)
            {
                case TesteVelocidade.Fase.Ping:
                    cartaoPing.Definir("Ping", "ms", $"{p.Valor:0}", "medindo…");
                    lblStatus.Text = "Medindo o ping…";
                    break;
                case TesteVelocidade.Fase.Download:
                    cartaoDownload.Definir("↓ Download", "Mbps", Mbps(p.Valor), "medindo…");
                    lblStatus.Text = "Medindo o download…";
                    break;
                case TesteVelocidade.Fase.Upload:
                    cartaoUpload.Definir("↑ Upload", "Mbps", Mbps(p.Valor), "medindo…");
                    lblStatus.Text = "Medindo o upload…";
                    break;
            }
        });

        string? mensagem = null;
        try
        {
            var r = await new TesteVelocidade().ExecutarAsync(progresso, _cancelar.Token);
            _banco.SalvarTeste(new TesteSalvo(DateTime.Now, r.DownloadMbps, r.UploadMbps, r.PingMs, r.JitterMs));
        }
        catch (OperationCanceledException) { mensagem = "Teste cancelado."; }
        catch (Exception ex) { mensagem = "Não foi possível testar: " + ex.GetBaseException().Message; }
        finally
        {
            _cancelar.Dispose();
            _cancelar = null;
            btnTestar.Enabled = true;
            btnCancelar.Visible = false;
            barraProgresso.Visible = false;
        }
        Atualizar();
        if (mensagem != null) lblStatus.Text = mensagem;
    }
}

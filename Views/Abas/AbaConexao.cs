using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views.Fluent;

namespace ManutencaoImpressora.Views.Abas;

// ---------------------------------------------------------------
//  "Conexão": estabilidade da internet (quedas e ping, medidos pelo
//  monitor de rede) e teste de velocidade. Avisa na bandeja quando a
//  internet cai e quando volta.
// ---------------------------------------------------------------
public class AbaConexao : AbaBase, INotificador
{
    readonly BancoUsoApps _monitor = new(BancoUsoApps.CaminhoPadrao);
    readonly BotaoPivo pivoEstabilidade = new() { Text = "Estabilidade", Checked = true };
    readonly BotaoPivo pivoVelocidade = new() { Text = "Velocidade" };
    readonly EstabilidadeConexao estabilidade;
    readonly VelocidadeConexao velocidade;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 10_000 };

    bool _estadoConhecido;
    long? _quedaAvisada;

    public event Action<string, string, ToolTipIcon>? Notificar;

    public AbaConexao()
        : base("Conexão", "Acompanha a internet o tempo todo (mesmo com o programa fechado), registra cada queda e mede a velocidade.")
    {
        estabilidade = new EstabilidadeConexao(_monitor);
        velocidade = new VelocidadeConexao(new BancoLocal(BancoLocal.CaminhoPadrao)) { Visible = false };

        var pivo = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 10)
        };
        pivo.Controls.Add(pivoEstabilidade);
        pivo.Controls.Add(pivoVelocidade);
        Adicionar(pivo);
        Adicionar(estabilidade);
        Adicionar(velocidade);

        foreach (var opcao in new[] { pivoEstabilidade, pivoVelocidade })
            opcao.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) MostrarVisao(); };

        _timer.Tick += (s, e) => Verificar();
        _timer.Start();
        VisibleChanged += (s, e) => { if (Visible) MostrarVisao(); };
        Verificar();
    }

    void MostrarVisao()
    {
        SuspendLayout();
        estabilidade.Visible = pivoEstabilidade.Checked;
        velocidade.Visible = pivoVelocidade.Checked;
        if (estabilidade.Visible) estabilidade.Atualizar();
        if (velocidade.Visible) velocidade.Atualizar();
        ResumeLayout(true);
    }

    // A cada 10 s: avisa quedas/retornos e atualiza a tela se estiver aberta
    void Verificar()
    {
        var ultima = _monitor.ListarQuedas(DateTime.Now.AddDays(-2), 1).FirstOrDefault();
        long? aberta = ultima is { Fim: null } ? ultima.Id : null;

        if (!_estadoConhecido)
        {
            _estadoConhecido = true;   // ao abrir o programa não avisa o que já estava acontecendo
            _quedaAvisada = aberta;
        }
        else if (aberta is long id && _quedaAvisada != id)
        {
            _quedaAvisada = id;
            Notificar?.Invoke("A internet caiu", ultima!.Motivo, ToolTipIcon.Warning);
        }
        else if (aberta is null && _quedaAvisada is long anterior)
        {
            _quedaAvisada = null;
            string texto = ultima is { Fim: DateTime fim } && ultima.Id == anterior
                ? $"Ficou fora por {FormatoTempo.Duracao(fim - ultima.Inicio)}."
                : "A conexão foi restabelecida.";
            Notificar?.Invoke("A internet voltou", texto, ToolTipIcon.Info);
        }

        if (Visible && estabilidade.Visible) estabilidade.Atualizar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

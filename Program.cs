using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views;

namespace ManutencaoImpressora;

// ---------------------------------------------------------------
//  Ponto de entrada
//  - Sem argumentos: abre a tela de configuração
//  - Com /imprimir: imprime a página colorida (usado pelo agendador)
//  - Com /servico: roda como serviço do Windows (monitor de rede por app)
//  - Com /instalar-servico ou /remover-servico: usado pelo instalador
// ---------------------------------------------------------------
static class Program
{
    const string NomeTarefa = "ManutencaoImpressora";

    [STAThread]
    static int Main(string[] args)
    {
        bool Tem(string argumento) => args.Any(a => a.Equals(argumento, StringComparison.OrdinalIgnoreCase));

        if (Tem("/servico"))
        {
            System.ServiceProcess.ServiceBase.Run(new ServicoMonitor());
            return 0;
        }
        if (Tem("/instalar-servico")) return InstaladorServico.Instalar();
        if (Tem("/remover-servico")) return InstaladorServico.Remover();

        string pastaDados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManutencaoImpressora");
        Directory.CreateDirectory(pastaDados);

        string arquivoConfig = Path.Combine(pastaDados, "config.json");
        var log = new HistoricoLog(Path.Combine(pastaDados, "historico.db"));
        var agendador = new AgendadorTarefa(NomeTarefa, pastaDados);
        var historicoRede = new HistoricoRede(Path.Combine(pastaDados, "uso_rede.db"));

        if (args.Any(a => a.Equals("/imprimir", StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = Config.Carregar(arquivoConfig);
            try
            {
                new Impressora(cfg.Impressora, cfg.CaminhoDocumento).Imprimir();
                log.Registrar("Impressão automática enviada.");
                return 0;
            }
            catch (Exception ex)
            {
                log.Registrar("ERRO na impressão automática: " + ex.Message);
                return 1;
            }
        }

        // Uma instância só da janela: se já houver uma (talvez escondida na bandeja),
        // pede para ela aparecer e sai, em vez de criar outro ícone na bandeja
        using var instancia = new Mutex(true, @"Local\L2Utilitarios_Janela", out bool primeiraInstancia);
        using var pedidoMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L2Utilitarios_Mostrar");
        if (!primeiraInstancia)
        {
            pedidoMostrar.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var janela = new FormPrincipal(arquivoConfig, log, agendador, historicoRede);
        janela.EscutarPedidosParaMostrar(pedidoMostrar);
        Application.Run(janela);
        return 0;
    }
}

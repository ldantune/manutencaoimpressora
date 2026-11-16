using ManutencaoImpressora.Models;
using ManutencaoImpressora.Services;
using ManutencaoImpressora.Views;

namespace ManutencaoImpressora;

// ---------------------------------------------------------------
//  Ponto de entrada
//  - Sem argumentos: abre a tela de configuração
//  - Com /imprimir: imprime a página colorida (usado pelo agendador)
// ---------------------------------------------------------------
static class Program
{
    const string NomeTarefa = "ManutencaoImpressora";

    [STAThread]
    static int Main(string[] args)
    {
        string pastaDados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManutencaoImpressora");
        Directory.CreateDirectory(pastaDados);

        string arquivoConfig = Path.Combine(pastaDados, "config.json");
        var log = new HistoricoLog(Path.Combine(pastaDados, "historico.txt"));
        var agendador = new AgendadorTarefa(NomeTarefa, pastaDados);

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

        ApplicationConfiguration.Initialize();
        Application.Run(new FormPrincipal(arquivoConfig, log, agendador));
        return 0;
    }
}

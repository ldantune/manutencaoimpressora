using System.ServiceProcess;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Serviço do Windows "L2 Utilitários - Monitor de rede". Roda o
//  mesmo .exe com o argumento /servico, como Sistema, e conta o uso
//  de internet por aplicativo mesmo com a janela do programa fechada.
// ---------------------------------------------------------------
public class ServicoMonitor : ServiceBase
{
    MonitorRedePorApp? _monitor;

    public ServicoMonitor()
    {
        ServiceName = InstaladorServico.Nome;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            _monitor = new MonitorRedePorApp(new BancoUsoApps(BancoUsoApps.CaminhoPadrao));
            _monitor.Iniciar();
            LogMonitor.Registrar("Serviço iniciado.");
        }
        catch (Exception ex)
        {
            LogMonitor.Registrar("Falha ao iniciar o serviço: " + ex);
            throw;   // o Windows marca o serviço como parado e tenta de novo em 1 minuto
        }
    }

    protected override void OnStop() => Encerrar();
    protected override void OnShutdown() => Encerrar();

    void Encerrar()
    {
        _monitor?.Dispose();
        _monitor = null;
    }
}

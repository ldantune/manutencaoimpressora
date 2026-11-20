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
    MonitorConexao? _conexao;

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

        // Quedas da internet: se falhar, o resto do monitor continua
        try
        {
            _conexao = new MonitorConexao(new BancoUsoApps(BancoUsoApps.CaminhoPadrao));
            _conexao.Iniciar();
        }
        catch (Exception ex)
        {
            LogMonitor.Registrar("Falha ao iniciar a medição de quedas: " + ex.Message);
        }
    }

    protected override void OnStop() => Encerrar();
    protected override void OnShutdown() => Encerrar();

    void Encerrar()
    {
        _conexao?.Dispose();
        _conexao = null;
        _monitor?.Dispose();
        _monitor = null;
    }
}

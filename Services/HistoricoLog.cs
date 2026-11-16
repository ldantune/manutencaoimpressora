namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Histórico de execuções, gravado em %AppData%\ManutencaoImpressora\historico.txt
// ---------------------------------------------------------------
public class HistoricoLog
{
    readonly string _arquivo;

    public HistoricoLog(string arquivo) => _arquivo = arquivo;

    public string Arquivo => _arquivo;

    public void Registrar(string mensagem)
    {
        try
        {
            File.AppendAllText(_arquivo,
                $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} - {mensagem}{Environment.NewLine}");
        }
        catch { /* não deixa o log derrubar o programa */ }
    }
}

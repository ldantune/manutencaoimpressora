using Microsoft.Data.Sqlite;
using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Histórico de execuções, gravado em um banco SQLite em
//  %AppData%\ManutencaoImpressora\historico.db
// ---------------------------------------------------------------
public class HistoricoLog
{
    readonly string _arquivo;
    readonly string _connectionString;

    public HistoricoLog(string arquivo)
    {
        _arquivo = arquivo;
        _connectionString = $"Data Source={_arquivo}";
        CriarTabela();
    }

    public string Arquivo => _arquivo;

    void CriarTabela()
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                CREATE TABLE IF NOT EXISTS Historico (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DataHora TEXT NOT NULL,
                    Mensagem TEXT NOT NULL
                )
                """;
            comando.ExecuteNonQuery();
        }
        catch { /* não deixa o log derrubar o programa */ }
    }

    public void Registrar(string mensagem)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "INSERT INTO Historico (DataHora, Mensagem) VALUES ($dataHora, $mensagem)";
            comando.Parameters.AddWithValue("$dataHora", DateTime.Now.ToString("O"));
            comando.Parameters.AddWithValue("$mensagem", mensagem);
            comando.ExecuteNonQuery();
        }
        catch { /* não deixa o log derrubar o programa */ }
    }

    public List<RegistroHistorico> Listar()
    {
        var registros = new List<RegistroHistorico>();
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT Id, DataHora, Mensagem FROM Historico ORDER BY Id DESC";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                registros.Add(new RegistroHistorico(
                    leitor.GetInt32(0),
                    DateTime.Parse(leitor.GetString(1)),
                    leitor.GetString(2)));
            }
        }
        catch { /* retorna o que conseguiu ler */ }
        return registros;
    }
}

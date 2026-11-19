using Microsoft.Data.Sqlite;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Uso de internet por dia, gravado em um banco SQLite em
//  %AppData%\ManutencaoImpressora\uso_rede.db, para permitir ver o
//  total de hoje, dos últimos 7 dias e do mês atual
// ---------------------------------------------------------------
public class HistoricoRede
{
    readonly string _connectionString;

    public HistoricoRede(string arquivo)
    {
        _connectionString = $"Data Source={arquivo}";
        CriarTabela();
    }

    void CriarTabela()
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                CREATE TABLE IF NOT EXISTS UsoRede (
                    Data TEXT PRIMARY KEY,
                    Recebidos INTEGER NOT NULL DEFAULT 0,
                    Enviados INTEGER NOT NULL DEFAULT 0
                )
                """;
            comando.ExecuteNonQuery();
        }
        catch { /* não deixa o registro derrubar o programa */ }
    }

    // Soma os bytes transferidos ao total do dia informado (chamado
    // periodicamente, nunca substitui o total, só acrescenta)
    public void Acumular(DateTime data, long recebidos, long enviados)
    {
        if (recebidos <= 0 && enviados <= 0) return;

        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                INSERT INTO UsoRede (Data, Recebidos, Enviados) VALUES ($data, $recebidos, $enviados)
                ON CONFLICT(Data) DO UPDATE SET
                    Recebidos = Recebidos + $recebidos,
                    Enviados = Enviados + $enviados
                """;
            comando.Parameters.AddWithValue("$data", data.Date.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("$recebidos", recebidos);
            comando.Parameters.AddWithValue("$enviados", enviados);
            comando.ExecuteNonQuery();
        }
        catch { /* não deixa o registro derrubar o programa */ }
    }

    public (long Recebidos, long Enviados) TotalDesde(DateTime data)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT COALESCE(SUM(Recebidos),0), COALESCE(SUM(Enviados),0) FROM UsoRede WHERE Data >= $data";
            comando.Parameters.AddWithValue("$data", data.Date.ToString("yyyy-MM-dd"));
            using var leitor = comando.ExecuteReader();
            if (leitor.Read()) return (leitor.GetInt64(0), leitor.GetInt64(1));
        }
        catch { /* retorna zero se não conseguir ler */ }
        return (0, 0);
    }

    public (long Recebidos, long Enviados) TotalHoje() => TotalDesde(DateTime.Today);
    public (long Recebidos, long Enviados) TotalUltimos7Dias() => TotalDesde(DateTime.Today.AddDays(-6));
    public (long Recebidos, long Enviados) TotalMesAtual() => TotalDesde(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
}

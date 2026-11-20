using System.Globalization;
using Microsoft.Data.Sqlite;
using ManutencaoImpressora.Models;

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
        CriarTabelasWindows();
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

    // ---------------- Consultas para o relatório ----------------

    // Um item por dia entre as duas datas (inclusive); dias sem registro vêm zerados
    public List<UsoDia> ListarDias(DateTime desde, DateTime ate)
    {
        var porData = new Dictionary<DateTime, (long Recebidos, long Enviados)>();
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT Data, Recebidos, Enviados FROM UsoRede WHERE Data >= $desde AND Data <= $ate";
            comando.Parameters.AddWithValue("$desde", desde.Date.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("$ate", ate.Date.ToString("yyyy-MM-dd"));
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                if (DateTime.TryParseExact(leitor.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var data))
                    porData[data] = (leitor.GetInt64(1), leitor.GetInt64(2));
            }
        }
        catch { /* devolve os dias zerados se não conseguir ler */ }

        var lista = new List<UsoDia>();
        for (var d = desde.Date; d <= ate.Date; d = d.AddDays(1))
        {
            porData.TryGetValue(d, out var v);
            lista.Add(new UsoDia(d, v.Recebidos, v.Enviados));
        }
        return lista;
    }

    public (long Recebidos, long Enviados) TotalEntre(DateTime desde, DateTime ate)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT COALESCE(SUM(Recebidos),0), COALESCE(SUM(Enviados),0) FROM UsoRede WHERE Data >= $desde AND Data <= $ate";
            comando.Parameters.AddWithValue("$desde", desde.Date.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("$ate", ate.Date.ToString("yyyy-MM-dd"));
            using var leitor = comando.ExecuteReader();
            if (leitor.Read()) return (leitor.GetInt64(0), leitor.GetInt64(1));
        }
        catch { /* retorna zero se não conseguir ler */ }
        return (0, 0);
    }

    // Primeiro dia com uso registrado (para o cartão "Total" e a exportação)
    public DateTime? PrimeiroDia()
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT MIN(Data) FROM UsoRede";
            if (comando.ExecuteScalar() is string texto &&
                DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                return data;
        }
        catch { /* sem dados */ }
        return null;
    }

    // ---------------- Histórico importado do Windows ----------------
    // Uso por app que o próprio Windows registra (Configurações › Rede › Uso de dados),
    // para mostrar dias de antes de o monitor do L2 existir

    void CriarTabelasWindows()
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                CREATE TABLE IF NOT EXISTS UsoAppWindows (
                    Data TEXT NOT NULL,
                    App TEXT NOT NULL,
                    Caminho TEXT NOT NULL DEFAULT '',
                    Recebidos INTEGER NOT NULL DEFAULT 0,
                    Enviados INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (Data, App)
                );
                CREATE TABLE IF NOT EXISTS Meta (Chave TEXT PRIMARY KEY, Valor TEXT NOT NULL);
                """;
            comando.ExecuteNonQuery();
        }
        catch { }
    }

    // Troca o que havia entre as datas pelo que veio do Windows
    public void SubstituirAppsWindows(DateTime desde, DateTime ate,
        IEnumerable<(DateTime Dia, string App, string Caminho, long Recebidos, long Enviados)> itens)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var transacao = conexao.BeginTransaction();
            using (var apagar = conexao.CreateCommand())
            {
                apagar.Transaction = transacao;
                apagar.CommandText = "DELETE FROM UsoAppWindows WHERE Data >= $desde AND Data <= $ate";
                apagar.Parameters.AddWithValue("$desde", desde.Date.ToString("yyyy-MM-dd"));
                apagar.Parameters.AddWithValue("$ate", ate.Date.ToString("yyyy-MM-dd"));
                apagar.ExecuteNonQuery();
            }
            using (var inserir = conexao.CreateCommand())
            {
                inserir.Transaction = transacao;
                inserir.CommandText = """
                    INSERT INTO UsoAppWindows (Data, App, Caminho, Recebidos, Enviados)
                    VALUES ($data, $app, $caminho, $recebidos, $enviados)
                    ON CONFLICT(Data, App) DO UPDATE SET Recebidos = Recebidos + $recebidos, Enviados = Enviados + $enviados
                    """;
                var pData = inserir.Parameters.Add("$data", SqliteType.Text);
                var pApp = inserir.Parameters.Add("$app", SqliteType.Text);
                var pCaminho = inserir.Parameters.Add("$caminho", SqliteType.Text);
                var pRecebidos = inserir.Parameters.Add("$recebidos", SqliteType.Integer);
                var pEnviados = inserir.Parameters.Add("$enviados", SqliteType.Integer);
                foreach (var item in itens)
                {
                    pData.Value = item.Dia.ToString("yyyy-MM-dd");
                    pApp.Value = item.App;
                    pCaminho.Value = item.Caminho;
                    pRecebidos.Value = item.Recebidos;
                    pEnviados.Value = item.Enviados;
                    inserir.ExecuteNonQuery();
                }
            }
            transacao.Commit();
        }
        catch { /* não deixa a importação derrubar o programa */ }
    }

    // Uso por app importado, em [desde, ateExclusive)
    public List<UsoApp> ListarAppsWindows(DateTime desde, DateTime ateExclusive)
    {
        var lista = new List<UsoApp>();
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                SELECT App, MAX(Caminho), SUM(Recebidos), SUM(Enviados) FROM UsoAppWindows
                WHERE Data >= $desde AND Data < $ate GROUP BY App
                """;
            comando.Parameters.AddWithValue("$desde", desde.Date.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("$ate", ateExclusive.Date.ToString("yyyy-MM-dd"));
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                lista.Add(new UsoApp(leitor.GetString(0), leitor.IsDBNull(1) ? "" : leitor.GetString(1),
                    leitor.GetInt64(2), leitor.GetInt64(3)));
        }
        catch { }
        return lista;
    }

    // Total por dia do histórico importado (soma de todos os apps)
    public Dictionary<DateTime, (long Recebidos, long Enviados)> DiasWindows(DateTime desde, DateTime ate)
    {
        var dias = new Dictionary<DateTime, (long, long)>();
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                SELECT Data, SUM(Recebidos), SUM(Enviados) FROM UsoAppWindows
                WHERE Data >= $desde AND Data <= $ate GROUP BY Data
                """;
            comando.Parameters.AddWithValue("$desde", desde.Date.ToString("yyyy-MM-dd"));
            comando.Parameters.AddWithValue("$ate", ate.Date.ToString("yyyy-MM-dd"));
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                if (DateTime.TryParseExact(leitor.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d))
                    dias[d] = (leitor.GetInt64(1), leitor.GetInt64(2));
        }
        catch { }
        return dias;
    }

    public DateTime? PrimeiroDiaWindows()
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT MIN(Data) FROM UsoAppWindows";
            if (comando.ExecuteScalar() is string texto &&
                DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }
        catch { }
        return null;
    }

    public string? LerMeta(string chave)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT Valor FROM Meta WHERE Chave = $chave";
            comando.Parameters.AddWithValue("$chave", chave);
            return comando.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    public void GravarMeta(string chave, string valor)
    {
        try
        {
            using var conexao = new SqliteConnection(_connectionString);
            conexao.Open();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "INSERT INTO Meta (Chave, Valor) VALUES ($chave, $valor) ON CONFLICT(Chave) DO UPDATE SET Valor = $valor";
            comando.Parameters.AddWithValue("$chave", chave);
            comando.Parameters.AddWithValue("$valor", valor);
            comando.ExecuteNonQuery();
        }
        catch { }
    }
}

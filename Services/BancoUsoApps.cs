using System.Globalization;
using Microsoft.Data.Sqlite;
using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Banco do monitor de rede, em C:\ProgramData\L2Utilitarios.
//  Quem grava é o serviço (rodando como Sistema); a tela só lê, então
//  não precisa de permissão de administrador.
//    UsoApp       = uso por aplicativo e por dia
//    UsoInterface = uso total por conexão (Wi-Fi, Ethernet...) e por dia
//    Latencia     = ping somado por minuto (estabilidade da conexão)
//    Queda        = cada período sem internet
// ---------------------------------------------------------------
public class BancoUsoApps
{
    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L2Utilitarios", "uso_apps.db");

    readonly string _arquivo;

    public BancoUsoApps(string arquivo)
    {
        _arquivo = arquivo;
    }

    public bool Existe => File.Exists(_arquivo);

    SqliteConnection AbrirEscrita()
    {
        var conexao = new SqliteConnection($"Data Source={_arquivo}");
        conexao.Open();
        return conexao;
    }

    SqliteConnection? AbrirLeitura()
    {
        if (!Existe) return null;
        var conexao = new SqliteConnection($"Data Source={_arquivo};Mode=ReadOnly");
        conexao.Open();
        return conexao;
    }

    static string Texto(DateTime d) => d.Date.ToString("yyyy-MM-dd");

    static DateTime? Data(object? valor) =>
        valor is string s && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    // ---------------- Escrita (serviço) ----------------

    public void CriarSeNecessario()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
        using var conexao = AbrirEscrita();
        using var comando = conexao.CreateCommand();
        comando.CommandText = """
            CREATE TABLE IF NOT EXISTS UsoApp (
                Data TEXT NOT NULL,
                App TEXT NOT NULL,
                Caminho TEXT NOT NULL DEFAULT '',
                Recebidos INTEGER NOT NULL DEFAULT 0,
                Enviados INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (Data, App)
            );
            CREATE TABLE IF NOT EXISTS UsoInterface (
                Data TEXT NOT NULL,
                Interface TEXT NOT NULL,
                Recebidos INTEGER NOT NULL DEFAULT 0,
                Enviados INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (Data, Interface)
            );
            CREATE TABLE IF NOT EXISTS Latencia (
                Minuto TEXT PRIMARY KEY,
                Soma REAL NOT NULL DEFAULT 0,
                Maximo REAL NOT NULL DEFAULT 0,
                Respostas INTEGER NOT NULL DEFAULT 0,
                Perdas INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS Queda (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Inicio TEXT NOT NULL,
                Fim TEXT,
                Motivo TEXT NOT NULL DEFAULT ''
            );
            """;
        comando.ExecuteNonQuery();
    }

    public void Acumular(IEnumerable<(DateTime Dia, string App, string Caminho, long Recebidos, long Enviados)> apps,
                         IEnumerable<(DateTime Dia, string Interface, long Recebidos, long Enviados)> interfaces)
    {
        using var conexao = AbrirEscrita();
        using var transacao = conexao.BeginTransaction();

        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = """
                INSERT INTO UsoApp (Data, App, Caminho, Recebidos, Enviados)
                VALUES ($data, $app, $caminho, $recebidos, $enviados)
                ON CONFLICT(Data, App) DO UPDATE SET
                    Recebidos = Recebidos + $recebidos,
                    Enviados = Enviados + $enviados,
                    Caminho = CASE WHEN $caminho <> '' THEN $caminho ELSE Caminho END
                """;
            var pData = comando.Parameters.Add("$data", SqliteType.Text);
            var pApp = comando.Parameters.Add("$app", SqliteType.Text);
            var pCaminho = comando.Parameters.Add("$caminho", SqliteType.Text);
            var pRecebidos = comando.Parameters.Add("$recebidos", SqliteType.Integer);
            var pEnviados = comando.Parameters.Add("$enviados", SqliteType.Integer);
            foreach (var item in apps)
            {
                pData.Value = Texto(item.Dia);
                pApp.Value = item.App;
                pCaminho.Value = item.Caminho;
                pRecebidos.Value = item.Recebidos;
                pEnviados.Value = item.Enviados;
                comando.ExecuteNonQuery();
            }
        }

        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = """
                INSERT INTO UsoInterface (Data, Interface, Recebidos, Enviados)
                VALUES ($data, $interface, $recebidos, $enviados)
                ON CONFLICT(Data, Interface) DO UPDATE SET
                    Recebidos = Recebidos + $recebidos,
                    Enviados = Enviados + $enviados
                """;
            var pData = comando.Parameters.Add("$data", SqliteType.Text);
            var pInterface = comando.Parameters.Add("$interface", SqliteType.Text);
            var pRecebidos = comando.Parameters.Add("$recebidos", SqliteType.Integer);
            var pEnviados = comando.Parameters.Add("$enviados", SqliteType.Integer);
            foreach (var item in interfaces)
            {
                pData.Value = Texto(item.Dia);
                pInterface.Value = item.Interface;
                pRecebidos.Value = item.Recebidos;
                pEnviados.Value = item.Enviados;
                comando.ExecuteNonQuery();
            }
        }

        transacao.Commit();
    }

    // ---------------- Leitura (tela) ----------------

    // Uso somado por app desde a data informada, do maior para o menor
    public List<UsoApp> Listar(DateTime desde)
    {
        var lista = new List<UsoApp>();
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return lista;
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                SELECT App, MAX(Caminho), SUM(Recebidos), SUM(Enviados)
                FROM UsoApp WHERE Data >= $desde
                GROUP BY App
                ORDER BY SUM(Recebidos) + SUM(Enviados) DESC
                """;
            comando.Parameters.AddWithValue("$desde", Texto(desde));
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                lista.Add(new UsoApp(leitor.GetString(0), leitor.IsDBNull(1) ? "" : leitor.GetString(1),
                    leitor.GetInt64(2), leitor.GetInt64(3)));
        }
        catch { /* banco ocupado por um instante ou ainda não criado */ }
        return lista;
    }

    // Uso por dia (só os dias com registro); interfaceNome null = todas as conexões
    public Dictionary<DateTime, (long Recebidos, long Enviados)> DiasInterfaces(DateTime desde, DateTime ate, string? interfaceNome)
    {
        var dias = new Dictionary<DateTime, (long, long)>();
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return dias;
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                SELECT Data, SUM(Recebidos), SUM(Enviados) FROM UsoInterface
                WHERE Data >= $desde AND Data <= $ate AND ($interface IS NULL OR Interface = $interface)
                GROUP BY Data
                """;
            comando.Parameters.AddWithValue("$desde", Texto(desde));
            comando.Parameters.AddWithValue("$ate", Texto(ate));
            comando.Parameters.AddWithValue("$interface", (object?)interfaceNome ?? DBNull.Value);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                if (Data(leitor.GetString(0)) is DateTime d) dias[d] = (leitor.GetInt64(1), leitor.GetInt64(2));
        }
        catch { }
        return dias;
    }

    // Conexões já vistas pelo monitor, da mais usada para a menos usada
    public List<string> Interfaces()
    {
        var lista = new List<string>();
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return lista;
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT Interface FROM UsoInterface GROUP BY Interface ORDER BY SUM(Recebidos) + SUM(Enviados) DESC";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read()) lista.Add(leitor.GetString(0));
        }
        catch { }
        return lista;
    }

    public DateTime? PrimeiroDiaApps() => Escalar("SELECT MIN(Data) FROM UsoApp");
    public DateTime? PrimeiroDiaInterfaces(string? interfaceNome) =>
        Escalar("SELECT MIN(Data) FROM UsoInterface WHERE $interface IS NULL OR Interface = $interface", interfaceNome);

    DateTime? Escalar(string sql, string? interfaceNome = null)
    {
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return null;
            using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            comando.Parameters.AddWithValue("$interface", (object?)interfaceNome ?? DBNull.Value);
            return Data(comando.ExecuteScalar());
        }
        catch { return null; }
    }

    // ---------------- Estabilidade da conexão ----------------

    const string FormatoMomento = "yyyy-MM-dd HH:mm:ss";
    const string FormatoMinuto = "yyyy-MM-dd HH:mm";

    static DateTime? Momento(object? valor) =>
        valor is string s && DateTime.TryParseExact(s, new[] { FormatoMomento, FormatoMinuto }, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    // Soma as medições de ping de cada minuto (serviço)
    public void RegistrarLatencia(IEnumerable<MinutoLatencia> minutos)
    {
        using var conexao = AbrirEscrita();
        using var transacao = conexao.BeginTransaction();
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = """
            INSERT INTO Latencia (Minuto, Soma, Maximo, Respostas, Perdas) VALUES ($minuto, $soma, $maximo, $respostas, $perdas)
            ON CONFLICT(Minuto) DO UPDATE SET
                Soma = Soma + $soma, Maximo = MAX(Maximo, $maximo),
                Respostas = Respostas + $respostas, Perdas = Perdas + $perdas
            """;
        var pMinuto = comando.Parameters.Add("$minuto", SqliteType.Text);
        var pSoma = comando.Parameters.Add("$soma", SqliteType.Real);
        var pMaximo = comando.Parameters.Add("$maximo", SqliteType.Real);
        var pRespostas = comando.Parameters.Add("$respostas", SqliteType.Integer);
        var pPerdas = comando.Parameters.Add("$perdas", SqliteType.Integer);
        foreach (var m in minutos)
        {
            pMinuto.Value = m.Minuto.ToString(FormatoMinuto, CultureInfo.InvariantCulture);
            pSoma.Value = m.Soma;
            pMaximo.Value = m.Maximo;
            pRespostas.Value = m.Respostas;
            pPerdas.Value = m.Perdas;
            comando.ExecuteNonQuery();
        }
        transacao.Commit();
    }

    public long? AbrirQueda(DateTime inicio, string motivo)
    {
        try
        {
            using var conexao = AbrirEscrita();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "INSERT INTO Queda (Inicio, Motivo) VALUES ($inicio, $motivo); SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$inicio", inicio.ToString(FormatoMomento, CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$motivo", motivo);
            return Convert.ToInt64(comando.ExecuteScalar());
        }
        catch { return null; }
    }

    public void FecharQueda(long id, DateTime fim)
    {
        try
        {
            using var conexao = AbrirEscrita();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "UPDATE Queda SET Fim = $fim WHERE Id = $id";
            comando.Parameters.AddWithValue("$fim", fim.ToString(FormatoMomento, CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }
        catch { }
    }

    // Ao iniciar o serviço: quedas que ficaram abertas (PC desligado, serviço parado) terminam
    // no último minuto em que houve medição depois delas
    public void FecharQuedasAbertas()
    {
        try
        {
            using var conexao = AbrirEscrita();
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                UPDATE Queda SET Fim = COALESCE(
                    (SELECT MAX(Minuto) || ':59' FROM Latencia WHERE Minuto >= substr(Queda.Inicio, 1, 16)), Inicio)
                WHERE Fim IS NULL
                """;
            comando.ExecuteNonQuery();
        }
        catch { }
    }

    public List<MinutoLatencia> ListarLatencia(DateTime desde)
    {
        var lista = new List<MinutoLatencia>();
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return lista;
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT Minuto, Soma, Maximo, Respostas, Perdas FROM Latencia WHERE Minuto >= $desde ORDER BY Minuto";
            comando.Parameters.AddWithValue("$desde", desde.ToString(FormatoMinuto, CultureInfo.InvariantCulture));
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                if (Momento(leitor.GetString(0)) is DateTime minuto)
                    lista.Add(new MinutoLatencia(minuto, leitor.GetDouble(1), leitor.GetDouble(2), leitor.GetInt32(3), leitor.GetInt32(4)));
        }
        catch { }
        return lista;
    }

    public DateTime? PrimeiroMinutoLatencia()
    {
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return null;
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT MIN(Minuto) FROM Latencia";
            return Momento(comando.ExecuteScalar());
        }
        catch { return null; }
    }

    // Quedas que terminaram depois de "desde" (ou ainda abertas), da mais recente para a mais antiga
    public List<Queda> ListarQuedas(DateTime desde, int limite)
    {
        var lista = new List<Queda>();
        try
        {
            using var conexao = AbrirLeitura();
            if (conexao is null) return lista;
            using var comando = conexao.CreateCommand();
            comando.CommandText = """
                SELECT Id, Inicio, Fim, Motivo FROM Queda
                WHERE Fim IS NULL OR Fim >= $desde
                ORDER BY Inicio DESC LIMIT $limite
                """;
            comando.Parameters.AddWithValue("$desde", desde.ToString(FormatoMomento, CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$limite", limite);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
                if (Momento(leitor.GetString(1)) is DateTime inicio)
                    lista.Add(new Queda(leitor.GetInt64(0), inicio, leitor.IsDBNull(2) ? null : Momento(leitor.GetString(2)), leitor.GetString(3)));
        }
        catch { }
        return lista;
    }
}

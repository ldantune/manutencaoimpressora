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
}

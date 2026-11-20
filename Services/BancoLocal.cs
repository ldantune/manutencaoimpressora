using System.Globalization;
using Microsoft.Data.Sqlite;
using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Dados das ferramentas que rodam na própria tela (do usuário):
//  testes de velocidade, aparelhos vistos na rede e preferências.
//  %AppData%\ManutencaoImpressora\ferramentas.db
// ---------------------------------------------------------------
public class BancoLocal
{
    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManutencaoImpressora", "ferramentas.db");

    const string Formato = "yyyy-MM-dd HH:mm:ss";
    readonly string _conexao;

    public BancoLocal(string arquivo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        _conexao = $"Data Source={arquivo}";
        try
        {
            Executar("""
                CREATE TABLE IF NOT EXISTS TesteVelocidade (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Momento TEXT NOT NULL, Download REAL NOT NULL, Upload REAL NOT NULL, Ping REAL NOT NULL, Jitter REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Dispositivo (
                    Mac TEXT PRIMARY KEY, Apelido TEXT, Nome TEXT, Fabricante TEXT, UltimoIp TEXT,
                    PrimeiraVez TEXT NOT NULL, UltimaVez TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Meta (Chave TEXT PRIMARY KEY, Valor TEXT NOT NULL);
                """);
        }
        catch { }
    }

    SqliteConnection Abrir()
    {
        var c = new SqliteConnection(_conexao);
        c.Open();
        return c;
    }

    void Executar(string sql, params (string Nome, object? Valor)[] parametros)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static string Texto(DateTime d) => d.ToString(Formato, CultureInfo.InvariantCulture);
    static DateTime Data(string s) =>
        DateTime.TryParseExact(s, Formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

    // ---------------- Testes de velocidade ----------------

    public void SalvarTeste(TesteSalvo t)
    {
        try
        {
            Executar("INSERT INTO TesteVelocidade (Momento, Download, Upload, Ping, Jitter) VALUES ($m, $d, $u, $p, $j)",
                ("$m", Texto(t.Momento)), ("$d", t.Download), ("$u", t.Upload), ("$p", t.Ping), ("$j", t.Jitter));
        }
        catch { }
    }

    public List<TesteSalvo> ListarTestes(int limite)
    {
        var lista = new List<TesteSalvo>();
        try
        {
            using var c = Abrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Momento, Download, Upload, Ping, Jitter FROM TesteVelocidade ORDER BY Id DESC LIMIT $l";
            cmd.Parameters.AddWithValue("$l", limite);
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(new TesteSalvo(Data(r.GetString(0)), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)));
        }
        catch { }
        return lista;
    }

    // ---------------- Dispositivos ----------------

    public int QuantidadeDispositivos()
    {
        try
        {
            using var c = Abrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Dispositivo";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    // Registra os aparelhos vistos agora; devolve os endereços físicos (MAC) que nunca tinham aparecido
    public List<string> RegistrarVistos(IEnumerable<(string Mac, string Ip, string? Nome, string? Fabricante)> vistos, DateTime quando)
    {
        var novos = new List<string>();
        try
        {
            using var c = Abrir();
            using var transacao = c.BeginTransaction();
            foreach (var (mac, ip, nome, fabricante) in vistos)
            {
                using var existe = c.CreateCommand();
                existe.Transaction = transacao;
                existe.CommandText = "SELECT COUNT(*) FROM Dispositivo WHERE Mac = $mac";
                existe.Parameters.AddWithValue("$mac", mac);
                bool jaVisto = Convert.ToInt32(existe.ExecuteScalar()) > 0;

                using var cmd = c.CreateCommand();
                cmd.Transaction = transacao;
                cmd.CommandText = jaVisto
                    ? """
                      UPDATE Dispositivo SET UltimoIp = $ip, UltimaVez = $quando,
                          Nome = COALESCE($nome, Nome), Fabricante = COALESCE($fab, Fabricante)
                      WHERE Mac = $mac
                      """
                    : "INSERT INTO Dispositivo (Mac, Nome, Fabricante, UltimoIp, PrimeiraVez, UltimaVez) VALUES ($mac, $nome, $fab, $ip, $quando, $quando)";
                cmd.Parameters.AddWithValue("$mac", mac);
                cmd.Parameters.AddWithValue("$ip", ip);
                cmd.Parameters.AddWithValue("$quando", Texto(quando));
                cmd.Parameters.AddWithValue("$nome", (object?)nome ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fab", (object?)fabricante ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                if (!jaVisto) novos.Add(mac);
            }
            transacao.Commit();
        }
        catch { }
        return novos;
    }

    public List<DispositivoConhecido> ListarDispositivos()
    {
        var lista = new List<DispositivoConhecido>();
        try
        {
            using var c = Abrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Mac, Apelido, Nome, Fabricante, UltimoIp, PrimeiraVez, UltimaVez FROM Dispositivo ORDER BY UltimaVez DESC";
            using var r = cmd.ExecuteReader();
            string? Opcional(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (r.Read())
                lista.Add(new DispositivoConhecido(r.GetString(0), Opcional(1), Opcional(2), Opcional(3), Opcional(4),
                    Data(r.GetString(5)), Data(r.GetString(6))));
        }
        catch { }
        return lista;
    }

    public void Renomear(string mac, string? apelido)
    {
        try
        {
            Executar("UPDATE Dispositivo SET Apelido = $a WHERE Mac = $mac",
                ("$a", string.IsNullOrWhiteSpace(apelido) ? null : apelido.Trim()), ("$mac", mac));
        }
        catch { }
    }

    // ---------------- Preferências ----------------

    public string? LerMeta(string chave)
    {
        try
        {
            using var c = Abrir();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Valor FROM Meta WHERE Chave = $c";
            cmd.Parameters.AddWithValue("$c", chave);
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    public void GravarMeta(string chave, string valor)
    {
        try { Executar("INSERT INTO Meta (Chave, Valor) VALUES ($c, $v) ON CONFLICT(Chave) DO UPDATE SET Valor = $v", ("$c", chave), ("$v", valor)); }
        catch { }
    }
}

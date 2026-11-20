using System.Diagnostics;
using Microsoft.Win32;

namespace ManutencaoImpressora.Services;

public enum OrigemInicializacao { RegistroUsuario, RegistroComputador, RegistroComputador32, PastaUsuario, PastaComputador }

public record ItemInicializacao(OrigemInicializacao Origem, string Nome, string Comando, string Caminho,
    string Fabricante, bool Ligado)
{
    // Itens do computador (todos os usuários) só mudam com administrador
    public bool PedeAdministrador => Origem is OrigemInicializacao.RegistroComputador or
        OrigemInicializacao.RegistroComputador32 or OrigemInicializacao.PastaComputador;

    public string OrigemTexto => Origem switch
    {
        OrigemInicializacao.RegistroUsuario => "Registro do usuário",
        OrigemInicializacao.RegistroComputador or OrigemInicializacao.RegistroComputador32 => "Registro do computador",
        OrigemInicializacao.PastaUsuario => "Pasta Inicializar",
        _ => "Pasta Inicializar (todos)"
    };
}

// ---------------------------------------------------------------
//  Programas que abrem com o Windows, nos mesmos lugares que o
//  Gerenciador de Tarefas olha (chaves "Run" do registro e pastas
//  "Inicializar"). Ligar/desligar usa a mesma marcação que ele
//  (chaves StartupApproved): nada é apagado.
// ---------------------------------------------------------------
public static class ItensInicializacao
{
    const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string Aprovados = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    static string PastaUsuario => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    static string PastaComputador => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    public static List<ItemInicializacao> Listar()
    {
        var lista = new List<ItemInicializacao>();
        LerRegistro(lista, Registry.CurrentUser, Run, OrigemInicializacao.RegistroUsuario);
        LerRegistro(lista, Registry.LocalMachine, Run, OrigemInicializacao.RegistroComputador);
        LerRegistro(lista, Registry.LocalMachine, Run32, OrigemInicializacao.RegistroComputador32);
        LerPasta(lista, PastaUsuario, OrigemInicializacao.PastaUsuario);
        LerPasta(lista, PastaComputador, OrigemInicializacao.PastaComputador);
        return lista.OrderByDescending(i => i.Ligado).ThenBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void LerRegistro(List<ItemInicializacao> lista, RegistryKey raiz, string caminho, OrigemInicializacao origem)
    {
        try
        {
            using var chave = raiz.OpenSubKey(caminho);
            if (chave is null) return;
            foreach (var nome in chave.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(nome) || chave.GetValue(nome) is not string comando) continue;
                string exe = ExtrairExecutavel(comando);
                lista.Add(new ItemInicializacao(origem, NomeExibicao(nome, exe), comando, exe, Fabricante(exe), EstaLigado(origem, nome)));
            }
        }
        catch { }
    }

    static void LerPasta(List<ItemInicializacao> lista, string pasta, OrigemInicializacao origem)
    {
        try
        {
            if (!Directory.Exists(pasta)) return;
            foreach (var arquivo in Directory.GetFiles(pasta))
            {
                if (Path.GetFileName(arquivo).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                string nomeArquivo = Path.GetFileName(arquivo);
                lista.Add(new ItemInicializacao(origem, Path.GetFileNameWithoutExtension(arquivo), arquivo, arquivo,
                    "", EstaLigado(origem, nomeArquivo)));
            }
        }
        catch { }
    }

    // "C:\Program Files\App\app.exe" --minimizado  ->  C:\Program Files\App\app.exe
    public static string ExtrairExecutavel(string comando)
    {
        comando = Environment.ExpandEnvironmentVariables(comando.Trim());
        if (comando.StartsWith('"'))
        {
            int fim = comando.IndexOf('"', 1);
            return fim > 0 ? comando[1..fim] : comando.Trim('"');
        }
        int exe = comando.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? comando[..(exe + 4)] : comando.Split(' ')[0];
    }

    static string NomeExibicao(string nomeValor, string exe)
    {
        try
        {
            if (File.Exists(exe))
            {
                var descricao = FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (!string.IsNullOrWhiteSpace(descricao) && descricao.Length < 60) return descricao.Trim();
            }
        }
        catch { }
        return nomeValor;
    }

    static string Fabricante(string exe)
    {
        try { return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? "" : ""; }
        catch { return ""; }
    }

    // ---------------- Ligado / desligado (StartupApproved) ----------------

    static (RegistryKey Raiz, string Subchave) ChaveAprovacao(OrigemInicializacao origem) => origem switch
    {
        OrigemInicializacao.RegistroUsuario => (Registry.CurrentUser, Aprovados + @"\Run"),
        OrigemInicializacao.RegistroComputador => (Registry.LocalMachine, Aprovados + @"\Run"),
        OrigemInicializacao.RegistroComputador32 => (Registry.LocalMachine, Aprovados + @"\Run32"),
        OrigemInicializacao.PastaUsuario => (Registry.CurrentUser, Aprovados + @"\StartupFolder"),
        _ => (Registry.LocalMachine, Aprovados + @"\StartupFolder")
    };

    // Sem marcação = ligado. 1º byte par (02, 06) = ligado; ímpar (03, 07) = desligado
    static bool EstaLigado(OrigemInicializacao origem, string nome)
    {
        try
        {
            var (raiz, subchave) = ChaveAprovacao(origem);
            using var chave = raiz.OpenSubKey(subchave);
            return chave?.GetValue(nome) is not byte[] dados || dados.Length == 0 || (dados[0] & 1) == 0;
        }
        catch { return true; }
    }

    // Grava a marcação; devolve false se precisar de administrador
    public static bool Definir(OrigemInicializacao origem, string nomeValor, bool ligado)
    {
        try
        {
            var (raiz, subchave) = ChaveAprovacao(origem);
            using var chave = raiz.CreateSubKey(subchave, writable: true);
            var dados = new byte[12];
            dados[0] = (byte)(ligado ? 0x02 : 0x03);
            if (!ligado) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(dados, 4);   // quando foi desligado
            chave.SetValue(nomeValor, dados, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    // Nome do valor no registro ou do arquivo na pasta (é a chave usada pela marcação)
    public static string ChaveDoItem(ItemInicializacao item) =>
        item.Origem is OrigemInicializacao.PastaUsuario or OrigemInicializacao.PastaComputador
            ? Path.GetFileName(item.Comando)
            : NomeValor(item);

    static string NomeValor(ItemInicializacao item)
    {
        // O nome exibido pode ter vindo da descrição do .exe; procura o nome real do valor
        var raiz = item.Origem == OrigemInicializacao.RegistroUsuario ? Registry.CurrentUser : Registry.LocalMachine;
        string caminho = item.Origem == OrigemInicializacao.RegistroComputador32 ? Run32 : Run;
        try
        {
            using var chave = raiz.OpenSubKey(caminho);
            foreach (var nome in chave?.GetValueNames() ?? Array.Empty<string>())
                if (chave!.GetValue(nome) as string == item.Comando) return nome;
        }
        catch { }
        return item.Nome;
    }

    // Usado pelo programa rodando como administrador: /inicializacao <origem> <on|off> <nome>
    public static int ExecutarComoAdministrador(string[] args)
    {
        try
        {
            int i = Array.FindIndex(args, a => a.Equals("/inicializacao", StringComparison.OrdinalIgnoreCase));
            var origem = Enum.Parse<OrigemInicializacao>(args[i + 1]);
            bool ligado = args[i + 2] == "on";
            string nome = string.Join(" ", args.Skip(i + 3));
            return Definir(origem, nome, ligado) ? 0 : 1;
        }
        catch { return 1; }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ManutencaoImpressora.Services;

public enum AcaoEnergia { Desligar, Reiniciar, Suspender, Hibernar }
public enum ModoEnergia { Economia, Equilibrado, Desempenho }

// ---------------------------------------------------------------
//  Controles de energia do Windows:
//   - "manter acordado": avisa o Windows que o PC está em uso
//     (SetThreadExecutionState), enquanto o programa estiver aberto
//   - desligar / reiniciar / suspender / hibernar
//   - modo de energia do Windows 11 (economia / equilibrado / desempenho)
//   - tempos atuais de desligar a tela e suspender (powercfg)
// ---------------------------------------------------------------
public static partial class Energia
{
    // ---------------- Manter acordado ----------------

    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001, ES_DISPLAY_REQUIRED = 0x00000002;

    public static void ManterAcordado(bool ligado, bool manterTela)
    {
        uint estado = ES_CONTINUOUS;
        if (ligado) estado |= ES_SYSTEM_REQUIRED | (manterTela ? ES_DISPLAY_REQUIRED : 0);
        SetThreadExecutionState(estado);
    }

    // ---------------- Ações ----------------

    public static void Executar(AcaoEnergia acao)
    {
        switch (acao)
        {
            case AcaoEnergia.Desligar: Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            case AcaoEnergia.Reiniciar: Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            case AcaoEnergia.Suspender: SetSuspendState(false, false, false); break;
            case AcaoEnergia.Hibernar: SetSuspendState(true, false, false); break;
        }
    }

    public static string Verbo(AcaoEnergia acao) => acao switch
    {
        AcaoEnergia.Desligar => "desligar",
        AcaoEnergia.Reiniciar => "reiniciar",
        AcaoEnergia.Suspender => "suspender",
        _ => "hibernar"
    };

    // ---------------- Modo de energia (Windows 10/11) ----------------

    static readonly Guid GuidEconomia = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    static readonly Guid GuidDesempenho = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static ModoEnergia? ModoAtual()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var guid) != 0) return null;
            if (guid == GuidEconomia) return ModoEnergia.Economia;
            if (guid == GuidDesempenho) return ModoEnergia.Desempenho;
            return ModoEnergia.Equilibrado;
        }
        catch { return null; }   // Windows sem esse recurso
    }

    public static bool DefinirModo(ModoEnergia modo)
    {
        try
        {
            var guid = modo switch { ModoEnergia.Economia => GuidEconomia, ModoEnergia.Desempenho => GuidDesempenho, _ => Guid.Empty };
            return PowerSetActiveOverlayScheme(guid) == 0;
        }
        catch { return false; }
    }

    // ---------------- Tempos atuais (powercfg) ----------------

    [GeneratedRegex("0x([0-9a-fA-F]{8})")]
    private static partial Regex Hexadecimal();

    // Minutos na tomada para desligar a tela e suspender (0 = nunca)
    public static (int? Tela, int? Suspender) TemposAtuais() =>
        (LerMinutos("SUB_VIDEO VIDEOIDLE"), LerMinutos("SUB_SLEEP STANDBYIDLE"));

    static int? LerMinutos(string configuracao)
    {
        try
        {
            using var processo = Process.Start(new ProcessStartInfo("powercfg", $"/q SCHEME_CURRENT {configuracao}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (processo is null) return null;
            string saida = processo.StandardOutput.ReadToEnd();
            processo.WaitForExit(3000);
            // A saída vem no idioma do Windows; os valores são os dois últimos números hexadecimais (tomada e bateria)
            var valores = Hexadecimal().Matches(saida).Select(m => Convert.ToInt32(m.Groups[1].Value, 16)).ToList();
            if (valores.Count < 2) return null;
            return valores[^2] / 60;
        }
        catch { return null; }
    }

    // ---------------- APIs do Windows ----------------

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint estado);

    [DllImport("powrprof.dll", SetLastError = true)]
    static extern bool SetSuspendState(bool hibernar, bool forcar, bool desativarEventosDeDespertar);

    [DllImport("powrprof.dll")]
    static extern uint PowerGetEffectiveOverlayScheme(out Guid guid);

    [DllImport("powrprof.dll")]
    static extern uint PowerSetActiveOverlayScheme(Guid guid);
}

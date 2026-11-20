using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace ManutencaoImpressora.Services;

public record PlacaVideo(string Nome, string? Driver, double? Uso, double? Temperatura,
    double? MemoriaUsadaMb, double? MemoriaTotalMb, double? Ventoinha, double? Watts);

// ---------------------------------------------------------------
//  Placa de vídeo para a "Saúde do PC".
//   - NVIDIA (GTX/RTX): usa o nvidia-smi, que vem junto com o driver,
//     e traz uso, temperatura, memória, ventoinha e consumo
//   - outras marcas: nome pelo WMI e uso/memória pelos contadores do
//     Windows (os mesmos do Gerenciador de Tarefas); sem temperatura
// ---------------------------------------------------------------
public sealed class InfoPlacaVideo : IDisposable
{
    readonly string? _nvidiaSmi = AcharNvidiaSmi();
    string? _nomeWmi, _driverWmi;
    bool _wmiLido;

    PerformanceCounter[] _contadoresUso = Array.Empty<PerformanceCounter>();
    PerformanceCounter[] _contadoresMemoria = Array.Empty<PerformanceCounter>();
    DateTime _contadoresCriados = DateTime.MinValue;

    public bool TemNvidia => _nvidiaSmi != null;

    static string? AcharNvidiaSmi()
    {
        foreach (var caminho in new[]
                 {
                     Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
                 })
            if (File.Exists(caminho)) return caminho;
        return null;
    }

    // Leitura completa (leva alguns milissegundos: chame fora da tela, com Task.Run)
    public PlacaVideo? Ler()
    {
        if (_nvidiaSmi != null && LerNvidia() is PlacaVideo nvidia) return nvidia;
        return LerGenerica();
    }

    PlacaVideo? LerNvidia()
    {
        try
        {
            using var processo = Process.Start(new ProcessStartInfo(_nvidiaSmi!,
                "--query-gpu=name,driver_version,utilization.gpu,temperature.gpu,memory.used,memory.total,fan.speed,power.draw " +
                "--format=csv,noheader,nounits")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (processo is null) return null;
            string linha = processo.StandardOutput.ReadLine() ?? "";
            processo.WaitForExit(3000);
            var p = linha.Split(',').Select(c => c.Trim()).ToArray();
            if (p.Length < 8) return null;
            static double? Numero(string s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;   // "[N/A]" vira nulo
            return new PlacaVideo(p[0], p[1], Numero(p[2]), Numero(p[3]), Numero(p[4]), Numero(p[5]), Numero(p[6]), Numero(p[7]));
        }
        catch { return null; }
    }

    PlacaVideo? LerGenerica()
    {
        if (!_wmiLido)
        {
            _wmiLido = true;
            try
            {
                using var busca = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
                foreach (ManagementObject placa in busca.Get())
                {
                    using (placa)
                    {
                        string nome = placa["Name"] as string ?? "";
                        // Prefere a placa dedicada quando há duas (ex.: Intel + NVIDIA no notebook)
                        if (_nomeWmi is null || nome.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || nome.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        {
                            _nomeWmi = nome;
                            _driverWmi = placa["DriverVersion"] as string;
                        }
                    }
                }
            }
            catch { }
        }
        if (_nomeWmi is null) return null;

        double? uso = null, memoriaMb = null;
        try
        {
            // A lista de "instâncias" muda quando programas abrem e fecham: renova a cada 30 s
            if (DateTime.Now - _contadoresCriados > TimeSpan.FromSeconds(30))
            {
                DescartarContadores();
                var motores = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n => n.EndsWith("engtype_3D"));
                _contadoresUso = motores.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, readOnly: true)).ToArray();
                _contadoresMemoria = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames()
                    .Select(n => new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", n, readOnly: true)).ToArray();
                foreach (var c in _contadoresUso) c.NextValue();   // a 1ª leitura sempre é zero
                _contadoresCriados = DateTime.Now;
            }
            uso = Math.Min(100, _contadoresUso.Sum(c => { try { return c.NextValue(); } catch { return 0f; } }));
            memoriaMb = _contadoresMemoria.Select(c => { try { return c.NextValue(); } catch { return 0f; } }).DefaultIfEmpty(0).Max() / 1024 / 1024;
        }
        catch { /* contadores indisponíveis */ }

        return new PlacaVideo(_nomeWmi, _driverWmi, uso, null, memoriaMb, null, null, null);
    }

    void DescartarContadores()
    {
        foreach (var c in _contadoresUso.Concat(_contadoresMemoria)) c.Dispose();
        _contadoresUso = _contadoresMemoria = Array.Empty<PerformanceCounter>();
    }

    public void Dispose() => DescartarContadores();
}

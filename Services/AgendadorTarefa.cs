using System.Diagnostics;
using System.Security;
using System.Text;
using ManutencaoImpressora.Models;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Cria/remove a tarefa no Agendador de Tarefas do Windows
//  (usa XML, assim não depende do formato de data do Windows)
// ---------------------------------------------------------------
public class AgendadorTarefa
{
    readonly string _nomeTarefa;
    readonly string _pastaDados;

    public AgendadorTarefa(string nomeTarefa, string pastaDados)
    {
        _nomeTarefa = nomeTarefa;
        _pastaDados = pastaDados;
    }

    public void Criar(Config c)
    {
        string exe = SecurityElement.Escape(Environment.ProcessPath!);
        string usuario = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string inicio = (c.Inicio.Date + c.Hora).ToString("yyyy-MM-ddTHH:mm:ss");
        string fim = c.TemFim
            ? $"<EndBoundary>{c.Fim.Date.AddDays(1).AddSeconds(-1):yyyy-MM-ddTHH:mm:ss}</EndBoundary>"
            : "";
        string dias = string.Concat(c.Dias.OrderBy(d => d).Select(d => $"<{d} />"));

        string xml = $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Imprime uma página colorida para a tinta da impressora jato de tinta não secar.</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>{inicio}</StartBoundary>
      {fim}
      <Enabled>true</Enabled>
      <ScheduleByWeek>
        <DaysOfWeek>{dias}</DaysOfWeek>
        <WeeksInterval>{c.IntervaloSemanas}</WeeksInterval>
      </ScheduleByWeek>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{usuario}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{exe}</Command>
      <Arguments>/imprimir</Arguments>
    </Exec>
  </Actions>
</Task>
""";

        string arquivoXml = Path.Combine(_pastaDados, "tarefa.xml");
        File.WriteAllText(arquivoXml, xml, Encoding.Unicode);

        var (codigo, saida) = Schtasks($"/create /tn \"{_nomeTarefa}\" /xml \"{arquivoXml}\" /f");
        if (codigo != 0)
            throw new Exception("O Windows não aceitou o agendamento:\n" + saida);
    }

    public void Remover() => Schtasks($"/delete /tn \"{_nomeTarefa}\" /f");

    public bool Existe() => Schtasks($"/query /tn \"{_nomeTarefa}\"").codigo == 0;

    static (int codigo, string saida) Schtasks(string argumentos)
    {
        var psi = new ProcessStartInfo("schtasks.exe", argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        string saida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, saida.Trim());
    }
}

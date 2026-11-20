namespace ManutencaoImpressora.Models;

// Uma queda da internet registrada pelo monitor (Fim nulo = ainda sem internet)
public record Queda(long Id, DateTime Inicio, DateTime? Fim, string Motivo)
{
    public TimeSpan Duracao(DateTime agora) => (Fim ?? agora) - Inicio;
}

// Ping somado por minuto: Soma/Respostas = média; Perdas = medições sem resposta
public record MinutoLatencia(DateTime Minuto, double Soma, double Maximo, int Respostas, int Perdas)
{
    public double? Media => Respostas > 0 ? Soma / Respostas : null;
}

// Resultado salvo de um teste de velocidade (Mbps e ms)
public record TesteSalvo(DateTime Momento, double Download, double Upload, double Ping, double Jitter);

// Aparelho já visto na rede local
public record DispositivoConhecido(string Mac, string? Apelido, string? Nome, string? Fabricante,
    string? UltimoIp, DateTime PrimeiraVez, DateTime UltimaVez);

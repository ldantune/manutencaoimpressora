namespace ManutencaoImpressora.Services;

// Textos de tempo para a tela: "3 min 10 s", "há 2 h", "ontem às 14:32"...
public static class FormatoTempo
{
    public static string Duracao(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds} s";
        if (t.TotalMinutes < 60) return t.Seconds > 0 && t.TotalMinutes < 10 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{(int)t.TotalMinutes} min";
        if (t.TotalHours < 24) return t.Minutes > 0 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalHours} h";
        return $"{(int)t.TotalDays} d {t.Hours} h";
    }

    public static string Quando(DateTime momento)
    {
        var hoje = DateTime.Today;
        if (momento.Date == hoje) return $"hoje às {momento:HH:mm}";
        if (momento.Date == hoje.AddDays(-1)) return $"ontem às {momento:HH:mm}";
        return $"{momento:ddd, dd/MM} às {momento:HH:mm}";
    }

    public static string Relativo(DateTime momento)
    {
        var passado = DateTime.Now - momento;
        if (passado.TotalMinutes < 2) return "agora";
        if (passado.TotalMinutes < 60) return $"há {(int)passado.TotalMinutes} min";
        if (passado.TotalHours < 24 && momento.Date == DateTime.Today) return $"há {(int)passado.TotalHours} h";
        return Quando(momento);
    }
}

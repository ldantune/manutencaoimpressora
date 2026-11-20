namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Formata quantidades de bytes para leitura (ex.: 1,2 GB)
// ---------------------------------------------------------------
public static class FormatoBytes
{
    public const double KB = 1024, MB = KB * 1024, GB = MB * 1024, TB = GB * 1024;

    public static string Formatar(double bytes)
    {
        if (bytes >= TB) return $"{bytes / TB:0.00} TB";
        if (bytes >= GB * 100) return $"{bytes / GB:0} GB";
        if (bytes >= GB) return $"{bytes / GB:0.0} GB";
        if (bytes >= MB) return $"{bytes / MB:0} MB";
        if (bytes >= KB) return $"{bytes / KB:0} KB";
        return $"{bytes:0} B";
    }
}

namespace VECLauncher.Installer.Models;

public enum InstallStage
{
    Idle,
    CheckingUpdate,
    Downloading,
    Extracting,
    CreatingShortcuts,
    Registering,
    Finalizing,
    Completed,
    Error,
    Uninstalling
}

public sealed class InstallProgressReport
{
    public InstallStage Stage { get; set; } = InstallStage.Idle;
    public string StatusMessage { get; set; } = "";
    public double Percent { get; set; }
    public string SpeedFormatted { get; set; } = "";
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string? ErrorMessage { get; set; }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "Б", "КБ", "МБ", "ГБ" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }
}

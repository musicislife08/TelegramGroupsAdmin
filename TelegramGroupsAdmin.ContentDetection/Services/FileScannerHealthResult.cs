namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Health check result for file scanner services.
/// </summary>
public class FileScannerHealthResult
{
    public bool IsHealthy { get; set; }
    public string? Version { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? ErrorMessage { get; set; }
}

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// ClamAV health check result (Phase 4.22)
/// </summary>
public class ClamAVHealthResult
{
    public bool IsHealthy { get; set; }
    public string? Version { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? ErrorMessage { get; set; }
}

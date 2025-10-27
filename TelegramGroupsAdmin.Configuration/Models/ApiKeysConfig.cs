namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// API keys for external services used in file scanning
/// Stored encrypted in configs.api_keys JSONB column with [ProtectedData] attribute
/// Backup system automatically decrypts during export and re-encrypts during restore
/// </summary>
public class ApiKeysConfig
{
    /// <summary>
    /// VirusTotal API key (https://www.virustotal.com/gui/my-apikey)
    /// Required for cloud file scanning with 70+ antivirus engines
    /// Free tier: 500 requests/day, 4 requests/minute
    /// </summary>
    public string? VirusTotal { get; set; }

    /// <summary>
    /// MetaDefender API key (https://metadefender.opswat.com)
    /// Optional cloud scanner with 30+ engines
    /// Free tier: 40 requests/day
    /// </summary>
    public string? MetaDefender { get; set; }

    /// <summary>
    /// Hybrid Analysis API key (https://www.hybrid-analysis.com)
    /// Optional sandbox analysis service
    /// Free tier: 30 requests/month
    /// </summary>
    public string? HybridAnalysis { get; set; }

    /// <summary>
    /// Intezer API key (https://analyze.intezer.com)
    /// Optional genetic malware analysis service
    /// Free tier: 10 requests/month
    /// </summary>
    public string? Intezer { get; set; }

    /// <summary>
    /// Returns true if at least one API key is configured
    /// </summary>
    public bool HasAnyKey()
    {
        return !string.IsNullOrWhiteSpace(VirusTotal) ||
               !string.IsNullOrWhiteSpace(MetaDefender) ||
               !string.IsNullOrWhiteSpace(HybridAnalysis) ||
               !string.IsNullOrWhiteSpace(Intezer);
    }
}

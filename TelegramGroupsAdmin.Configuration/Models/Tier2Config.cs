namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Tier 2: Cloud scanner configuration
/// </summary>
public class Tier2Config
{
    /// <summary>
    /// User-configurable priority order for cloud services
    /// Services are tried in this order until one succeeds
    /// </summary>
    public List<string> CloudQueuePriority { get; set; } = new() { "VirusTotal", "MetaDefender", "HybridAnalysis", "Intezer" };

    /// <summary>
    /// VirusTotal configuration
    /// </summary>
    public VirusTotalConfig VirusTotal { get; set; } = new();

    /// <summary>
    /// MetaDefender configuration
    /// </summary>
    public MetaDefenderConfig MetaDefender { get; set; } = new();

    /// <summary>
    /// Hybrid Analysis configuration
    /// </summary>
    public HybridAnalysisConfig HybridAnalysis { get; set; } = new();

    /// <summary>
    /// Intezer configuration
    /// </summary>
    public IntezerConfig Intezer { get; set; } = new();

    /// <summary>
    /// Fail-open when all cloud services exhausted
    /// true = allow file through, false = block file
    /// </summary>
    public bool FailOpenWhenExhausted { get; set; } = true;
}

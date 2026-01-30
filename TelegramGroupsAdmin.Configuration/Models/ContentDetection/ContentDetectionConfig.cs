using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// Configuration for content detection, based on tg-spam's configuration structure
/// </summary>
public class ContentDetectionConfig
{
    /// <summary>
    /// Enable auto-whitelisting after users prove themselves with non-spam messages.
    /// When enabled, users who post N consecutive non-spam messages are automatically
    /// whitelisted and skip all future spam checks (global trust).
    /// Trust is revoked if spam detected (e.g., compromised account, manual /spam).
    /// </summary>
    public bool FirstMessageOnly { get; set; } = true;

    /// <summary>
    /// Number of consecutive non-spam messages required before auto-whitelisting.
    /// Default: 3 (protects against "post innocent → edit to spam" tactic)
    /// Users are fully spam-checked until threshold reached, then globally trusted.
    /// </summary>
    public int FirstMessagesCount { get; set; } = 3;

    /// <summary>
    /// Minimum message length for messages to count toward auto-trust.
    /// Prevents trust gaming with short agreeable replies ("wow", "yeah", "amazing").
    /// Default: 20 chars (same as spam detection minimum)
    /// </summary>
    public int AutoTrustMinMessageLength { get; set; } = 20;

    /// <summary>
    /// Minimum account age (hours since first_seen) before auto-trust can activate.
    /// Prevents quick hit-and-run spam attacks. Default: 24 hours.
    /// Both this AND message count must be satisfied.
    /// Set to 0 to disable the account age check entirely.
    /// </summary>
    public int AutoTrustMinAccountAgeHours { get; set; } = 24;

    /// <summary>
    /// Minimum message length to trigger expensive checks (similarity, Bayes)
    /// </summary>
    public int MinMessageLength { get; set; } = 10;

    /// <summary>
    /// Auto-ban threshold (confidence >= this value)
    /// </summary>
    public int AutoBanThreshold { get; set; } = 80;

    /// <summary>
    /// Review queue threshold (confidence >= this value but < auto-ban)
    /// </summary>
    public int ReviewQueueThreshold { get; set; } = 50;

    /// <summary>
    /// Maximum individual check confidence to trigger OpenAI veto (0-100)
    /// Veto runs if: (NetConfidence > ReviewQueueThreshold) OR (MaxConfidence > this value)
    /// Default: 85 (catches high-confidence outliers that might be outvoted)
    /// </summary>
    public int MaxConfidenceVetoThreshold { get; set; } = 85;

    /// <summary>
    /// Training mode - forces all spam detections into review queue instead of auto-banning
    /// Use this to validate spam detection is working correctly before enabling auto-ban
    /// </summary>
    public bool TrainingMode { get; set; } = false;

    /// <summary>
    /// Stop words configuration
    /// </summary>
    public StopWordsConfig StopWords { get; set; } = new();

    /// <summary>
    /// Similarity check configuration
    /// </summary>
    public SimilarityConfig Similarity { get; set; } = new();

    /// <summary>
    /// Naive Bayes classifier configuration
    /// </summary>
    public BayesConfig Bayes { get; set; } = new();

    /// <summary>
    /// Invisible character detection configuration
    /// </summary>
    public InvisibleCharsConfig InvisibleChars { get; set; } = new();

    /// <summary>
    /// Foreign language translation configuration
    /// </summary>
    public TranslationConfig Translation { get; set; } = new();

    /// <summary>
    /// Abnormal spacing detection configuration
    /// </summary>
    public SpacingConfig Spacing { get; set; } = new();

    /// <summary>
    /// AI veto configuration - AI always runs as veto to confirm/override spam detection
    /// JSON key kept as "openAI" for database compatibility (existing JSONB data)
    /// </summary>
    [JsonPropertyName("openAI")]
    public AIVetoConfig AIVeto { get; set; } = new();

    /// <summary>
    /// URL blocklist checking configuration
    /// </summary>
    public UrlBlocklistConfig UrlBlocklist { get; set; } = new();

    /// <summary>
    /// Threat intelligence (VirusTotal, Google Safe Browsing) configuration
    /// </summary>
    public ThreatIntelConfig ThreatIntel { get; set; } = new();

    /// <summary>
    /// SEO scraping configuration
    /// </summary>
    public SeoScrapingConfig SeoScraping { get; set; } = new();

    /// <summary>
    /// Image content detection configuration
    /// </summary>
    public ImageContentConfig ImageSpam { get; set; } = new();

    /// <summary>
    /// Video content detection configuration (ML-6)
    /// </summary>
    public VideoContentConfig VideoSpam { get; set; } = new();

    /// <summary>
    /// File scanning detection configuration (detection flags only).
    /// Infrastructure settings (ClamAV, VirusTotal connection) remain in FileScanningConfig.
    /// </summary>
    public FileScanningDetectionConfig FileScanning { get; set; } = new();
}

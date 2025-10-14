namespace TelegramGroupsAdmin.SpamDetection.Configuration;

/// <summary>
/// Configuration for spam detection, based on tg-spam's configuration structure
/// </summary>
public class SpamDetectionConfig
{
    /// <summary>
    /// Only check first N messages from each user
    /// </summary>
    public bool FirstMessageOnly { get; set; } = false;

    /// <summary>
    /// Number of first messages to check if FirstMessageOnly is true
    /// </summary>
    public int FirstMessagesCount { get; set; } = 1;

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
    /// CAS (Combot Anti-Spam) configuration
    /// </summary>
    public CasConfig Cas { get; set; } = new();

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
    /// OpenAI integration configuration
    /// </summary>
    public OpenAIConfig OpenAI { get; set; } = new();

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
    /// Image spam detection configuration
    /// </summary>
    public ImageSpamConfig ImageSpam { get; set; } = new();
}

/// <summary>
/// Stop words check configuration
/// </summary>
public class StopWordsConfig
{
    /// <summary>
    /// Whether stop words check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 50;
}

/// <summary>
/// Spam similarity check configuration
/// </summary>
public class SimilarityConfig
{
    /// <summary>
    /// Whether similarity check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Similarity threshold (0.0 - 1.0)
    /// </summary>
    public double Threshold { get; set; } = 0.5;
}

/// <summary>
/// CAS (Combot Anti-Spam) configuration
/// </summary>
public class CasConfig
{
    /// <summary>
    /// Whether CAS check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CAS API URL
    /// </summary>
    public string ApiUrl { get; set; } = "https://api.cas.chat";

    /// <summary>
    /// HTTP timeout for CAS requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// User-Agent header for CAS requests
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Naive Bayes classifier configuration
/// </summary>
public class BayesConfig
{
    /// <summary>
    /// Whether Bayes check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum spam probability to trigger (0.0 - 100.0)
    /// </summary>
    public double MinSpamProbability { get; set; } = 50.0;
}

/// <summary>
/// Invisible character detection configuration
/// </summary>
public class InvisibleCharsConfig
{
    /// <summary>
    /// Whether invisible character detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Foreign language translation configuration
/// </summary>
public class TranslationConfig
{
    /// <summary>
    /// Whether translation is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to run spam checks on translated content
    /// </summary>
    public bool CheckTranslatedContent { get; set; } = true;
}

/// <summary>
/// Abnormal spacing detection configuration
/// </summary>
public class SpacingConfig
{
    /// <summary>
    /// Whether spacing check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum number of words required for spacing analysis
    /// </summary>
    public int MinWordsCount { get; set; } = 5;

    /// <summary>
    /// Maximum length for "short" words
    /// </summary>
    public int ShortWordLength { get; set; } = 3;

    /// <summary>
    /// Threshold for short word ratio (0.0 - 1.0)
    /// </summary>
    public double ShortWordRatioThreshold { get; set; } = 0.7;

    /// <summary>
    /// Threshold for space ratio (0.0 - 1.0)
    /// </summary>
    public double SpaceRatioThreshold { get; set; } = 0.3;
}

/// <summary>
/// OpenAI integration configuration
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// Whether OpenAI check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether OpenAI runs in veto mode (confirm spam) or enhancement mode (find spam)
    /// </summary>
    public bool VetoMode { get; set; } = true;

    /// <summary>
    /// Confidence threshold for triggering OpenAI veto (0-100)
    /// OpenAI veto only runs if spam is detected with confidence below this threshold
    /// Higher values = more vetos (more conservative), Lower values = fewer vetos (more aggressive)
    /// </summary>
    public int VetoThreshold { get; set; } = 95;

    /// <summary>
    /// Whether to check short messages with OpenAI
    /// </summary>
    public bool CheckShortMessages { get; set; } = false;

    /// <summary>
    /// Custom system prompt for this group (topic-specific)
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// URL blocklist checking configuration
/// </summary>
public class UrlBlocklistConfig
{
    /// <summary>
    /// Whether URL blocklist checking is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache duration for blocklist entries
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Threat intelligence configuration
/// </summary>
public class ThreatIntelConfig
{
    /// <summary>
    /// Whether threat intelligence checking is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use VirusTotal (disabled by default - slow for URL checks, better for file scanning)
    /// </summary>
    public bool UseVirusTotal { get; set; } = false;

    /// <summary>
    /// Timeout for threat intel API calls
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// SEO scraping configuration
/// </summary>
public class SeoScrapingConfig
{
    /// <summary>
    /// Whether SEO scraping is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout for SEO scraping requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Image spam detection configuration
/// </summary>
public class ImageSpamConfig
{
    /// <summary>
    /// Whether image spam detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use OpenAI Vision for image analysis
    /// </summary>
    public bool UseOpenAIVision { get; set; } = true;

    /// <summary>
    /// Timeout for image analysis requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
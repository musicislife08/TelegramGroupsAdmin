namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ContentDetectionConfig for EF Core JSON column mapping.
/// Root config containing all content detection algorithm sub-configs.
/// </summary>
public class ContentDetectionConfigData
{
    public bool FirstMessageOnly { get; set; } = true;

    public int FirstMessagesCount { get; set; } = 3;

    public int AutoTrustMinMessageLength { get; set; } = 20;

    public int AutoTrustMinAccountAgeHours { get; set; } = 24;

    public int MinMessageLength { get; set; } = 10;

    public int AutoBanThreshold { get; set; } = 80;

    public int ReviewQueueThreshold { get; set; } = 50;

    public int MaxConfidenceVetoThreshold { get; set; } = 85;

    public bool TrainingMode { get; set; }

    public StopWordsConfigData StopWords { get; set; } = new();

    public SimilarityConfigData Similarity { get; set; } = new();

    public BayesConfigData Bayes { get; set; } = new();

    public InvisibleCharsConfigData InvisibleChars { get; set; } = new();

    public TranslationConfigData Translation { get; set; } = new();

    public SpacingConfigData Spacing { get; set; } = new();

    public AIVetoConfigData AIVeto { get; set; } = new();

    public UrlBlocklistConfigData UrlBlocklist { get; set; } = new();

    public ThreatIntelConfigData ThreatIntel { get; set; } = new();

    public SeoScrapingConfigData SeoScraping { get; set; } = new();

    public ImageContentConfigData ImageSpam { get; set; } = new();

    public VideoContentConfigData VideoSpam { get; set; } = new();

    public FileScanningDetectionConfigData FileScanning { get; set; } = new();
}

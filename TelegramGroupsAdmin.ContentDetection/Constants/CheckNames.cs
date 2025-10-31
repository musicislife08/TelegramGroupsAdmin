namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Type-safe check name enum
/// Prevents magic string typos and enables exhaustive switch checking
/// </summary>
public enum CheckName
{
    StopWords,
    CAS,
    Similarity,
    Bayes,
    Spacing,
    InvisibleChars,
    OpenAI,
    ThreatIntel,
    UrlBlocklist,
    SeoScraping,
    ImageSpam,
    VideoSpam,
    FileScanning
}

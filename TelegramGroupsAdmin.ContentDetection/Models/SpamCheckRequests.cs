namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Base class for all content check requests
/// Contains common properties needed by all checks
/// </summary>
public abstract class ContentCheckRequestBase
{
    public required string Message { get; init; }
    public required long UserId { get; init; }
    public required string? UserName { get; init; }
    public required long ChatId { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Request for StopWords spam check
/// </summary>
public sealed class StopWordsCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for Bayes spam check
/// </summary>
public sealed class BayesCheckRequest : ContentCheckRequestBase
{
    public required int MinMessageLength { get; init; }
    public required int MinSpamProbability { get; init; }
}

/// <summary>
/// Request for CAS (Combot Anti-Spam) check
/// </summary>
public sealed class CasCheckRequest : ContentCheckRequestBase
{
    public required string ApiUrl { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required string? UserAgent { get; init; }
}

/// <summary>
/// Request for Similarity (TF-IDF) spam check
/// </summary>
public sealed class SimilarityCheckRequest : ContentCheckRequestBase
{
    public required int MinMessageLength { get; init; }
    public required double SimilarityThreshold { get; init; }
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for Spacing/InvisibleChars spam check
/// </summary>
public sealed class SpacingCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
    public required double SuspiciousRatioThreshold { get; init; }
}

/// <summary>
/// Request for InvisibleChars spam check
/// </summary>
public sealed class InvisibleCharsCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for OpenAI spam check
/// </summary>
public sealed class OpenAICheckRequest : ContentCheckRequestBase
{
    public required bool VetoMode { get; init; }
    public required string? SystemPrompt { get; init; }
    public required bool HasSpamFlags { get; init; }
    public required int MinMessageLength { get; init; }
    public required bool CheckShortMessages { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
}

/// <summary>
/// Request for ThreatIntel spam check (VirusTotal, Safe Browsing)
/// </summary>
public sealed class ThreatIntelCheckRequest : ContentCheckRequestBase
{
    public required List<string> Urls { get; init; }
    public required string? VirusTotalApiKey { get; init; }
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for UrlBlocklist spam check
/// </summary>
public sealed class UrlBlocklistCheckRequest : ContentCheckRequestBase
{
    public required List<string> Urls { get; init; }
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for SeoScraping spam check
/// </summary>
public sealed class SeoScrapingCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
}

/// <summary>
/// Request for Image spam check (OpenAI Vision)
/// </summary>
public sealed class ImageCheckRequest : ContentCheckRequestBase
{
    public required string PhotoFileId { get; init; }
    public required string? PhotoUrl { get; init; }
    public required string? CustomPrompt { get; init; }
    public required int ConfidenceThreshold { get; init; }
    public required string ApiKey { get; init; }
}

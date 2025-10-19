using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Main content detection orchestrator that runs all configured checks
/// </summary>
public class ContentDetector : IContentDetector
{
    private readonly IEnumerable<IContentCheck> _contentChecks;
    private readonly ILogger<ContentDetector> _logger;
    private readonly SpamDetectionConfig _config;

    public ContentDetector(
        IEnumerable<IContentCheck> contentChecks,
        ILogger<ContentDetector> logger,
        SpamDetectionConfig config)
    {
        _contentChecks = contentChecks;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Execute all configured content checks for the given request
    /// LEGACY: This class is obsolete and no longer compatible with the new typed request system
    /// </summary>
    public Task<ContentCheckResult> CheckAsync(ContentCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Legacy ContentDetector is incompatible with new typed request system
        _logger.LogError("LEGACY CODE: ContentDetector should not be used. Use IContentDetectionEngine instead.");
        throw new NotSupportedException(
            "ContentDetector is a legacy class and cannot be used with the new typed request system. " +
            "Use IContentDetectionEngine instead.");
    }

    /// <summary>
    /// Get list of all configured checks for the given group
    /// </summary>
    public IEnumerable<string> GetConfiguredChecks(int groupId)
    {
        // For now, return all available checks
        // TODO: Implement per-group configuration
        return _contentChecks.Select(c => c.CheckName);
    }

    /// <summary>
    /// Calculate overall spam result from individual check responses
    /// </summary>
    private ContentCheckResult CalculateOverallResult(List<ContentCheckResponse> responses, List<int> allExtraDeleteIds)
    {
        if (!responses.Any())
        {
            return new ContentCheckResult
            {
                CheckResponses = responses,
                IsSpam = false,
                OverallConfidence = 0,
                RecommendedAction = SpamAction.Allow,
                Summary = "No checks executed",
                AllExtraDeleteIds = allExtraDeleteIds
            };
        }

        // Calculate weighted confidence score
        // For now, use simple maximum confidence from any check that flagged as spam
        var spamResponses = responses.Where(r => r.Result == CheckResultType.Spam && r.Error == null).ToList();
        var maxConfidence = spamResponses.Any() ? spamResponses.Max(r => r.Confidence) : 0;

        // Determine if overall result is spam
        var isSpam = maxConfidence >= _config.ReviewQueueThreshold;

        // Determine recommended action based on confidence thresholds
        var recommendedAction = SpamAction.Allow;
        if (maxConfidence >= _config.AutoBanThreshold)
        {
            recommendedAction = SpamAction.AutoBan;
        }
        else if (maxConfidence >= _config.ReviewQueueThreshold)
        {
            recommendedAction = SpamAction.ReviewQueue;
        }

        // Generate summary
        var summary = GenerateSummary(responses, maxConfidence, recommendedAction);

        return new ContentCheckResult
        {
            CheckResponses = responses,
            IsSpam = isSpam,
            OverallConfidence = maxConfidence,
            RecommendedAction = recommendedAction,
            Summary = summary,
            AllExtraDeleteIds = allExtraDeleteIds.Distinct().ToList()
        };
    }

    /// <summary>
    /// Generate human-readable summary of the detection result
    /// </summary>
    private static string GenerateSummary(List<ContentCheckResponse> responses, int maxConfidence, SpamAction action)
    {
        var spamChecks = responses.Where(r => r.Result == CheckResultType.Spam && r.Error == null).ToList();
        var errorChecks = responses.Where(r => r.Error != null).ToList();

        if (!spamChecks.Any())
        {
            var checkCount = responses.Count - errorChecks.Count;
            return $"Message appears clean (checked by {checkCount} algorithms)";
        }

        var triggeringCheck = spamChecks.OrderByDescending(r => r.Confidence).First();
        var summary = $"{triggeringCheck.CheckName}: {triggeringCheck.Details}";

        if (spamChecks.Count > 1)
        {
            summary += $" (+{spamChecks.Count - 1} other checks)";
        }

        if (errorChecks.Any())
        {
            summary += $" ({errorChecks.Count} checks failed)";
        }

        return $"{summary} (confidence: {maxConfidence}%)";
    }
}
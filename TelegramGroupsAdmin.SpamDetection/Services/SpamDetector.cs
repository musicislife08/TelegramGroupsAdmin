using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Services;

/// <summary>
/// Main spam detection orchestrator that runs all configured checks
/// </summary>
public class SpamDetector : ISpamDetector
{
    private readonly IEnumerable<ISpamCheck> _spamChecks;
    private readonly ILogger<SpamDetector> _logger;
    private readonly SpamDetectionConfig _config;

    public SpamDetector(
        IEnumerable<ISpamCheck> spamChecks,
        ILogger<SpamDetector> logger,
        SpamDetectionConfig config)
    {
        _spamChecks = spamChecks;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Execute all configured spam checks for the given request
    /// LEGACY: This class is obsolete and no longer compatible with the new typed request system
    /// </summary>
    public Task<SpamCheckResult> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        // Legacy SpamDetector is incompatible with new typed request system
        _logger.LogError("LEGACY CODE: SpamDetector should not be used. Use ISpamDetectionEngine instead.");
        throw new NotSupportedException(
            "SpamDetector is a legacy class and cannot be used with the new typed request system. " +
            "Use ISpamDetectionEngine instead.");
    }

    /// <summary>
    /// Get list of all configured checks for the given group
    /// </summary>
    public IEnumerable<string> GetConfiguredChecks(int groupId)
    {
        // For now, return all available checks
        // TODO: Implement per-group configuration
        return _spamChecks.Select(c => c.CheckName);
    }

    /// <summary>
    /// Calculate overall spam result from individual check responses
    /// </summary>
    private SpamCheckResult CalculateOverallResult(List<SpamCheckResponse> responses, List<int> allExtraDeleteIds)
    {
        if (!responses.Any())
        {
            return new SpamCheckResult
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
        var spamResponses = responses.Where(r => r.Result == SpamCheckResultType.Spam && r.Error == null).ToList();
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

        return new SpamCheckResult
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
    private static string GenerateSummary(List<SpamCheckResponse> responses, int maxConfidence, SpamAction action)
    {
        var spamChecks = responses.Where(r => r.Result == SpamCheckResultType.Spam && r.Error == null).ToList();
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
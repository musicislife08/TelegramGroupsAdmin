using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Services;

/// <summary>
/// Factory implementation that orchestrates all spam detection checks
/// </summary>
public class SpamDetectorFactory : ISpamDetectorFactory
{
    private readonly ILogger<SpamDetectorFactory> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly IEnumerable<ISpamCheck> _spamChecks;

    public SpamDetectorFactory(
        ILogger<SpamDetectorFactory> logger,
        SpamDetectionConfig config,
        IEnumerable<ISpamCheck> spamChecks)
    {
        _logger = logger;
        _config = config;
        _spamChecks = spamChecks;
    }

    /// <summary>
    /// Run all applicable spam checks on a message and return aggregated results
    /// </summary>
    public async Task<SpamDetectionResult> CheckMessageAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        var checkResults = new List<SpamCheckResponse>();

        _logger.LogDebug("Starting spam detection for user {UserId} in chat {ChatId}", request.UserId, request.ChatId);

        // First, run all non-OpenAI checks
        var nonOpenAIResult = await CheckMessageWithoutOpenAIAsync(request, cancellationToken);
        checkResults.AddRange(nonOpenAIResult.CheckResults);

        // Determine if we should run OpenAI veto check
        var shouldRunOpenAI = nonOpenAIResult.ShouldVeto && _config.OpenAI.Enabled && _config.OpenAI.VetoMode;

        if (shouldRunOpenAI)
        {
            var openAICheck = _spamChecks.FirstOrDefault(check => check.CheckName == "OpenAI");
            if (openAICheck != null)
            {
                _logger.LogDebug("Running OpenAI veto check for user {UserId}", request.UserId);

                // Update request to indicate other checks found spam
                var vetoRequest = request with { HasSpamFlags = true };

                if (openAICheck.ShouldExecute(vetoRequest))
                {
                    var vetoResult = await openAICheck.CheckAsync(vetoRequest, cancellationToken);
                    checkResults.Add(vetoResult);

                    // If OpenAI vetoes the spam detection, override the result
                    if (!vetoResult.IsSpam && vetoResult.Confidence == 0)
                    {
                        _logger.LogInformation("OpenAI vetoed spam detection for user {UserId}", request.UserId);
                        return CreateVetoedResult(checkResults, vetoResult);
                    }
                }
            }
        }

        return AggregateResults(checkResults);
    }

    /// <summary>
    /// Run only non-OpenAI checks to determine if message should be vetoed by OpenAI
    /// </summary>
    public async Task<SpamDetectionResult> CheckMessageWithoutOpenAIAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        var checkResults = new List<SpamCheckResponse>();

        // Run all checks except OpenAI
        var checks = _spamChecks.Where(check => check.CheckName != "OpenAI").ToList();

        foreach (var check in checks)
        {
            if (!check.ShouldExecute(request))
            {
                _logger.LogDebug("Skipping {CheckName} for user {UserId} - conditions not met", check.CheckName, request.UserId);
                continue;
            }

            try
            {
                _logger.LogDebug("Running {CheckName} for user {UserId}", check.CheckName, request.UserId);
                var result = await check.CheckAsync(request, cancellationToken);
                checkResults.Add(result);

                _logger.LogDebug("{CheckName} result: IsSpam={IsSpam}, Confidence={Confidence}",
                    check.CheckName, result.IsSpam, result.Confidence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running {CheckName} for user {UserId}", check.CheckName, request.UserId);
                // Continue with other checks
            }
        }

        return AggregateResults(checkResults);
    }

    /// <summary>
    /// Aggregate results from multiple spam checks
    /// </summary>
    private SpamDetectionResult AggregateResults(List<SpamCheckResponse> checkResults)
    {
        var spamResults = checkResults.Where(r => r.IsSpam).ToList();
        var isSpam = spamResults.Any();
        var spamFlags = spamResults.Count;

        var maxConfidence = isSpam ? spamResults.Max(r => r.Confidence) : 0;
        var avgConfidence = isSpam ? (int)spamResults.Average(r => r.Confidence) : 0;

        // Determine recommended action based on confidence thresholds
        var recommendedAction = DetermineAction(maxConfidence);

        // Primary reason is from the highest confidence check
        var primaryReason = isSpam
            ? spamResults.OrderByDescending(r => r.Confidence).First().Details
            : "No spam detected";

        // Should veto if spam detected but confidence is not extremely high
        var shouldVeto = isSpam && maxConfidence < 95 && _config.OpenAI.VetoMode;

        var result = new SpamDetectionResult
        {
            IsSpam = isSpam,
            MaxConfidence = maxConfidence,
            AvgConfidence = avgConfidence,
            SpamFlags = spamFlags,
            CheckResults = checkResults,
            PrimaryReason = primaryReason,
            RecommendedAction = recommendedAction,
            ShouldVeto = shouldVeto
        };

        _logger.LogDebug("Aggregated result: IsSpam={IsSpam}, MaxConfidence={MaxConfidence}, SpamFlags={SpamFlags}, Action={Action}",
            result.IsSpam, result.MaxConfidence, result.SpamFlags, result.RecommendedAction);

        return result;
    }

    /// <summary>
    /// Create result when OpenAI vetoes spam detection
    /// </summary>
    private SpamDetectionResult CreateVetoedResult(List<SpamCheckResponse> checkResults, SpamCheckResponse vetoResult)
    {
        return new SpamDetectionResult
        {
            IsSpam = false, // Vetoed
            MaxConfidence = 0,
            AvgConfidence = 0,
            SpamFlags = 0,
            CheckResults = checkResults,
            PrimaryReason = vetoResult.Details,
            RecommendedAction = SpamAction.Allow,
            ShouldVeto = false
        };
    }

    /// <summary>
    /// Determine recommended action based on confidence score
    /// </summary>
    private SpamAction DetermineAction(int confidence)
    {
        if (confidence >= _config.AutoBanThreshold)
        {
            return SpamAction.AutoBan;
        }
        if (confidence >= _config.ReviewQueueThreshold)
        {
            return SpamAction.ReviewQueue;
        }
        return SpamAction.Allow;
    }
}
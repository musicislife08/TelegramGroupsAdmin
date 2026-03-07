using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 Bayes check with proper abstention and SpamAssassin-style scoring.
/// Uses the Singleton <see cref="IBayesClassifierService"/> for classification
/// instead of training inline on every request.
/// Key behaviors:
/// - Abstain when classifier not trained (instead of Clean 0%)
/// - Abstain when uncertain (40-60% probability)
/// - Map probability to points: 99%=5.0, 95%=3.5, 80%=2.0
/// </summary>
public class BayesContentCheckV2(
    ILogger<BayesContentCheckV2> logger,
    IBayesClassifierService bayesClassifier,
    ITokenizerService tokenizerService) : IContentCheckV2
{
    public CheckName CheckName => CheckName.Bayes;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return false;

        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping Bayes check for {User}: User is {UserType}",
                request.User.ToLogDebug(),
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (BayesCheckRequest)request;

        try
        {
            // Check message length
            if (req.Message.Length < req.MinMessageLength)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                });
            }

            // Classify using the Singleton service (already trained via Quartz job / startup)
            var processedMessage = tokenizerService.RemoveEmojis(req.Message);
            var result = bayesClassifier.Classify(processedMessage);

            if (result is null)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Classifier not trained - insufficient data",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                });
            }

            var spamProbabilityPercent = (int)(result.SpamProbability * 100);

            // Abstain when uncertain or likely ham
            if (spamProbabilityPercent < BayesConstants.UncertaintyLowerBound)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Likely ham ({spamProbabilityPercent}% spam probability, certainty: {result.Certainty:F3})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                });
            }

            if (spamProbabilityPercent is >= BayesConstants.UncertaintyLowerBound and <= BayesConstants.UncertaintyUpperBound)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Uncertain ({spamProbabilityPercent}% spam probability, certainty: {result.Certainty:F3})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                });
            }

            // Map spam probability to SpamAssassin-style score
            var score = MapProbabilityToScore(spamProbabilityPercent);

            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = $"{result.Details} (certainty: {result.Certainty:F3})",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in BayesSpamCheckV2 for {User}", req.User.ToLogDebug());
            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            });
        }
    }

    /// <summary>
    /// Map Bayes probability to SpamAssassin-style score.
    /// Research guidance: bayes_99=5.0, bayes_95=3.5, bayes_80=2.0
    /// </summary>
    private static double MapProbabilityToScore(int spamProbabilityPercent)
    {
        return spamProbabilityPercent switch
        {
            >= BayesConstants.ProbabilityThreshold99 => ScoringConstants.ScoreBayes99,
            >= BayesConstants.ProbabilityThreshold95 => ScoringConstants.ScoreBayes95,
            >= BayesConstants.ProbabilityThreshold80 => ScoringConstants.ScoreBayes80,
            >= BayesConstants.ProbabilityThreshold70 => ScoringConstants.ScoreBayes70,
            _ => 0.5  // 60-70% = weak signal
        };
    }
}

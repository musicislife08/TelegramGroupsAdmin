using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 Bayes check with proper abstention and SpamAssassin-style scoring
/// Key changes:
/// - Abstain when not trained (instead of Clean 0%)
/// - Abstain when uncertain (40-60% probability)
/// - Map probability to points: 99%=5.0, 95%=3.5, 80%=2.0
/// </summary>
public class BayesSpamCheckV2(
    ILogger<BayesSpamCheckV2> logger,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITokenizerService tokenizerService) : IContentCheckV2
{
    private const int MAX_TRAINING_SAMPLES = 10_000;

    // SpamAssassin-style scoring (from research)
    private const double ScoreBayes99 = 5.0;   // 99%+ probability
    private const double ScoreBayes95 = 3.5;   // 95-99% probability
    private const double ScoreBayes80 = 2.0;   // 80-95% probability
    private const double ScoreBayes70 = 1.0;   // 70-80% probability

    // Abstention thresholds
    private const int UncertaintyLowerBound = 40; // <40% = likely ham, abstain (no negative scores)
    private const int UncertaintyUpperBound = 60; // 40-60% = uncertain, abstain

    private BayesClassifier? _classifier;
    private DateTime _lastTrainingUpdate = DateTime.MinValue;
    private readonly TimeSpan _retrainingInterval = TimeSpan.FromHours(1);

    public CheckName CheckName => CheckName.Bayes;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip expensive Bayes training/classification for trusted/admin users
        // Bayes is not a critical check - it's very expensive (~500ms) and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping Bayes check for user {UserId}: User is {UserType}",
                request.UserId,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (BayesCheckRequest)request;

        try
        {
            // Check message length
            if (req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Ensure classifier is trained
            await EnsureClassifierTrainedAsync(req.CancellationToken);

            if (_classifier == null)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Classifier not trained - insufficient data",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Classify message
            var processedMessage = tokenizerService.RemoveEmojis(req.Message);
            var (spamProbability, details, certainty) = _classifier.ClassifyMessage(processedMessage);
            var spamProbabilityPercent = (int)(spamProbability * 100);

            // V2 FIX: Abstain when uncertain (40-60%) or likely ham (<40%)
            if (spamProbabilityPercent < UncertaintyLowerBound)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Likely ham ({spamProbabilityPercent}% spam probability, certainty: {certainty:F3})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            if (spamProbabilityPercent is >= UncertaintyLowerBound and <= UncertaintyUpperBound)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Uncertain ({spamProbabilityPercent}% spam probability, certainty: {certainty:F3})",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Map spam probability to SpamAssassin-style score
            var score = MapProbabilityToScore(spamProbabilityPercent);

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = $"{details} (certainty: {certainty:F3})",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in BayesSpamCheckV2 for user {UserId}", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Map Bayes probability to SpamAssassin-style score
    /// Research guidance: bayes_99=5.0, bayes_95=3.5, bayes_80=2.0
    /// </summary>
    private static double MapProbabilityToScore(int spamProbabilityPercent)
    {
        return spamProbabilityPercent switch
        {
            >= 99 => ScoreBayes99,  // 5.0 points (strongest signal)
            >= 95 => ScoreBayes95,  // 3.5 points
            >= 80 => ScoreBayes80,  // 2.0 points
            >= 70 => ScoreBayes70,  // 1.0 points
            _ => 0.5                 // 60-70% = weak signal (0.5 points)
        };
    }

    private async Task EnsureClassifierTrainedAsync(CancellationToken cancellationToken)
    {
        // Check if retraining is needed
        if (_classifier != null && DateTime.UtcNow - _lastTrainingUpdate < _retrainingInterval)
        {
            return;
        }

        try
        {
            // Load training data (reuse V1 logic - same database query)
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var manualSamples = await (
                from dr in dbContext.DetectionResults
                join m in dbContext.Messages on dr.MessageId equals m.MessageId
                join mt in dbContext.MessageTranslations on m.MessageId equals mt.MessageId into translations
                from mt in translations.DefaultIfEmpty()
                where dr.UsedForTraining && (dr.DetectionSource == "manual" || dr.DetectionSource == "Manual")
                orderby dr.DetectedAt descending
                select new { MessageText = mt != null ? mt.TranslatedText : m.MessageText, dr.IsSpam, dr.DetectionSource }
            ).AsNoTracking().ToListAsync(cancellationToken);

            var autoSamples = await (
                from dr in dbContext.DetectionResults
                join m in dbContext.Messages on dr.MessageId equals m.MessageId
                join mt in dbContext.MessageTranslations on m.MessageId equals mt.MessageId into translations
                from mt in translations.DefaultIfEmpty()
                where dr.UsedForTraining && dr.DetectionSource != "manual" && dr.DetectionSource != "Manual"
                orderby dr.DetectedAt descending
                select new { MessageText = mt != null ? mt.TranslatedText : m.MessageText, dr.IsSpam, dr.DetectionSource }
            ).AsNoTracking().Take(MAX_TRAINING_SAMPLES).ToListAsync(cancellationToken);

            var trainingSet = manualSamples.Concat(autoSamples).ToList();

            if (!trainingSet.Any())
            {
                logger.LogWarning("No training samples available for Bayes V2 classifier");
                return;
            }

            // Create and train classifier
            _classifier = new BayesClassifier(tokenizerService);

            var spamCount = 0;
            var hamCount = 0;

            foreach (var sample in trainingSet)
            {
                _classifier.Train(sample.MessageText, sample.IsSpam);
                if (sample.IsSpam)
                    spamCount++;
                else
                    hamCount++;
            }

            _lastTrainingUpdate = DateTime.UtcNow;
            logger.LogInformation(
                "Bayes V2 classifier trained with {Total} samples ({Manual} manual, {Auto} auto, {Spam} spam, {Ham} ham)",
                trainingSet.Count, manualSamples.Count, autoSamples.Count, spamCount, hamCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrain Bayes V2 classifier");
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 Bayes check with proper abstention and SpamAssassin-style scoring
/// Key changes:
/// - Abstain when not trained (instead of Clean 0%)
/// - Abstain when uncertain (40-60% probability)
/// - Map probability to points: 99%=5.0, 95%=3.5, 80%=2.0
/// </summary>
public class BayesContentCheckV2(
    ILogger<BayesContentCheckV2> logger,
    IMLTrainingDataRepository trainingDataRepository,
    ITokenizerService tokenizerService) : IContentCheckV2
{
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
                "Skipping Bayes check for {User}: User is {UserType}",
                request.User.ToLogDebug(),
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

            // V2 FIX: Abstain when uncertain or likely ham
            if (spamProbabilityPercent < BayesConstants.UncertaintyLowerBound)
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

            if (spamProbabilityPercent is >= BayesConstants.UncertaintyLowerBound and <= BayesConstants.UncertaintyUpperBound)
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
            logger.LogError(ex, "Error in BayesSpamCheckV2 for {User}", req.User.ToLogDebug());
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
            >= BayesConstants.ProbabilityThreshold99 => ScoringConstants.ScoreBayes99,
            >= BayesConstants.ProbabilityThreshold95 => ScoringConstants.ScoreBayes95,
            >= BayesConstants.ProbabilityThreshold80 => ScoringConstants.ScoreBayes80,
            >= BayesConstants.ProbabilityThreshold70 => ScoringConstants.ScoreBayes70,
            _ => 0.5  // 60-70% = weak signal
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
            // Load training data using IMLTrainingDataRepository (shares logic with ML.NET classifier)
            var labeledMessageIds = await trainingDataRepository.GetLabeledMessageIdsAsync(cancellationToken);
            var spamSamples = await trainingDataRepository.GetSpamSamplesAsync(labeledMessageIds, cancellationToken);
            var hamSamples = await trainingDataRepository.GetHamSamplesAsync(spamSamples.Count, labeledMessageIds, cancellationToken);

            if (spamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass || hamSamples.Count < SpamClassifierMetadata.MinimumSamplesPerClass)
            {
                logger.LogWarning(
                    "Insufficient training data for Bayes (spam: {Spam}, ham: {Ham}, minimum: {Min})",
                    spamSamples.Count, hamSamples.Count, SpamClassifierMetadata.MinimumSamplesPerClass);
                return;  // _classifier stays null → abstains on all checks
            }

            var allSamples = spamSamples.Concat(hamSamples).ToList();

            // Log balance status after collecting samples
            var totalSamples = spamSamples.Count + hamSamples.Count;
            var spamRatio = totalSamples > 0 ? (double)spamSamples.Count / totalSamples : 0.0;
            var isBalanced = spamRatio >= SpamClassifierMetadata.MinBalancedSpamRatio &&
                             spamRatio <= SpamClassifierMetadata.MaxBalancedSpamRatio;

            if (!isBalanced)
            {
                logger.LogWarning(
                    "Bayes classifier training with imbalanced data: {Spam} spam + {Ham} ham = {SpamRatio:P1} spam ratio " +
                    "(recommended: 20-80%). Accuracy may be reduced.",
                    spamSamples.Count, hamSamples.Count, spamRatio);
            }

            // Create and train classifier
            _classifier = new BayesClassifier(tokenizerService);

            var spamCount = 0;
            var hamCount = 0;
            var explicitCount = 0;

            foreach (var sample in allSamples)
            {
                var isSpam = sample.Label == Core.Models.TrainingLabel.Spam;
                _classifier.Train(sample.Text, isSpam);

                if (isSpam)
                    spamCount++;
                else
                    hamCount++;

                if (sample.Source == TrainingSampleSource.Explicit)
                    explicitCount++;
            }

            _lastTrainingUpdate = DateTime.UtcNow;
            logger.LogInformation(
                "Bayes V2 classifier trained with {Total} samples ({Explicit} explicit labels, {Implicit} implicit auto, {Spam} spam, {Ham} ham)",
                allSamples.Count, explicitCount, allSamples.Count - explicitCount, spamCount, hamCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrain Bayes V2 classifier");
        }
    }
}

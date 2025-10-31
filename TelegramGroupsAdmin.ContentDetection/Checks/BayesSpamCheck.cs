using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Enhanced Naive Bayes spam classifier with database training and continuous learning
/// Based on tg-spam's Bayes implementation with improved self-learning capabilities
/// </summary>
public class BayesSpamCheck(
    ILogger<BayesSpamCheck> logger,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITokenizerService tokenizerService) : IContentCheck
{
    private const int MAX_TRAINING_SAMPLES = 10_000;

    private BayesClassifier? _classifier;
    private DateTime _lastTrainingUpdate = DateTime.MinValue;
    private readonly TimeSpan _retrainingInterval = TimeSpan.FromHours(1);

    public CheckName CheckName => CheckName.Bayes;

    /// <summary>
    /// Check if Bayes check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    /// <summary>
    /// Execute Bayes spam check with strongly-typed request
    /// Config values come from request - check loads training data from DB with guardrails
    /// </summary>
    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (BayesCheckRequest)request;

        try
        {
            // Check message length
            if (req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    Confidence = 0
                };
            }

            // Ensure classifier is trained with latest data
            await EnsureClassifierTrainedAsync(req.CancellationToken);

            if (_classifier == null)
            {
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Classifier not trained - insufficient data",
                    Confidence = 0
                };
            }

            // Preprocess message using shared tokenizer
            var processedMessage = tokenizerService.RemoveEmojis(req.Message);
            var (spamProbability, details, certainty) = _classifier.ClassifyMessage(processedMessage);

            var spamProbabilityPercent = spamProbability * 100;
            var isSpam = spamProbabilityPercent >= req.MinSpamProbability;
            var result = isSpam ? CheckResultType.Spam : CheckResultType.Clean;

            // Calculate confidence based on certainty and how far from threshold
            // If spam: confidence = spamProbability * certainty
            // If ham: confidence = (100 - spamProbability) * certainty
            var confidence = isSpam
                ? (int)(spamProbabilityPercent * certainty)
                : (int)((100 - spamProbabilityPercent) * certainty);

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = result,
                Details = $"{details} (certainty: {certainty:F3})",
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
        }
    }

    /// <summary>
    /// Ensure classifier is trained with latest data from database
    /// Uses DbContextFactory directly with MAX_TRAINING_SAMPLES guardrail
    /// </summary>
    private async Task EnsureClassifierTrainedAsync(CancellationToken cancellationToken)
    {
        // Check if retraining is needed
        if (_classifier != null && DateTime.UtcNow - _lastTrainingUpdate < _retrainingInterval)
        {
            return;
        }

        try
        {
            // Load training data from detection_results (Phase 2.2: normalized architecture)
            // Training data = detection_results WHERE used_for_training = true
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Query detection_results + JOIN messages to get message text
            // Separate manual vs auto based on detection_source
            var manualSamples = await (
                from dr in dbContext.DetectionResults
                join m in dbContext.Messages on dr.MessageId equals m.MessageId
                where dr.UsedForTraining && (dr.DetectionSource == "manual" || dr.DetectionSource == "Manual")
                orderby dr.DetectedAt descending
                select new { m.MessageText, dr.IsSpam, dr.DetectionSource }
            ).AsNoTracking().ToListAsync(cancellationToken);

            var autoSamples = await (
                from dr in dbContext.DetectionResults
                join m in dbContext.Messages on dr.MessageId equals m.MessageId
                where dr.UsedForTraining && dr.DetectionSource != "manual" && dr.DetectionSource != "Manual"
                orderby dr.DetectedAt descending
                select new { m.MessageText, dr.IsSpam, dr.DetectionSource }
            ).AsNoTracking().Take(MAX_TRAINING_SAMPLES).ToListAsync(cancellationToken);

            // Combine: all manual + recent MAX_TRAINING_SAMPLES auto
            var trainingSet = manualSamples.Concat(autoSamples).ToList();

            if (!trainingSet.Any())
            {
                logger.LogWarning("No training samples available for Bayes classifier (detection_results.used_for_training = true)");
                return;
            }

            // Create new classifier and train with bounded sample set
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
                "Bayes classifier trained with {Total} samples ({Manual} manual, {Auto} auto, {Spam} spam, {Ham} ham)",
                trainingSet.Count, manualSamples.Count, autoSamples.Count, spamCount, hamCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrain Bayes classifier");
        }
    }

}

/// <summary>
/// Enhanced Naive Bayes classifier with certainty scoring
/// </summary>
internal class BayesClassifier
{
    private readonly Dictionary<string, int> _spamWordCounts = new();
    private readonly Dictionary<string, int> _hamWordCounts = new();
    private readonly ITokenizerService _tokenizerService;
    private int _spamMessageCount;
    private int _hamMessageCount;
    private int _totalWordCount;

    public BayesClassifier(ITokenizerService tokenizerService)
    {
        _tokenizerService = tokenizerService;
    }

    /// <summary>
    /// Train the classifier with a message sample
    /// </summary>
    public void Train(string message, bool isSpam)
    {
        var words = _tokenizerService.Tokenize(message);
        var wordCounts = isSpam ? _spamWordCounts : _hamWordCounts;

        foreach (var word in words)
        {
            wordCounts[word] = wordCounts.GetValueOrDefault(word, 0) + 1;
            _totalWordCount++;
        }

        if (isSpam)
        {
            _spamMessageCount++;
        }
        else
        {
            _hamMessageCount++;
        }
    }

    /// <summary>
    /// Classify a message and return spam probability with certainty score
    /// </summary>
    public (double spamProbability, string details, double certainty) ClassifyMessage(string message)
    {
        if (_spamMessageCount == 0 || _hamMessageCount == 0)
        {
            return (0.0, "Classifier not trained", 0.0);
        }

        var words = _tokenizerService.Tokenize(message);
        if (!words.Any())
        {
            return (0.0, "No words to analyze", 0.0);
        }

        // Calculate prior probabilities
        var totalMessages = _spamMessageCount + _hamMessageCount;
        var priorSpam = (double)_spamMessageCount / totalMessages;
        var priorHam = (double)_hamMessageCount / totalMessages;

        // Calculate likelihood for each word (with Laplace smoothing)
        var logProbSpam = Math.Log(priorSpam);
        var logProbHam = Math.Log(priorHam);

        var spamWordTotal = _spamWordCounts.Values.Sum();
        var hamWordTotal = _hamWordCounts.Values.Sum();
        var vocabularySize = _spamWordCounts.Keys.Union(_hamWordCounts.Keys).Count();

        var significantWords = new List<string>();

        foreach (var word in words.Distinct())
        {
            var spamWordCount = _spamWordCounts.GetValueOrDefault(word, 0);
            var hamWordCount = _hamWordCounts.GetValueOrDefault(word, 0);

            // Laplace smoothing: add 1 to counts, add vocabulary size to totals
            var spamLikelihood = (double)(spamWordCount + 1) / (spamWordTotal + vocabularySize);
            var hamLikelihood = (double)(hamWordCount + 1) / (hamWordTotal + vocabularySize);

            logProbSpam += Math.Log(spamLikelihood);
            logProbHam += Math.Log(hamLikelihood);

            // Track words that significantly favor spam
            if (spamWordCount > hamWordCount && spamWordCount > 0)
            {
                significantWords.Add(word);
            }
        }

        // Convert back from log space using normalization
        var maxLogProb = Math.Max(logProbSpam, logProbHam);
        var normSpamProb = Math.Exp(logProbSpam - maxLogProb);
        var normHamProb = Math.Exp(logProbHam - maxLogProb);

        var spamProbability = normSpamProb / (normSpamProb + normHamProb);

        // Calculate certainty based on how far the probability is from 0.5 (uncertain)
        var certainty = Math.Abs(spamProbability - 0.5) * 2; // Scale to 0-1 range

        var details = spamProbability > 0.5
            ? $"Spam probability: {spamProbability:F3}" + (significantWords.Any() ? $" (key words: {string.Join(", ", significantWords.Take(3))})" : "")
            : $"Ham probability: {1 - spamProbability:F3}";

        return (spamProbability, details, certainty);
    }


}
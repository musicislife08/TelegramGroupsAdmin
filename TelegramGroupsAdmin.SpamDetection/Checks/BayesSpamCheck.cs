using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Enhanced Naive Bayes spam classifier with database training and continuous learning
/// Based on tg-spam's Bayes implementation with improved self-learning capabilities
/// </summary>
public class BayesSpamCheck : ISpamCheck
{
    private readonly ILogger<BayesSpamCheck> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly ITrainingSamplesRepository _trainingSamplesRepository;
    private readonly ITokenizerService _tokenizerService;
    private BayesClassifier? _classifier;
    private DateTime _lastTrainingUpdate = DateTime.MinValue;
    private readonly TimeSpan _retrainingInterval = TimeSpan.FromHours(1);

    public string CheckName => "Bayes";

    public BayesSpamCheck(
        ILogger<BayesSpamCheck> logger,
        SpamDetectionConfig config,
        ITrainingSamplesRepository trainingSamplesRepository,
        ITokenizerService tokenizerService)
    {
        _logger = logger;
        _config = config;
        _trainingSamplesRepository = trainingSamplesRepository;
        _tokenizerService = tokenizerService;
    }

    /// <summary>
    /// Check if Bayes check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Check if Bayes check is enabled
        if (!_config.Bayes.Enabled)
        {
            return false;
        }

        // Skip empty or very short messages
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length < _config.MinMessageLength)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Execute Bayes spam check with database training
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure classifier is trained with latest data
            await EnsureClassifierTrainedAsync(cancellationToken);

            if (_classifier == null)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Classifier not trained - insufficient data",
                    Confidence = 0
                };
            }

            // Preprocess message using shared tokenizer
            var processedMessage = _tokenizerService.RemoveEmojis(request.Message);
            var (spamProbability, details, certainty) = _classifier.ClassifyMessage(processedMessage);

            var spamProbabilityPercent = spamProbability * 100;
            var isSpam = spamProbabilityPercent >= _config.Bayes.MinSpamProbability;

            // Adjust confidence based on certainty (how confident the classifier is)
            var confidence = (int)(spamProbabilityPercent * certainty);

            _logger.LogDebug("Bayes check for user {UserId}: SpamProbability={SpamProbability:F3}, Certainty={Certainty:F3}, Threshold={Threshold}, IsSpam={IsSpam}",
                request.UserId, spamProbabilityPercent, certainty, _config.Bayes.MinSpamProbability, isSpam);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = isSpam,
                Details = $"{details} (certainty: {certainty:F3})",
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bayes check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "Bayes check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Ensure classifier is trained with latest data from database
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
            // Bounded training query: Use recent 10k samples + all manual samples
            // This prevents unbounded growth while preserving curated training data
            const int maxAutoSamples = 10000;

            var allSamples = await _trainingSamplesRepository.GetAllSamplesAsync(cancellationToken);
            var samplesList = allSamples.ToList();

            // Separate manual samples from automatic detections
            var manualSamples = samplesList.Where(s => s.Source == "Manual" || s.Source == "manual").ToList();
            var autoSamples = samplesList.Where(s => s.Source != "Manual" && s.Source != "manual")
                .OrderByDescending(s => s.AddedDate)
                .Take(maxAutoSamples)
                .ToList();

            // Combine: all manual + recent 10k auto
            var trainingSet = manualSamples.Concat(autoSamples).ToList();

            if (!trainingSet.Any())
            {
                _logger.LogWarning("No training samples available for Bayes classifier");
                return;
            }

            // Create new classifier and train with bounded sample set
            _classifier = new BayesClassifier(_tokenizerService);

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
            _logger.LogInformation(
                "Bayes classifier trained with {Total} samples ({Manual} manual, {Auto} auto, {Spam} spam, {Ham} ham)",
                trainingSet.Count, manualSamples.Count, autoSamples.Count, spamCount, hamCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrain Bayes classifier");
        }
    }

    /// <summary>
    /// Add training sample for continuous learning (call from spam detection pipeline)
    /// </summary>
    public async Task AddTrainingSampleAsync(string messageText, bool isSpam, string source, int? confidence = null, string? groupId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _trainingSamplesRepository.AddSampleAsync(messageText, isSpam, source, confidence, groupId, addedBy: "auto_learning", cancellationToken);

            // Force retraining on next check to incorporate new sample
            _lastTrainingUpdate = DateTime.MinValue;

            _logger.LogDebug("Added training sample: {Type} from {Source}", isSpam ? "SPAM" : "HAM", source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add training sample for continuous learning");
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
    private int _spamMessageCount = 0;
    private int _hamMessageCount = 0;
    private int _totalWordCount = 0;

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
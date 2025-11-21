using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Naive Bayes classifier for spam detection
/// Shared by both V1 and V2 implementations
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

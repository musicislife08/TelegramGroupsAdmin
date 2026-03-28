using System.Collections.Frozen;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Naive Bayes classifier for spam detection.
/// Constructed with frozen, immutable word-count snapshots and pre-computed aggregates
/// so that ClassifyMessage incurs zero per-call allocations for sums or set unions.
/// </summary>
internal class BayesClassifier
{
    private readonly FrozenDictionary<string, int> _spamWordCounts;
    private readonly FrozenDictionary<string, int> _hamWordCounts;
    private readonly ITokenizerService _tokenizerService;
    private readonly int _spamMessageCount;
    private readonly int _hamMessageCount;

    // Pre-computed aggregates — never recalculated on hot path
    private readonly int _vocabularySize;
    private readonly long _spamWordTotal;
    private readonly long _hamWordTotal;
    private readonly double _laplaceDenominatorSpam;
    private readonly double _laplaceDenominatorHam;

    public int SpamVocabularySize => _spamWordCounts.Count;
    public int HamVocabularySize => _hamWordCounts.Count;

    public BayesClassifier(
        ITokenizerService tokenizerService,
        Dictionary<string, int> spamWordCounts,
        Dictionary<string, int> hamWordCounts,
        int spamMessageCount,
        int hamMessageCount)
    {
        _tokenizerService = tokenizerService;
        _spamMessageCount = spamMessageCount;
        _hamMessageCount = hamMessageCount;

        _spamWordCounts = spamWordCounts.ToFrozenDictionary();
        _hamWordCounts = hamWordCounts.ToFrozenDictionary();

        _vocabularySize = _spamWordCounts.Keys.Union(_hamWordCounts.Keys).Count();
        _spamWordTotal = _spamWordCounts.Values.Sum();
        _hamWordTotal = _hamWordCounts.Values.Sum();
        _laplaceDenominatorSpam = _spamWordTotal + _vocabularySize;
        _laplaceDenominatorHam = _hamWordTotal + _vocabularySize;
    }

    /// <summary>
    /// Classify a message and return spam probability with certainty score.
    /// </summary>
    public BayesClassificationResult ClassifyMessage(string message)
    {
        if (_spamMessageCount == 0 || _hamMessageCount == 0)
        {
            return new BayesClassificationResult(0.0, "Classifier not trained", 0.0);
        }

        var words = _tokenizerService.Tokenize(message);
        if (!words.Any())
        {
            return new BayesClassificationResult(0.0, "No words to analyze", 0.0);
        }

        // Calculate prior probabilities
        var totalMessages = _spamMessageCount + _hamMessageCount;
        var priorSpam = (double)_spamMessageCount / totalMessages;
        var priorHam = (double)_hamMessageCount / totalMessages;

        // Calculate likelihood for each word (with Laplace smoothing)
        var logProbSpam = Math.Log(priorSpam);
        var logProbHam = Math.Log(priorHam);

        var significantWords = new List<string>();

        foreach (var word in words.Distinct())
        {
            var spamWordCount = _spamWordCounts.GetValueOrDefault(word, 0);
            var hamWordCount = _hamWordCounts.GetValueOrDefault(word, 0);

            // Laplace smoothing: add 1 to counts, use pre-computed denominators
            var spamLikelihood = (double)(spamWordCount + 1) / _laplaceDenominatorSpam;
            var hamLikelihood = (double)(hamWordCount + 1) / _laplaceDenominatorHam;

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

        return new BayesClassificationResult(spamProbability, details, certainty);
    }
}

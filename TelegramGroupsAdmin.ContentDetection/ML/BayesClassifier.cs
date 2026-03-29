using System.Collections.Frozen;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Naive Bayes classifier for spam detection.
/// Constructed with frozen, immutable word-count snapshots and pre-computed aggregates
/// so that ClassifyMessage incurs zero per-call allocations for sums or set unions.
/// </summary>
/// <remarks>
/// ClassifyMessage uses span-based tokenization with <see cref="Regex.EnumerateMatches(ReadOnlySpan{char})"/>
/// and <see cref="FrozenDictionary{TKey,TValue}.GetAlternateLookup{TAlternateKey}"/> to perform dictionary
/// lookups directly from <see cref="ReadOnlySpan{T}"/> slices without allocating intermediate strings.
/// </remarks>
internal class BayesClassifier
{
    private readonly FrozenDictionary<string, int> _spamWordCounts;
    private readonly FrozenDictionary<string, int> _hamWordCounts;
    private readonly int _spamMessageCount;
    private readonly int _hamMessageCount;

    // Alternate lookups for span-based dictionary access (no string allocation per word)
    private readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _spamLookup;
    private readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _hamLookup;

    // Cached regex instance for span-based word extraction
    private static readonly Regex _wordBoundaryRegex = TextTokenizer.GetWordBoundaryRegex();

    // Pre-computed aggregates — never recalculated on hot path
    private readonly int _vocabularySize;
    private readonly long _spamWordTotal;
    private readonly long _hamWordTotal;
    private readonly double _laplaceDenominatorSpam;
    private readonly double _laplaceDenominatorHam;

    public int SpamVocabularySize => _spamWordCounts.Count;
    public int HamVocabularySize => _hamWordCounts.Count;

    public BayesClassifier(
        Dictionary<string, int> spamWordCounts,
        Dictionary<string, int> hamWordCounts,
        int spamMessageCount,
        int hamMessageCount)
    {
        _spamMessageCount = spamMessageCount;
        _hamMessageCount = hamMessageCount;

        // Build with OrdinalIgnoreCase to support AlternateLookup<ReadOnlySpan<char>>
        // and case-insensitive matching (training data is already lowercased, but
        // classification input may not be — the comparer handles it).
        _spamWordCounts = spamWordCounts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _hamWordCounts = hamWordCounts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Cache alternate lookups — these are cheap struct wrappers, safe to store
        _spamLookup = _spamWordCounts.GetAlternateLookup<ReadOnlySpan<char>>();
        _hamLookup = _hamWordCounts.GetAlternateLookup<ReadOnlySpan<char>>();

        _vocabularySize = _spamWordCounts.Keys.Union(_hamWordCounts.Keys).Count();
        _spamWordTotal = _spamWordCounts.Values.Sum();
        _hamWordTotal = _hamWordCounts.Values.Sum();
        _laplaceDenominatorSpam = _spamWordTotal + _vocabularySize;
        _laplaceDenominatorHam = _hamWordTotal + _vocabularySize;
    }

    /// <summary>
    /// Classify a message and return spam probability with certainty score.
    /// </summary>
    /// <remarks>
    /// Uses span-based tokenization to avoid per-word string allocations:
    /// <list type="bullet">
    /// <item><see cref="Regex.EnumerateMatches(ReadOnlySpan{char})"/> yields ValueMatch structs (no Match objects)</item>
    /// <item><see cref="FrozenDictionary{TKey,TValue}.AlternateLookup{TAlternateKey}"/> performs dictionary lookups from spans</item>
    /// <item>Emoji removal is skipped — emojis never match the <c>\b[\w']+\b</c> word boundary regex</item>
    /// <item>Lowercasing is skipped — FrozenDictionaries use <see cref="StringComparer.OrdinalIgnoreCase"/></item>
    /// <item>Deduplication uses a HashSet instead of LINQ Distinct() to avoid iterator allocation</item>
    /// </list>
    /// </remarks>
    public BayesClassificationResult ClassifyMessage(string message)
    {
        if (_spamMessageCount == 0 || _hamMessageCount == 0)
        {
            return new BayesClassificationResult(0.0, "Classifier not trained", 0.0);
        }

        // Span-based tokenization: enumerate regex matches without allocating Match objects.
        // Skip emoji removal (emojis don't match \b[\w']+\b) and lowercasing (OrdinalIgnoreCase comparer).
        var textSpan = message.AsSpan();
        var seenWords = new HashSet<UInt128>(); // track seen words by 128-bit hash (collision-safe)
        var wordCount = 0;

        // Calculate prior probabilities
        var totalMessages = _spamMessageCount + _hamMessageCount;
        var priorSpam = (double)_spamMessageCount / totalMessages;
        var priorHam = (double)_hamMessageCount / totalMessages;

        var logProbSpam = Math.Log(priorSpam);
        var logProbHam = Math.Log(priorHam);

        var significantWords = new List<string>();

        // Pre-allocate lowercase buffer outside loop (CA2014: stackalloc must not be in loops).
        // Telegram messages are max 4096 chars; individual words are much shorter.
        const int maxWordLength = 256;
        Span<char> lowerBuf = stackalloc char[maxWordLength];

        foreach (var valueMatch in _wordBoundaryRegex.EnumerateMatches(textSpan))
        {
            var wordSpan = textSpan.Slice(valueMatch.Index, valueMatch.Length);

            // Filter: minimum length
            if (wordSpan.Length < TextTokenizer.DefaultMinTokenLength)
                continue;

            // Filter: stop words (span-based, no allocation)
            if (TextTokenizer.IsStopWord(wordSpan))
                continue;

            // Filter: pure numeric tokens
            if (int.TryParse(wordSpan, out _))
                continue;

            wordCount++;

            // Deduplicate using 128-bit hash — zero-allocation via pre-allocated buffer + ToLowerInvariant
            var wordLower = lowerBuf[..wordSpan.Length];
            wordSpan.ToLowerInvariant(wordLower);
            var hash = XxHash128.HashToUInt128(MemoryMarshal.AsBytes(wordLower));
            if (!seenWords.Add(hash))
                continue;

            // Span-based dictionary lookups — no string allocation
            _spamLookup.TryGetValue(wordSpan, out var spamWordCount);
            _hamLookup.TryGetValue(wordSpan, out var hamWordCount);

            // Laplace smoothing: add 1 to counts, use pre-computed denominators
            var spamLikelihood = (double)(spamWordCount + 1) / _laplaceDenominatorSpam;
            var hamLikelihood = (double)(hamWordCount + 1) / _laplaceDenominatorHam;

            logProbSpam += Math.Log(spamLikelihood);
            logProbHam += Math.Log(hamLikelihood);

            // Track words that significantly favor spam (must materialize to string for storage)
            if (spamWordCount > hamWordCount && spamWordCount > 0)
            {
                significantWords.Add(wordSpan.ToString());
            }
        }

        if (wordCount == 0)
        {
            return new BayesClassificationResult(0.0, "No words to analyze", 0.0);
        }

        // Convert back from log space using normalization
        var maxLogProb = Math.Max(logProbSpam, logProbHam);
        var normSpamProb = Math.Exp(logProbSpam - maxLogProb);
        var normHamProb = Math.Exp(logProbHam - maxLogProb);

        var spamProbability = normSpamProb / (normSpamProb + normHamProb);

        // Calculate certainty based on how far the probability is from 0.5 (uncertain)
        var certainty = Math.Abs(spamProbability - 0.5) * 2; // Scale to 0-1 range

        var details = spamProbability > 0.5
            ? $"Spam probability: {spamProbability:F3}" + (significantWords.Count > 0 ? $" (key words: {string.Join(", ", significantWords.Take(3))})" : "")
            : $"Ham probability: {1 - spamProbability:F3}";

        return new BayesClassificationResult(spamProbability, details, certainty);
    }
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Enhanced spam check using cosine similarity with database-stored patterns and early exit
/// Based on tg-spam's similarity detection using TF-IDF vectors with optimizations
/// </summary>
public class SimilaritySpamCheck : ISpamCheck
{
    private readonly ILogger<SimilaritySpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly ITrainingSamplesRepository _trainingSamplesRepository;
    private readonly ITokenizerService _tokenizerService;


    public string CheckName => "Similarity";

    // Cached spam samples and vectors
    private List<TrainingSample>? _cachedSamples;
    private Dictionary<long, double[]>? _cachedVectors;
    private HashSet<string>? _cachedVocabulary;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(10);

    public SimilaritySpamCheck(
        ILogger<SimilaritySpamCheck> logger,
        ISpamDetectionConfigRepository configRepository,
        ITrainingSamplesRepository trainingSamplesRepository,
        ITokenizerService tokenizerService)
    {
        _logger = logger;
        _configRepository = configRepository;
        _trainingSamplesRepository = trainingSamplesRepository;
        _tokenizerService = tokenizerService;
    }

    /// <summary>
    /// Check if similarity check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
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
    /// Execute similarity spam check with database samples and early exit
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config from database
            var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

            // Check if this check is enabled
            if (!config.Similarity.Enabled)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Check disabled",
                    Confidence = 0
                };
            }

            // Check message length
            if (request.Message.Length < config.MinMessageLength)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = $"Message too short (< {config.MinMessageLength} chars)",
                    Confidence = 0
                };
            }

            // Get cached spam samples and vectors
            await RefreshCacheIfNeededAsync(request.ChatId, cancellationToken);

            if (_cachedSamples == null || !_cachedSamples.Any() || _cachedVectors == null || _cachedVocabulary == null)
            {
                _logger.LogWarning("Similarity check has no spam samples: Samples={HasSamples}, Count={Count}, Vectors={HasVectors}, Vocab={HasVocab}",
                    _cachedSamples != null, _cachedSamples?.Count ?? 0, _cachedVectors != null, _cachedVocabulary != null);
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = $"No spam samples available for comparison (loaded: {_cachedSamples?.Count ?? 0})",
                    Confidence = 0
                };
            }

            // Preprocess message using shared tokenizer
            var processedMessage = _tokenizerService.RemoveEmojis(request.Message);
            var messageVector = ComputeTfIdfVector(processedMessage, _cachedVocabulary);

            // Calculate similarity with early exit optimization
            var maxSimilarity = 0.0;
            var matchedSampleId = 0L;
            var checkedCount = 0;

            // Sort samples by detection count (most effective patterns first)
            var sortedSamples = _cachedSamples.OrderByDescending(s => s.DetectionCount).ToList();

            foreach (var sample in sortedSamples)
            {
                checkedCount++;

                if (!_cachedVectors.TryGetValue(sample.Id, out var spamVector))
                    continue;

                var similarity = CosineSimilarity(messageVector, spamVector);
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                    matchedSampleId = sample.Id;
                }

                // Early exit: if we found a high-confidence match, stop checking
                if (similarity >= 0.9) // Very high similarity threshold for early exit
                {
                    _logger.LogDebug("Early exit after checking {CheckedCount}/{TotalCount} samples", checkedCount, sortedSamples.Count);
                    break;
                }

                // Early exit: if we've checked enough samples and have a decent match
                if (checkedCount >= 20 && maxSimilarity >= config.Similarity.Threshold)
                {
                    _logger.LogDebug("Early exit after checking {CheckedCount} samples with threshold match", checkedCount);
                    break;
                }
            }

            // Determine if message is spam based on similarity threshold
            var isSpam = maxSimilarity >= config.Similarity.Threshold;
            var confidence = (int)(maxSimilarity * 100);

            // Update detection count for matched sample
            if (isSpam && matchedSampleId > 0)
            {
                _ = Task.Run(() => _trainingSamplesRepository.IncrementDetectionCountAsync(matchedSampleId, CancellationToken.None), CancellationToken.None);
            }

            var details = isSpam
                ? $"High similarity ({maxSimilarity:F3}) to spam sample (checked {checkedCount}/{sortedSamples.Count})"
                : $"Low similarity ({maxSimilarity:F3}) to spam samples (checked {checkedCount}/{sortedSamples.Count})";

            _logger.LogDebug("Similarity check for user {UserId}: MaxSimilarity={MaxSimilarity:F3}, Threshold={Threshold}, Checked={CheckedCount}, IsSpam={IsSpam}",
                request.UserId, maxSimilarity, config.Similarity.Threshold, checkedCount, isSpam);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = isSpam,
                Details = details,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Similarity check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "Similarity check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Refresh cache if needed
    /// </summary>
    private async Task RefreshCacheIfNeededAsync(string? chatId, CancellationToken cancellationToken)
    {
        if (_cachedSamples == null || DateTime.UtcNow - _lastCacheUpdate > _cacheRefreshInterval)
        {
            try
            {
                _logger.LogInformation("Refreshing similarity cache for chat {ChatId}...", chatId ?? "global");
                var samples = await _trainingSamplesRepository.GetSpamSamplesAsync(chatId, cancellationToken);
                _cachedSamples = samples.ToList();

                _logger.LogInformation("Loaded {Count} spam samples from repository", _cachedSamples.Count);

                // Build vocabulary from all samples
                _cachedVocabulary = BuildVocabulary(_cachedSamples.Select(s => s.MessageText).ToArray());
                _logger.LogInformation("Built vocabulary with {VocabSize} unique words", _cachedVocabulary.Count);

                // Pre-compute TF-IDF vectors for all samples
                _cachedVectors = new Dictionary<long, double[]>();
                foreach (var sample in _cachedSamples)
                {
                    _cachedVectors[sample.Id] = ComputeTfIdfVector(sample.MessageText, _cachedVocabulary);
                }

                _lastCacheUpdate = DateTime.UtcNow;
                _logger.LogInformation("Refreshed similarity cache with {Count} samples, {VocabSize} vocab, {VectorCount} vectors for chat {ChatId}",
                    _cachedSamples.Count, _cachedVocabulary.Count, _cachedVectors.Count, chatId ?? "global");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh similarity cache");
                // Keep existing cache if refresh fails
            }
        }
    }

    /// <summary>
    /// Build vocabulary from spam samples
    /// </summary>
    private HashSet<string> BuildVocabulary(string[] documents)
    {
        var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in documents)
        {
            var words = NormalizeAndTokenize(doc);
            foreach (var word in words)
            {
                vocabulary.Add(word);
            }
        }

        return vocabulary;
    }

    /// <summary>
    /// Normalize text and extract tokens (words) using shared tokenizer
    /// </summary>
    private string[] NormalizeAndTokenize(string text)
    {
        // Use shared tokenizer for consistent preprocessing
        var tokens = _tokenizerService.Tokenize(text);

        // Additional filtering for similarity analysis
        return tokens.Where(w => w.Length >= 3).ToArray();
    }


    /// <summary>
    /// Compute TF-IDF vector for a document
    /// </summary>
    private double[] ComputeTfIdfVector(string document, HashSet<string> vocabulary)
    {
        var words = NormalizeAndTokenize(document);
        var wordCounts = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var vector = new double[vocabulary.Count];

        var vocabArray = vocabulary.ToArray();
        for (int i = 0; i < vocabArray.Length; i++)
        {
            var word = vocabArray[i];
            if (wordCounts.TryGetValue(word, out var count))
            {
                // Simple TF-IDF: (term frequency) * log(inverse document frequency)
                // For simplicity, using basic TF and assuming uniform IDF
                vector[i] = (double)count / words.Length;
            }
        }

        return vector;
    }

    /// <summary>
    /// Calculate cosine similarity between two vectors
    /// </summary>
    private static double CosineSimilarity(double[] vectorA, double[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }

        var dotProduct = 0.0;
        var magnitudeA = 0.0;
        var magnitudeB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        if (magnitudeA == 0.0 || magnitudeB == 0.0)
        {
            return 0.0;
        }

        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
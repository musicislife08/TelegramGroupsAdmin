using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Enhanced spam check using cosine similarity with database-stored patterns and early exit
/// Based on tg-spam's similarity detection using TF-IDF vectors with optimizations
/// Engine orchestrates config loading - check manages its own DB access with guardrails
/// </summary>
public class SimilaritySpamCheck : IContentCheck
{
    private const int MAX_SIMILARITY_SAMPLES = 5_000; // Guardrail: cap similarity query

    private readonly ILogger<SimilaritySpamCheck> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ITokenizerService _tokenizerService;

    public string CheckName => "Similarity";

    // Internal training sample record (used only by this check, loaded from detection_results)
    private record TrainingSample(
        long Id,
        string MessageText,
        bool IsSpam,
        DateTimeOffset AddedDate,
        string Source,
        int ConfidenceWhenAdded,
        long[] ChatIds,
        string? AddedBy,
        int DetectionCount,
        DateTimeOffset? LastDetectedDate
    );

    // Cached spam samples and vectors
    private List<TrainingSample>? _cachedSamples;
    private Dictionary<long, double[]>? _cachedVectors;
    private HashSet<string>? _cachedVocabulary;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(10);

    public SimilaritySpamCheck(
        ILogger<SimilaritySpamCheck> logger,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ITokenizerService tokenizerService)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _tokenizerService = tokenizerService;
    }

    /// <summary>
    /// Check if similarity check should be executed
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
    /// Execute similarity spam check with strongly-typed request
    /// Config values come from request - check loads spam samples from DB with guardrails
    /// </summary>
    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (SimilarityCheckRequest)request;

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

            // Get cached spam samples and vectors
            await RefreshCacheIfNeededAsync(req.ChatId, req.CancellationToken);

            if (_cachedSamples == null || !_cachedSamples.Any() || _cachedVectors == null || _cachedVocabulary == null)
            {
                _logger.LogWarning("Similarity check has no spam samples: Samples={HasSamples}, Count={Count}, Vectors={HasVectors}, Vocab={HasVocab}",
                    _cachedSamples != null, _cachedSamples?.Count ?? 0, _cachedVectors != null, _cachedVocabulary != null);
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = $"No spam samples available for comparison (loaded: {_cachedSamples?.Count ?? 0})",
                    Confidence = 0
                };
            }

            // Preprocess message using shared tokenizer
            var processedMessage = _tokenizerService.RemoveEmojis(req.Message);
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
                    break;

                // Early exit: if we've checked enough samples and have a decent match
                if (checkedCount >= 20 && maxSimilarity >= req.SimilarityThreshold)
                    break;
            }

            // Determine if message is spam based on similarity threshold
            var isSpam = maxSimilarity >= req.SimilarityThreshold;
            var result = isSpam ? CheckResultType.Spam : CheckResultType.Clean;

            // Calculate confidence based on similarity and spam decision
            // If spam: confidence = similarity * 100 (how similar to known spam)
            // If not spam: confidence = (1 - similarity) * 100 (how dissimilar from spam)
            var confidence = isSpam
                ? (int)(maxSimilarity * 100)
                : (int)((1.0 - maxSimilarity) * 100);

            // Update detection count for matched sample
            // Note: Detection count tracking removed in normalized schema (detection_results table)
            // This is now a no-op for backward compatibility - keeping the code structure but removing the fire-and-forget
            if (isSpam && matchedSampleId > 0)
            {
                _logger.LogDebug("Similarity match found for sample {SampleId} (detection count tracking removed in normalized schema)", matchedSampleId);
            }

            var details = isSpam
                ? $"High similarity ({maxSimilarity:F3}) to spam sample (checked {checkedCount}/{sortedSamples.Count})"
                : $"Low similarity ({maxSimilarity:F3}) to spam samples (checked {checkedCount}/{sortedSamples.Count})";

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = result,
                Details = details,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Similarity check failed for user {UserId}", req.UserId);
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean, // Fail open
                Details = "Similarity check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Refresh cache if needed
    /// Uses DbContextFactory directly with MAX_SIMILARITY_SAMPLES guardrail
    /// </summary>
    private async Task RefreshCacheIfNeededAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_cachedSamples == null || DateTime.UtcNow - _lastCacheUpdate > _cacheRefreshInterval)
        {
            try
            {
                _logger.LogInformation("Refreshing similarity cache for chat {ChatId}...", chatId);

                // Load spam samples from database with guardrail
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var samples = await dbContext.DetectionResults
                    .AsNoTracking()
                    .Include(dr => dr.Message)
                    .Where(dr => dr.IsSpam && dr.UsedForTraining)
                    .OrderByDescending(dr => dr.DetectedAt)
                    .Take(MAX_SIMILARITY_SAMPLES) // Guardrail
                    .Select(dr => new TrainingSample(
                        dr.Id,
                        dr.Message!.MessageText!,
                        dr.IsSpam,
                        dr.DetectedAt,
                        dr.DetectionSource,
                        dr.Confidence,
                        new long[] { dr.Message!.ChatId },
                        dr.SystemIdentifier ?? "unknown", // Phase 4.19: Actor system (simplified for internal use)
                        0, // DetectionCount removed in normalized schema
                        null // LastDetectedDate removed in normalized schema
                    ))
                    .ToListAsync(cancellationToken);

                _cachedSamples = samples;

                _logger.LogInformation("Loaded {Count} spam samples from database", _cachedSamples.Count);

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
                    _cachedSamples.Count, _cachedVocabulary.Count, _cachedVectors.Count, chatId);
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
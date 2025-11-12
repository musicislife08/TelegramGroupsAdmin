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
/// Enhanced spam check using cosine similarity with database-stored patterns and early exit
/// Based on tg-spam's similarity detection using TF-IDF vectors with optimizations
/// Engine orchestrates config loading - check manages its own DB access with guardrails
/// </summary>
public class SimilaritySpamCheck(
    ILogger<SimilaritySpamCheck> logger,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITokenizerService tokenizerService) : IContentCheck
{
    private const int MAX_SIMILARITY_SAMPLES = 5_000; // Guardrail: cap similarity query

    public CheckName CheckName => CheckName.Similarity;

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
    private Dictionary<string, int>? _cachedVocabulary; // PERF-CD-4: Pre-indexed vocabulary (word -> index)
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(10);

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
    public async ValueTask<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
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
                logger.LogDebug("Similarity check has no spam samples: Samples={HasSamples}, Count={Count}, Vectors={HasVectors}, Vocab={HasVocab}",
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
            var processedMessage = tokenizerService.RemoveEmojis(req.Message);
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
                logger.LogDebug("Similarity match found for sample {SampleId} (detection count tracking removed in normalized schema)", matchedSampleId);
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
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
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
                logger.LogDebug("Refreshing similarity cache for chat {ChatId}...", chatId);

                // Load spam samples from database with guardrail
                // Phase 4.20+: Use translated text when available (matches spam detection behavior)
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var samples = await (
                    from dr in dbContext.DetectionResults
                    join m in dbContext.Messages on dr.MessageId equals m.MessageId
                    join mt in dbContext.MessageTranslations on m.MessageId equals mt.MessageId into translations
                    from mt in translations.DefaultIfEmpty()
                    where dr.IsSpam && dr.UsedForTraining
                    orderby dr.DetectedAt descending
                    select new
                    {
                        dr.Id,
                        MessageText = mt != null ? mt.TranslatedText : m.MessageText,
                        dr.IsSpam,
                        dr.DetectedAt,
                        dr.DetectionSource,
                        dr.Confidence,
                        m.ChatId,
                        dr.SystemIdentifier
                    }
                )
                .AsNoTracking()
                .Take(MAX_SIMILARITY_SAMPLES) // Guardrail
                .ToListAsync(cancellationToken);

                _cachedSamples = samples.Select(s => new TrainingSample(
                    s.Id,
                    s.MessageText!,
                    s.IsSpam,
                    s.DetectedAt,
                    s.DetectionSource,
                    s.Confidence,
                    [s.ChatId],
                    s.SystemIdentifier ?? "unknown", // Phase 4.19: Actor system (simplified for internal use)
                    0, // DetectionCount removed in normalized schema
                    null // LastDetectedDate removed in normalized schema
                )).ToList();

                logger.LogDebug("Loaded {Count} spam samples from database", _cachedSamples.Count);

                // Build vocabulary from all samples
                _cachedVocabulary = BuildVocabulary(_cachedSamples.Select(s => s.MessageText).ToArray());
                logger.LogDebug("Built vocabulary with {VocabSize} unique words", _cachedVocabulary.Count);

                // Pre-compute TF-IDF vectors for all samples
                _cachedVectors = new Dictionary<long, double[]>();
                foreach (var sample in _cachedSamples)
                {
                    _cachedVectors[sample.Id] = ComputeTfIdfVector(sample.MessageText, _cachedVocabulary);
                }

                _lastCacheUpdate = DateTime.UtcNow;
                logger.LogDebug("Refreshed similarity cache with {Count} samples, {VocabSize} vocab, {VectorCount} vectors for chat {ChatId}",
                    _cachedSamples.Count, _cachedVocabulary.Count, _cachedVectors.Count, chatId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh similarity cache");
                // Keep existing cache if refresh fails
            }
        }
    }

    /// <summary>
    /// Build vocabulary from spam samples with pre-computed indices (PERF-CD-4 optimization)
    /// </summary>
    private Dictionary<string, int> BuildVocabulary(string[] documents)
    {
        // First pass: collect unique words
        var uniqueWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in documents)
        {
            var words = NormalizeAndTokenize(doc);
            foreach (var word in words)
            {
                uniqueWords.Add(word);
            }
        }

        // Second pass: build indexed vocabulary (word -> index)
        var vocabulary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var word in uniqueWords)
        {
            vocabulary[word] = index++;
        }

        return vocabulary;
    }

    /// <summary>
    /// Normalize text and extract tokens (words) using shared tokenizer
    /// </summary>
    private string[] NormalizeAndTokenize(string text)
    {
        // Use shared tokenizer for consistent preprocessing
        var tokens = tokenizerService.Tokenize(text);

        // Additional filtering for similarity analysis
        return tokens.Where(w => w.Length >= 3).ToArray();
    }


    /// <summary>
    /// Compute TF-IDF vector for a document using optimized Dictionary counting (PERF-CD-4)
    /// Benchmark: 2.59× faster, 44% less memory vs GroupBy+ToArray approach
    /// </summary>
    private double[] ComputeTfIdfVector(string document, Dictionary<string, int> vocabularyIndexed)
    {
        var words = NormalizeAndTokenize(document);

        // PERF-CD-4: Use Dictionary for term frequency (faster than GroupBy)
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            wordCounts[word] = wordCounts.GetValueOrDefault(word) + 1;
        }

        var vector = new double[vocabularyIndexed.Count];
        var wordLength = words.Length;

        // PERF-CD-4: Use pre-indexed vocabulary (no ToArray() allocation)
        foreach (var (word, count) in wordCounts)
        {
            if (vocabularyIndexed.TryGetValue(word, out var index))
            {
                // Simple TF-IDF: (term frequency) * log(inverse document frequency)
                // For simplicity, using basic TF and assuming uniform IDF
                vector[index] = (double)count / wordLength;
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
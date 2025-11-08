using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// ML-powered service for generating stop word recommendations
/// Analyzes spam/ham corpus to suggest additions, removals, and performance cleanup
/// Follows ThresholdRecommendationService pattern
/// </summary>
public class StopWordRecommendationService : IStopWordRecommendationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IStopWordsRepository _stopWordsRepository;
    private readonly ITokenizerService _tokenizerService;
    private readonly ILogger<StopWordRecommendationService> _logger;

    // Configuration constants
    private const decimal MinimumSpamFrequencyPercent = 5.0m; // Word must appear in ≥5% of spam samples
    private const decimal MaximumLegitFrequencyPercent = 1.0m; // Word must appear in <1% of legit messages
    private const int MinimumSpamSamples = 50; // Need at least 50 spam samples
    private const int MinimumLegitMessages = 100; // Need at least 100 legit messages
    private const decimal MinimumPrecisionPercent = 70.0m; // Recommend removal if precision <70%
    private const int MinimumTriggers = 5; // Need at least 5 triggers for reliable statistics
    private const int DaysConsideredInactive = 30; // Remove if not triggered in 30 days
    private const decimal PerformanceThresholdMs = 200.0m; // Trigger performance cleanup if >200ms avg

    public StopWordRecommendationService(
        IDbContextFactory<AppDbContext> contextFactory,
        IStopWordsRepository stopWordsRepository,
        ITokenizerService tokenizerService,
        ILogger<StopWordRecommendationService> logger)
    {
        _contextFactory = contextFactory;
        _stopWordsRepository = stopWordsRepository;
        _tokenizerService = tokenizerService;
        _logger = logger;
    }

    public async Task<StopWordRecommendationBatch> GenerateRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting stop word recommendation generation for period {Since} to {Now}",
            since, DateTimeOffset.UtcNow);

        var now = DateTimeOffset.UtcNow;

        // Step 1: Validate data availability
        var validation = await ValidateDataAvailabilityAsync(since, cancellationToken);
        if (validation.ValidationMessage != null)
        {
            _logger.LogWarning("Insufficient data for stop word recommendations: {Message}",
                validation.ValidationMessage);

            return new StopWordRecommendationBatch
            {
                AdditionRecommendations = [],
                RemovalRecommendations = [],
                PerformanceCleanupRecommendations = [],
                AnalysisPeriodStart = since,
                AnalysisPeriodEnd = now,
                CreatedAt = now,
                TotalSpamSamples = validation.SpamSampleCount,
                TotalLegitMessages = validation.LegitMessageCount,
                TotalDetectionResults = validation.DetectionResultCount,
                ValidationMessage = validation.ValidationMessage
            };
        }

        // Step 2: Generate addition recommendations (words to ADD)
        var additionRecommendations = await GenerateAdditionRecommendationsAsync(
            since,
            validation.SpamSampleCount,
            validation.LegitMessageCount,
            cancellationToken);

        // Step 3: Generate removal recommendations (words to REMOVE)
        var removalRecommendations = await GenerateRemovalRecommendationsAsync(
            since,
            cancellationToken);

        // Step 4: Generate performance cleanup recommendations (if applicable)
        var performanceRecommendations = await GeneratePerformanceCleanupRecommendationsAsync(
            since,
            cancellationToken);

        _logger.LogInformation(
            "Generated {AddCount} addition, {RemovalCount} removal, {PerfCount} performance recommendations",
            additionRecommendations.Count,
            removalRecommendations.Count,
            performanceRecommendations.Count);

        return new StopWordRecommendationBatch
        {
            AdditionRecommendations = additionRecommendations,
            RemovalRecommendations = removalRecommendations,
            PerformanceCleanupRecommendations = performanceRecommendations,
            AnalysisPeriodStart = since,
            AnalysisPeriodEnd = now,
            CreatedAt = now,
            CurrentAverageExecutionTimeMs = performanceRecommendations.Count > 0
                ? await GetAverageStopWordsExecutionTimeAsync(since, cancellationToken)
                : null,
            TotalSpamSamples = validation.SpamSampleCount,
            TotalLegitMessages = validation.LegitMessageCount,
            TotalDetectionResults = validation.DetectionResultCount
        };
    }

    /// <summary>
    /// Validate that we have sufficient data for analysis
    /// </summary>
    private async Task<(int SpamSampleCount, int LegitMessageCount, int DetectionResultCount, string? ValidationMessage)>
        ValidateDataAvailabilityAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Count spam training samples (using Message.Timestamp for consistent time window)
        var spamSampleCount = await dbContext.DetectionResults
            .AsNoTracking()
            .Include(d => d.Message)
            .Where(d => d.IsSpam &&
                       d.UsedForTraining &&
                       d.Message != null &&
                       d.Message.Timestamp >= since)
            .CountAsync(cancellationToken);

        // Count legitimate messages (messages without spam detection results)
        var legitMessageCount = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp >= since)
            .Where(m => !dbContext.DetectionResults.Any(d => d.MessageId == m.MessageId && d.IsSpam))
            .CountAsync(cancellationToken);

        // Count total detection results (for precision analysis)
        var detectionResultCount = await dbContext.DetectionResults
            .AsNoTracking()
            .Where(d => d.DetectedAt >= since)
            .CountAsync(cancellationToken);

        // Validate minimum requirements
        if (spamSampleCount < MinimumSpamSamples)
        {
            return (spamSampleCount, legitMessageCount, detectionResultCount,
                $"Insufficient spam samples: {spamSampleCount} found, need at least {MinimumSpamSamples}");
        }

        if (legitMessageCount < MinimumLegitMessages)
        {
            return (spamSampleCount, legitMessageCount, detectionResultCount,
                $"Insufficient legitimate messages: {legitMessageCount} found, need at least {MinimumLegitMessages}");
        }

        return (spamSampleCount, legitMessageCount, detectionResultCount, null);
    }

    /// <summary>
    /// Generate recommendations for words to ADD to stop words list
    /// Algorithm: Extract words from spam, compare frequency to legit messages, rank by ratio
    /// </summary>
    private async Task<List<StopWordAdditionRecommendation>> GenerateAdditionRecommendationsAsync(
        DateTimeOffset since,
        int totalSpamSamples,
        int totalLegitMessages,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1: Load spam training samples (using Message.Timestamp for consistent time window)
        // Use translated text if available (same as ContentDetectionEngine does)
        var spamMessages = await dbContext.DetectionResults
            .AsNoTracking()
            .Include(d => d.Message)
            .Where(d => d.IsSpam &&
                        d.UsedForTraining &&
                        d.Message != null &&
                        d.Message.Timestamp >= since)
            .Select(d => new
            {
                MessageId = d.Message!.MessageId,
                OriginalText = d.Message.MessageText
            })
            .ToListAsync(cancellationToken);

        // Get translations for these messages
        var messageIds = spamMessages.Select(m => m.MessageId).ToList();
        var translations = await dbContext.MessageTranslations
            .AsNoTracking()
            .Where(t => t.MessageId != null && messageIds.Contains(t.MessageId.Value))
            .ToDictionaryAsync(t => t.MessageId!.Value, t => t.TranslatedText, cancellationToken);

        // Use translated text when available, otherwise original
        var spamTexts = spamMessages
            .Select(m => translations.GetValueOrDefault(m.MessageId) ?? m.OriginalText)
            .ToList();

        // Step 2: Extract words from spam corpus
        var spamWordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in spamTexts.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            var words = _tokenizerService.Tokenize(message!);
            foreach (var word in words.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                spamWordCounts.TryGetValue(word, out var count);
                spamWordCounts[word] = count + 1;
            }
        }

        // Step 3: Load legitimate messages and extract words (messages without spam detection results)
        // Use translated text if available (same as ContentDetectionEngine does)
        var legitMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.Timestamp >= since)
            .Where(m => !dbContext.DetectionResults.Any(d => d.MessageId == m.MessageId && d.IsSpam))
            .Select(m => new
            {
                MessageId = m.MessageId,
                OriginalText = m.MessageText
            })
            .ToListAsync(cancellationToken);

        // Get translations for legitimate messages
        var legitMessageIds = legitMessages.Select(m => m.MessageId).ToList();
        var legitTranslations = await dbContext.MessageTranslations
            .AsNoTracking()
            .Where(t => t.MessageId != null && legitMessageIds.Contains(t.MessageId.Value))
            .ToDictionaryAsync(t => t.MessageId!.Value, t => t.TranslatedText, cancellationToken);

        // Use translated text when available, otherwise original
        var legitTexts = legitMessages
            .Select(m => legitTranslations.GetValueOrDefault(m.MessageId) ?? m.OriginalText)
            .ToList();

        var legitWordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in legitTexts.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            var words = _tokenizerService.Tokenize(message!);
            foreach (var word in words.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                legitWordCounts.TryGetValue(word, out var count);
                legitWordCounts[word] = count + 1;
            }
        }

        // Step 4: Get existing stop words to filter them out
        var existingStopWords = new HashSet<string>(
            await _stopWordsRepository.GetAllStopWordsAsync(cancellationToken).ContinueWith(
                t => t.Result.Select(sw => sw.Word),
                cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        // Step 5: Calculate frequencies and filter candidates
        var candidates = new List<StopWordAdditionRecommendation>();

        foreach (var (word, spamCount) in spamWordCounts)
        {
            // Skip if already a stop word
            if (existingStopWords.Contains(word))
                continue;

            // Skip if too short (likely not meaningful)
            if (word.Length < 3)
                continue;

            // Calculate frequencies
            var spamFreqPercent = (decimal)spamCount / totalSpamSamples * 100m;
            var legitCount = legitWordCounts.GetValueOrDefault(word, 0);
            var legitFreqPercent = (decimal)legitCount / totalLegitMessages * 100m;

            // Filter by frequency thresholds
            if (spamFreqPercent < MinimumSpamFrequencyPercent ||
                legitFreqPercent >= MaximumLegitFrequencyPercent)
            {
                continue;
            }

            // Calculate spam-to-legit ratio
            var ratio = spamFreqPercent / (legitFreqPercent + 0.01m); // +0.01 to avoid division by zero

            candidates.Add(new StopWordAdditionRecommendation
            {
                Word = word,
                SpamFrequencyPercent = spamFreqPercent,
                LegitFrequencyPercent = legitFreqPercent,
                SpamToLegitRatio = ratio,
                SpamSampleCount = spamCount,
                LegitSampleCount = legitCount,
                TotalSpamSamples = totalSpamSamples,
                TotalLegitMessages = totalLegitMessages
            });
        }

        // Step 6: Sort by ratio descending (best candidates first) and return top recommendations
        return candidates
            .OrderByDescending(c => c.SpamToLegitRatio)
            .ToList();
    }

    /// <summary>
    /// Generate recommendations for words to REMOVE from stop words list
    /// Algorithm: Analyze precision of each stop word based on detection results
    /// </summary>
    private async Task<List<StopWordRemovalRecommendation>> GenerateRemovalRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 1: Get all current stop words
        var stopWords = (await _stopWordsRepository.GetAllStopWordsAsync(cancellationToken)).ToList();

        // Step 2: Load detection results with check_results_json for StopWords check
        var detectionResults = await dbContext.DetectionResults
            .AsNoTracking()
            .Where(d => d.DetectedAt >= since && d.CheckResultsJson != null)
            .Select(d => new
            {
                d.Id,
                d.IsSpam,
                d.CheckResultsJson,
                d.DetectedAt
            })
            .ToListAsync(cancellationToken);

        // Step 3: Analyze each stop word
        var recommendations = new List<StopWordRemovalRecommendation>();

        foreach (var stopWord in stopWords)
        {
            var wordLower = stopWord.Word.ToLowerInvariant();
            var correctTriggers = 0;
            var falsePositives = 0;
            DateTimeOffset? lastTriggered = null;

            foreach (var result in detectionResults)
            {
                var checkResults = CheckResultsSerializer.Deserialize(result.CheckResultsJson!);
                var stopWordsCheck = checkResults.FirstOrDefault(c => c.CheckName == CheckName.StopWords);

                if (stopWordsCheck == null)
                    continue;

                // Check if this stop word triggered (appears in the Reason field)
                var triggered = stopWordsCheck.Reason?.Contains(wordLower, StringComparison.OrdinalIgnoreCase) ?? false;

                if (!triggered)
                    continue;

                // Update last triggered timestamp
                if (lastTriggered == null || result.DetectedAt > lastTriggered)
                {
                    lastTriggered = result.DetectedAt;
                }

                // Count correct vs false positive
                if (result.IsSpam)
                {
                    correctTriggers++;
                }
                else
                {
                    falsePositives++;
                }
            }

            var totalTriggers = correctTriggers + falsePositives;

            // Skip if never triggered or insufficient data
            if (totalTriggers == 0)
            {
                // Recommend removal if word exists but never triggered (dead weight)
                recommendations.Add(new StopWordRemovalRecommendation
                {
                    Word = stopWord.Word,
                    PrecisionPercent = 0m,
                    TotalTriggers = 0,
                    CorrectTriggers = 0,
                    FalsePositives = 0,
                    LastTriggeredAt = null,
                    DaysSinceLastTrigger = null,
                    RemovalReason = "Never triggered in analysis period (dead weight)"
                });
                continue;
            }

            if (totalTriggers < MinimumTriggers)
            {
                // Skip - insufficient data for reliable statistics
                continue;
            }

            // Calculate precision
            var precisionPercent = (decimal)correctTriggers / totalTriggers * 100m;

            // Calculate days since last trigger
            var daysSinceLastTrigger = lastTriggered.HasValue
                ? (int)(DateTimeOffset.UtcNow - lastTriggered.Value).TotalDays
                : (int?)null;

            // Determine if removal is recommended
            var shouldRemove = false;
            var removalReason = "";

            if (precisionPercent < MinimumPrecisionPercent)
            {
                shouldRemove = true;
                removalReason = $"Low precision ({precisionPercent:F1}%) - causes too many false positives";
            }
            else if (daysSinceLastTrigger.HasValue && daysSinceLastTrigger.Value > DaysConsideredInactive)
            {
                shouldRemove = true;
                removalReason = $"Not triggered in {daysSinceLastTrigger} days (inactive)";
            }

            if (shouldRemove)
            {
                recommendations.Add(new StopWordRemovalRecommendation
                {
                    Word = stopWord.Word,
                    PrecisionPercent = precisionPercent,
                    TotalTriggers = totalTriggers,
                    CorrectTriggers = correctTriggers,
                    FalsePositives = falsePositives,
                    LastTriggeredAt = lastTriggered,
                    DaysSinceLastTrigger = daysSinceLastTrigger,
                    RemovalReason = removalReason
                });
            }
        }

        // Sort by precision ascending (worst performers first)
        return recommendations
            .OrderBy(r => r.PrecisionPercent)
            .ToList();
    }

    /// <summary>
    /// Generate performance-based cleanup recommendations
    /// Triggered when StopWords check average time exceeds threshold
    /// </summary>
    private async Task<List<StopWordPerformanceCleanup>> GeneratePerformanceCleanupRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        // Step 1: Check if performance cleanup is needed
        var avgExecutionTime = await GetAverageStopWordsExecutionTimeAsync(since, cancellationToken);

        if (!avgExecutionTime.HasValue || avgExecutionTime.Value <= PerformanceThresholdMs)
        {
            // Performance is acceptable - no cleanup needed
            return [];
        }

        _logger.LogInformation(
            "StopWords check average execution time ({AvgMs}ms) exceeds threshold ({ThresholdMs}ms) - generating performance cleanup recommendations",
            avgExecutionTime.Value, PerformanceThresholdMs);

        // Step 2: Reuse removal recommendations logic to get word statistics
        var removalRecommendations = await GenerateRemovalRecommendationsAsync(since, cancellationToken);

        // Step 3: Calculate inefficiency score for each word
        var performanceRecommendations = new List<StopWordPerformanceCleanup>();

        foreach (var removal in removalRecommendations)
        {
            if (removal.TotalTriggers == 0)
                continue; // Skip words that never triggered

            // Calculate false positive rate
            var falsePositiveRate = (decimal)removal.FalsePositives / removal.TotalTriggers;

            // Estimate execution cost (simplified - assume linear with trigger count)
            var executionCost = (decimal)removal.TotalTriggers;

            // Calculate inefficiency score
            var inefficientScore = (falsePositiveRate * executionCost) / (removal.PrecisionPercent / 100m + 0.01m);

            // Estimate time savings (simplified - proportional to trigger count)
            var estimatedSavingsMs = avgExecutionTime.Value * ((decimal)removal.TotalTriggers / 1000m);

            performanceRecommendations.Add(new StopWordPerformanceCleanup
            {
                Word = removal.Word,
                PrecisionPercent = removal.PrecisionPercent,
                FalsePositives = removal.FalsePositives,
                TotalTriggers = removal.TotalTriggers,
                InefficientScore = inefficientScore,
                EstimatedTimeSavingsMs = estimatedSavingsMs
            });
        }

        // Sort by inefficiency score descending (most inefficient first)
        // Take top N to bring execution time back under threshold
        return performanceRecommendations
            .OrderByDescending(p => p.InefficientScore)
            .Take(10) // Limit to top 10 worst performers
            .ToList();
    }

    /// <summary>
    /// Get average StopWords check execution time from check_results_json
    /// Returns null if no data available
    /// </summary>
    private async Task<decimal?> GetAverageStopWordsExecutionTimeAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Note: This requires ProcessingTimeMs to be populated in check_results_json
        // If not available, return null
        // This is a placeholder - actual implementation would parse JSONB to extract ProcessingTimeMs

        // For now, return null as ProcessingTimeMs tracking is part of ML-5
        // This service can still generate recommendations without performance cleanup
        return null;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 similarity check with proper abstention
/// Scoring: 0-2.5 points based on similarity (research: high=2.5, medium=1.5)
/// Abstain when similarity <0.3 or no samples
/// </summary>
#pragma warning disable CS9113 // Parameter is unread (will be used when full implementation added)
public class SimilaritySpamCheckV2(
    ILogger<SimilaritySpamCheckV2> logger,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITokenizerService tokenizerService) : IContentCheckV2
#pragma warning restore CS9113
{
    private const int MAX_SIMILARITY_SAMPLES = 5_000;
    private const double AbstentionThreshold = 0.3; // <30% similarity = abstain

    public CheckName CheckName => CheckName.Similarity;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip expensive database queries for trusted/admin users
        // Similarity is not a critical check - it requires database queries and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping Similarity check for user {UserId}: User is {UserType}",
                request.UserId,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (SimilarityCheckRequest)request;

        try
        {
            if (req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Message too short (< {req.MinMessageLength} chars)"
                };
            }

            // Load spam samples (reuse V1 query logic - too complex to duplicate here)
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(req.CancellationToken);

            var spamSamples = await (
                from dr in dbContext.DetectionResults
                join m in dbContext.Messages on dr.MessageId equals m.MessageId
                join mt in dbContext.MessageTranslations on m.MessageId equals mt.MessageId into translations
                from mt in translations.DefaultIfEmpty()
                where dr.UsedForTraining && dr.IsSpam
                orderby dr.DetectedAt descending
                select mt != null ? mt.TranslatedText : m.MessageText
            ).AsNoTracking().Take(MAX_SIMILARITY_SAMPLES).ToListAsync(req.CancellationToken);

            if (!spamSamples.Any())
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No spam samples available"
                };
            }

            // Calculate max similarity (simplified - full implementation would use TF-IDF)
            var maxSimilarity = 0.0;
            foreach (var sample in spamSamples.Take(100)) // Check first 100 for performance
            {
                var similarity = CalculateSimpleSimilarity(req.Message, sample);
                if (similarity > maxSimilarity)
                    maxSimilarity = similarity;
            }

            // V2: Abstain if similarity too low
            if (maxSimilarity < AbstentionThreshold)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Low similarity ({maxSimilarity:P0}) to spam samples"
                };
            }

            // Map similarity to score (research: 80%+=2.5, 60-80%=1.5)
            var score = maxSimilarity switch
            {
                >= 0.8 => 2.5,
                >= 0.6 => 1.5,
                >= 0.4 => 1.0,
                _ => 0.5
            };

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = $"Similarity: {maxSimilarity:P0} to known spam"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SimilaritySpamCheckV2");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex
            };
        }
    }

    private static double CalculateSimpleSimilarity(string msg1, string msg2)
    {
        // Simple Jaccard similarity (proper implementation would use TF-IDF)
        var words1 = msg1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = msg2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }
}

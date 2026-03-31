using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for retrieving ML training data.
/// Handles complex multi-table queries and DTO-to-model conversion.
/// Scoped lifetime - matches standard pattern used by all other repositories.
/// </summary>
public class MLTrainingDataRepository(
    AppDbContext context,
    SimHashService simHashService,
    ILogger<MLTrainingDataRepository> logger) : IMLTrainingDataRepository
{

    public async Task<List<TrainingSample>> GetSpamSamplesAsync(CancellationToken cancellationToken = default)
    {
        // Explicit spam labels (admin decisions override auto-detection)
        // Note: OrderByDescending ensures deterministic results when multiple translations exist
        var explicitSpam = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Spam)
            .Join(context.Messages,
                  tl => new { tl.MessageId, tl.ChatId },
                  m => new { m.MessageId, m.ChatId },
                  (tl, m) => new { tl, m })
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       x => new { MessageId = (int?)x.m.MessageId, ChatId = (long?)x.m.ChatId },
                       mt => new { mt.MessageId, mt.ChatId },
                       (x, mts) => new { x.tl, x.m, mt = mts.OrderByDescending(t => t.TranslatedAt).FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.tl.MessageId,
                x.tl.ChatId,
                x.tl.LabeledByUserId,
                x.tl.LabeledAt
            })
            .Where(x => x.Text != null && x.Text.Length > MLConstants.MinTextLength)
            .ToListAsync(cancellationToken);

        // Implicit spam (high-confidence auto, not corrected) - use passed-in labeled IDs to avoid duplicate query
        var implicitSpam = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.IsSpam && dr.UsedForTraining && !context.TrainingLabels.Any(tl => tl.MessageId == dr.MessageId && tl.ChatId == dr.ChatId))
            .Join(context.Messages,
                  dr => new { dr.MessageId, dr.ChatId },
                  m => new { m.MessageId, m.ChatId },
                  (dr, m) => m)
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       m => new { MessageId = (int?)m.MessageId, ChatId = (long?)m.ChatId },
                       mt => new { mt.MessageId, mt.ChatId },
                       (m, mts) => new { m, mt = mts.OrderByDescending(t => t.TranslatedAt).FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.m.MessageId,
                x.m.ChatId
            })
            .Where(x => x.Text != null && x.Text.Length > MLConstants.MinTextLength)
            .ToListAsync(cancellationToken);

        // Convert to domain models using collection expressions
        List<TrainingSample> samples = [
            ..explicitSpam.Select(x => new TrainingSample
            {
                Text = x.Text!,
                Label = TrainingLabel.Spam,
                Source = TrainingSampleSource.Explicit,
                MessageId = x.MessageId,
                ChatId = x.ChatId,
                LabeledByUserId = x.LabeledByUserId,
                LabeledAt = x.LabeledAt
            }),
            ..implicitSpam.Select(x => new TrainingSample
            {
                Text = x.Text!,
                Label = TrainingLabel.Spam,
                Source = TrainingSampleSource.Implicit,
                MessageId = x.MessageId,
                ChatId = x.ChatId,
                LabeledByUserId = null,
                LabeledAt = null
            })
        ];

        logger.LogInformation(
            "Loaded {Count} spam training samples ({Explicit} explicit + {Implicit} implicit)",
            samples.Count, explicitSpam.Count, implicitSpam.Count);

        // Training-time deduplication: Remove near-duplicate spam samples to prevent model bias
        return DeduplicateSamples(samples, "spam");
    }

    public async Task<List<TrainingSample>> GetHamSamplesAsync(int spamCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spamCount);

        // Calculate target ham count for balance
        // Dynamic ham cap: maintains ~30% spam ratio
        // Formula: if spamCount = S, hamCount = H, then S/(S+H) = 0.3
        // Solving for H: H = S * (1-0.3)/0.3 = S * 2.33
        var dynamicHamCap = (int)(spamCount * MLConstants.HamMultiplier);

        // Explicit ham labels (admin corrections) - ALWAYS included, fetch all then dedupe
        var explicitHamRaw = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Ham)
            .Join(context.Messages,
                  tl => new { tl.MessageId, tl.ChatId },
                  m => new { m.MessageId, m.ChatId },
                  (tl, m) => new { tl, m })
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       x => new { MessageId = (int?)x.m.MessageId, ChatId = (long?)x.m.ChatId },
                       mt => new { mt.MessageId, mt.ChatId },
                       (x, mts) => new { x.tl, x.m, mt = mts.OrderByDescending(t => t.TranslatedAt).FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.tl.MessageId,
                x.tl.ChatId,
                x.tl.LabeledByUserId,
                x.tl.LabeledAt
            })
            .Where(x => x.Text != null && x.Text.Length > MLConstants.MinTextLength)
            .ToListAsync(cancellationToken);

        // Convert explicit ham to samples for deduplication
        var explicitHamSamples = explicitHamRaw.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Ham,
            Source = TrainingSampleSource.Explicit,
            MessageId = x.MessageId,
            ChatId = x.ChatId,
            LabeledByUserId = x.LabeledByUserId,
            LabeledAt = x.LabeledAt
        }).ToList();

        // Deduplicate explicit ham FIRST, then cap
        var explicitHamDeduped = DeduplicateSamples(explicitHamSamples, "explicit ham");

        // Cap explicit ham to maintain balance (prefer longer samples)
        List<TrainingSample> explicitHam;
        if (explicitHamDeduped.Count > dynamicHamCap)
        {
            logger.LogDebug(
                "Deduplicated explicit ham ({Count}) exceeds balance cap ({Cap}). " +
                "Capping to {Used} longest messages to maintain {TargetRatio:P0} spam ratio.",
                explicitHamDeduped.Count, dynamicHamCap, dynamicHamCap,
                SpamClassifierMetadata.TargetSpamRatio);

            explicitHam = explicitHamDeduped
                .OrderByDescending(x => x.Text.Length)
                .Take(dynamicHamCap)
                .ToList();
        }
        else
        {
            explicitHam = explicitHamDeduped;
        }

        // Calculate how many implicit ham we need after explicit ham
        var maxImplicitHam = Math.Max(dynamicHamCap - explicitHam.Count, 0);

        // Implicit ham: fetch a capped set of candidates from the database, dedupe in memory, then cap
        // Over-fetch by 3x to account for SimHash deduplication removing ~30-50% of samples
        // Uses ix_messages_text_length expression index for efficient sorting
        // NOT EXISTS correlated subqueries use composite (MessageId, ChatId) to prevent cross-chat data leakage
        var implicitHamRaw = await (
            from m in context.Messages.AsNoTracking()
            where !context.TrainingLabels.Any(tl => tl.MessageId == m.MessageId && tl.ChatId == m.ChatId)
               && !context.DetectionResults.Any(dr => dr.MessageId == m.MessageId && dr.ChatId == m.ChatId && dr.IsSpam)
               && m.DeletedAt == null  // Message-level filter (better signal than user-level ban)
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.ChatId == m.ChatId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MLConstants.MinTextLength
            orderby text.Length descending  // Sort by LENGTH (uses expression index)
            select new { Text = text, m.MessageId, m.ChatId }
        ).Take(maxImplicitHam * 3).ToListAsync(cancellationToken);

        // Convert to samples for deduplication
        var implicitHamSamples = implicitHamRaw.Select(x => new TrainingSample
        {
            Text = x.Text,
            Label = TrainingLabel.Ham,
            Source = TrainingSampleSource.Implicit,
            MessageId = x.MessageId,
            ChatId = x.ChatId,
            LabeledByUserId = null,
            LabeledAt = null
        }).ToList();

        // Deduplicate implicit ham, then cap to needed amount
        var implicitHamDeduped = DeduplicateSamples(implicitHamSamples, "implicit ham");
        var implicitHam = implicitHamDeduped.Take(maxImplicitHam).ToList();

        // Combine explicit and implicit ham
        List<TrainingSample> samples = [.. explicitHam, .. implicitHam];

        var totalHam = explicitHam.Count + implicitHam.Count;
        var totalSamples = spamCount + totalHam;
        var spamRatio = totalSamples > 0 ? (double)spamCount / totalSamples : 0.0;

        logger.LogInformation(
            "Loaded {Count} ham training samples ({Explicit} explicit + {Implicit} implicit, capped at {Cap} for balance). " +
            "Total dataset: {Spam} spam + {Ham} ham = {Total} samples ({SpamRatio:P1} spam ratio, balanced: {Balanced})",
            samples.Count, explicitHam.Count, implicitHam.Count, dynamicHamCap,
            spamCount, totalHam, totalSamples, spamRatio,
            spamRatio >= SpamClassifierMetadata.MinBalancedSpamRatio && spamRatio <= SpamClassifierMetadata.MaxBalancedSpamRatio);

        return samples;
    }

    public async Task<TrainingBalanceStats> GetTrainingBalanceStatsAsync(CancellationToken cancellationToken = default)
    {
        // Call the same methods that training uses to ensure UI shows accurate counts
        // This includes deduplication, so UI matches exactly what model training will use
        var spamSamples = await GetSpamSamplesAsync(cancellationToken);
        var hamSamples = await GetHamSamplesAsync(spamSamples.Count, cancellationToken);

        // Count by source (explicit vs implicit)
        var explicitSpamCount = spamSamples.Count(s => s.Source == TrainingSampleSource.Explicit);
        var implicitSpamCount = spamSamples.Count(s => s.Source == TrainingSampleSource.Implicit);
        var explicitHamCount = hamSamples.Count(s => s.Source == TrainingSampleSource.Explicit);
        var implicitHamCount = hamSamples.Count(s => s.Source == TrainingSampleSource.Implicit);

        return new TrainingBalanceStats
        {
            ExplicitSpamCount = explicitSpamCount,
            ImplicitSpamCount = implicitSpamCount,
            ExplicitHamCount = explicitHamCount,
            ImplicitHamCount = implicitHamCount
        };
    }

    /// <summary>
    /// Deduplicate training samples using SimHash fingerprints.
    /// Groups similar hashes together, keeping one representative per group.
    /// Prefers longer samples (better training signal).
    /// </summary>
    /// <param name="samples">Samples to deduplicate</param>
    /// <param name="label">Label for logging ("spam" or "ham")</param>
    /// <returns>Deduplicated samples</returns>
    private List<TrainingSample> DeduplicateSamples(List<TrainingSample> samples, string label)
    {
        if (samples.Count == 0)
            return samples;

        var deduplicated = new List<TrainingSample>();
        var usedHashes = new List<long>();

        // Sort by text length descending (prefer longer samples = better training signal)
        foreach (var sample in samples.OrderByDescending(s => s.Text.Length))
        {
            var hash = simHashService.ComputeHash(sample.Text);
            if (hash == 0)
            {
                // Empty/short text has no hash to compare - keep it
                deduplicated.Add(sample);
                continue;
            }

            // Check if similar hash already in deduplicated set
            var isDuplicate = usedHashes.Any(h => simHashService.AreSimilar(h, hash));
            if (!isDuplicate)
            {
                deduplicated.Add(sample);
                usedHashes.Add(hash);
            }
        }

        var removed = samples.Count - deduplicated.Count;
        if (removed > 0)
        {
            logger.LogDebug(
                "Deduplicated {Original} {Label} samples to {Deduplicated} ({Removed} near-duplicates removed)",
                samples.Count, label, deduplicated.Count, removed);
        }

        return deduplicated;
    }
}

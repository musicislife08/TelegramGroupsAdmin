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
        // Current verdict per message (latest event wins): ExplicitSpam and ImplicitSpam train as spam.
        var spamRows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where VerdictClassifications.TrainingSpamValues.Contains(v.Classification)
            join dr in context.DetectionResults.AsNoTracking() on v.VerdictId equals (long?)dr.Id
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            let mt = context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .OrderByDescending(t => t.TranslatedAt)
                .FirstOrDefault()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MLConstants.MinTextLength
            select new { Text = text, m.MessageId, m.ChatId, v.Classification, dr.TelegramUserId, dr.DetectedAt }
        ).ToListAsync(cancellationToken);

        List<TrainingSample> samples = [.. spamRows.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Spam,
            Source = x.Classification == (int)VerdictClassification.ExplicitSpam ? TrainingSampleSource.Explicit : TrainingSampleSource.Implicit,
            MessageId = x.MessageId,
            ChatId = x.ChatId,
            LabeledByUserId = x.Classification == (int)VerdictClassification.ExplicitSpam ? x.TelegramUserId : null,
            LabeledAt = x.Classification == (int)VerdictClassification.ExplicitSpam ? x.DetectedAt : null
        })];

        logger.LogInformation(
            "Loaded {Count} spam training samples ({Explicit} explicit + {Implicit} implicit)",
            samples.Count,
            samples.Count(s => s.Source == TrainingSampleSource.Explicit),
            samples.Count(s => s.Source == TrainingSampleSource.Implicit));

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

        // Explicit ham (current verdict, latest event wins) - ALWAYS included, fetch all then dedupe
        var explicitHamRaw = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where v.Classification == (int)VerdictClassification.ExplicitHam
            join dr in context.DetectionResults.AsNoTracking() on v.VerdictId equals (long?)dr.Id
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            let mt = context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .OrderByDescending(t => t.TranslatedAt)
                .FirstOrDefault()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MLConstants.MinTextLength
            select new { Text = text, m.MessageId, m.ChatId, dr.TelegramUserId, dr.DetectedAt }
        ).ToListAsync(cancellationToken);

        // Convert explicit ham to samples for deduplication
        var explicitHamSamples = explicitHamRaw.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Ham,
            Source = TrainingSampleSource.Explicit,
            MessageId = x.MessageId,
            ChatId = x.ChatId,
            LabeledByUserId = x.TelegramUserId,
            LabeledAt = x.DetectedAt
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
        // Current verdict per message (latest event wins): ImplicitHam and Unscanned train as ham.
        var implicitHamRaw = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where v.Classification == (int)VerdictClassification.ImplicitHam
               || v.Classification == (int)VerdictClassification.Unscanned
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.DeletedAt == null  // Message-level filter (better signal than user-level ban)
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

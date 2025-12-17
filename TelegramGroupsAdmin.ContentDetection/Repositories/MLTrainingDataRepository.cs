using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for retrieving ML training data.
/// Handles complex multi-table queries and DTO-to-model conversion.
/// </summary>
public class MLTrainingDataRepository : IMLTrainingDataRepository
{
    // Training data quality filters
    private const int MinTextLength = 10;  // Minimum text length for ML training
    private const int MinHamWordCount = 50;  // Minimum word count for quality ham messages
    private const double HamMultiplier = 2.33;  // Ham multiplier to maintain ~30% spam ratio
    private const int CandidateFetchMultiplier = 10;  // Fetch 10x candidates to account for ~90% rejection rate from MinHamWordCount filter (empirical observation)
    private const int MinCandidatesToFetch = 5000;  // Always fetch at least 5000 candidates for diversity

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MLTrainingDataRepository> _logger;

    public MLTrainingDataRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MLTrainingDataRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<HashSet<long>> GetLabeledMessageIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await context.TrainingLabels
            .AsNoTracking()
            .Select(tl => tl.MessageId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<List<TrainingSample>> GetSpamSamplesAsync(HashSet<long> labeledMessageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Explicit spam labels (admin decisions override auto-detection)
        var explicitSpam = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Spam)
            .Join(context.Messages, tl => tl.MessageId, m => m.MessageId, (tl, m) => new { tl, m })
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       x => x.m.MessageId, mt => mt.MessageId,
                       (x, mts) => new { x.tl, x.m, mt = mts.FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.tl.MessageId,
                x.tl.LabeledByUserId,
                x.tl.LabeledAt
            })
            .Where(x => x.Text != null && x.Text.Length > MinTextLength)
            .ToListAsync(cancellationToken);

        // Implicit spam (high-confidence auto, not corrected) - use passed-in labeled IDs to avoid duplicate query
        var implicitSpam = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.IsSpam && dr.UsedForTraining && !labeledMessageIds.Contains(dr.MessageId))
            .Join(context.Messages, dr => dr.MessageId, m => m.MessageId, (dr, m) => m)
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       m => m.MessageId, mt => mt.MessageId,
                       (m, mts) => new { m, mt = mts.FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.m.MessageId
            })
            .Where(x => x.Text != null && x.Text.Length > MinTextLength)
            .ToListAsync(cancellationToken);

        // Convert to domain models using collection expressions
        List<TrainingSample> samples = [
            ..explicitSpam.Select(x => new TrainingSample
            {
                Text = x.Text!,
                Label = TrainingLabel.Spam,
                Source = TrainingSampleSource.Explicit,
                MessageId = x.MessageId,
                LabeledByUserId = x.LabeledByUserId,
                LabeledAt = x.LabeledAt
            }),
            ..implicitSpam.Select(x => new TrainingSample
            {
                Text = x.Text!,
                Label = TrainingLabel.Spam,
                Source = TrainingSampleSource.Implicit,
                MessageId = x.MessageId,
                LabeledByUserId = null,
                LabeledAt = null
            })
        ];

        _logger.LogInformation(
            "Loaded {Count} spam training samples ({Explicit} explicit + {Implicit} implicit)",
            samples.Count, explicitSpam.Count, implicitSpam.Count);

        return samples;
    }

    public async Task<List<TrainingSample>> GetHamSamplesAsync(int spamCount, HashSet<long> labeledMessageIds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spamCount);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Explicit ham labels (admin corrections) - ALWAYS included
        var explicitHam = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Ham)
            .Join(context.Messages, tl => tl.MessageId, m => m.MessageId, (tl, m) => new { tl, m })
            .GroupJoin(context.MessageTranslations.Where(mt => mt.EditId == null),
                       x => x.m.MessageId, mt => mt.MessageId,
                       (x, mts) => new { x.tl, x.m, mt = mts.FirstOrDefault() })
            .Select(x => new
            {
                Text = x.mt != null ? x.mt.TranslatedText : x.m.MessageText,
                x.tl.MessageId,
                x.tl.LabeledByUserId,
                x.tl.LabeledAt
            })
            .Where(x => x.Text != null && x.Text.Length > MinTextLength)
            .ToListAsync(cancellationToken);

        // Calculate how many explicit ham we can use while maintaining balance
        // Dynamic ham cap: maintains ~30% spam ratio
        // Formula: if spamCount = S, hamCount = H, then S/(S+H) = 0.3
        // Solving for H: H = S * (1-0.3)/0.3 = S * 2.33
        var dynamicHamCap = (int)(spamCount * HamMultiplier);
        var explicitHamToUse = Math.Min(explicitHam.Count, dynamicHamCap);

        // If explicit ham exceeds cap, sort by message length and take top samples
        if (explicitHam.Count > dynamicHamCap)
        {
            _logger.LogDebug(
                "Explicit ham labels ({ExplicitCount}) exceed balance cap ({Cap}). " +
                "Capping to {Used} longest messages to maintain {TargetRatio:P0} spam ratio.",
                explicitHam.Count, dynamicHamCap, explicitHamToUse,
                SpamClassifierMetadata.TargetSpamRatio);

            explicitHam = explicitHam
                .OrderByDescending(x => x.Text!.Length)  // Longest first = better training signal
                .Take(explicitHamToUse)
                .ToList();
        }

        // Calculate how many implicit ham we need after explicit ham is capped
        var maxImplicitHam = Math.Max(dynamicHamCap - explicitHam.Count, 0);

        // Materialize spam exclusion set once (avoids N+1 correlated subqueries)
        var spamDetectedMessageIds = (await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.IsSpam)
            .Select(dr => dr.MessageId).ToListAsync(cancellationToken)).ToHashSet();

        // Implicit ham: longest non-deleted messages (naturally high word count, best training signal)
        // Uses ix_messages_text_length expression index for efficient sorting
        var implicitHam = await (
            from m in context.Messages.AsNoTracking()
            where !labeledMessageIds.Contains(m.MessageId)
               && !spamDetectedMessageIds.Contains(m.MessageId)
               && m.DeletedAt == null  // Message-level filter (better signal than user-level ban)
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MinTextLength
            orderby text.Length descending  // Sort by LENGTH (uses expression index)
            select new { Text = text, m.MessageId }
        ).Take(maxImplicitHam).ToListAsync(cancellationToken);

        // Convert to domain models using collection expressions
        List<TrainingSample> samples = [
            ..explicitHam.Select(x => new TrainingSample
            {
                Text = x.Text!,
                Label = TrainingLabel.Ham,
                Source = TrainingSampleSource.Explicit,
                MessageId = x.MessageId,
                LabeledByUserId = x.LabeledByUserId,
                LabeledAt = x.LabeledAt
            }),
            ..implicitHam.Select(x => new TrainingSample
            {
                Text = x.Text,
                Label = TrainingLabel.Ham,
                Source = TrainingSampleSource.Implicit,
                MessageId = x.MessageId,
                LabeledByUserId = null,
                LabeledAt = null
            })
        ];

        var totalHam = explicitHam.Count + implicitHam.Count;
        var totalSamples = spamCount + totalHam;
        var spamRatio = totalSamples > 0 ? (double)spamCount / totalSamples : 0.0;

        _logger.LogInformation(
            "Loaded {Count} ham training samples ({Explicit} explicit + {Implicit} implicit, capped at {Cap} for balance). " +
            "Total dataset: {Spam} spam + {Ham} ham = {Total} samples ({SpamRatio:P1} spam ratio, balanced: {Balanced})",
            samples.Count, explicitHam.Count, implicitHam.Count, dynamicHamCap,
            spamCount, totalHam, totalSamples, spamRatio,
            spamRatio >= SpamClassifierMetadata.MinBalancedSpamRatio && spamRatio <= SpamClassifierMetadata.MaxBalancedSpamRatio);

        return samples;
    }

    public async Task<TrainingBalanceStats> GetTrainingBalanceStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get labeled message IDs (reuse existing method logic)
        var labeledMessageIds = await GetLabeledMessageIdsAsync(cancellationToken);

        // Count explicit spam labels
        var explicitSpamCount = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Spam)
            .CountAsync(cancellationToken);

        // Count implicit spam (high-confidence auto-detected, not manually labeled)
        var implicitSpamCount = await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.IsSpam && dr.UsedForTraining && !labeledMessageIds.Contains(dr.MessageId))
            .CountAsync(cancellationToken);

        var totalSpamCount = explicitSpamCount + implicitSpamCount;

        // Count explicit ham labels
        var explicitHamCount = await context.TrainingLabels
            .AsNoTracking()
            .Where(tl => tl.Label == (short)TrainingLabel.Ham)
            .CountAsync(cancellationToken);

        // Calculate implicit ham count using same balance logic as training
        var dynamicHamCap = (int)(totalSpamCount * HamMultiplier);
        var maxImplicitHam = Math.Max(dynamicHamCap - explicitHamCount, 0);

        // Count available implicit ham candidates (after filters)
        var spamDetectedMessageIds = (await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.IsSpam)
            .Select(dr => dr.MessageId).ToListAsync(cancellationToken)).ToHashSet();

        var availableImplicitHam = await (
            from m in context.Messages.AsNoTracking()
            where !labeledMessageIds.Contains(m.MessageId)
               && !spamDetectedMessageIds.Contains(m.MessageId)
               && m.DeletedAt == null
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MinTextLength
            select m.MessageId
        ).CountAsync(cancellationToken);

        // Actual implicit ham used is minimum of cap and available
        var implicitHamCount = Math.Min(maxImplicitHam, availableImplicitHam);

        return new TrainingBalanceStats
        {
            ExplicitSpamCount = explicitSpamCount,
            ImplicitSpamCount = implicitSpamCount,
            ExplicitHamCount = explicitHamCount,
            ImplicitHamCount = implicitHamCount
        };
    }
}

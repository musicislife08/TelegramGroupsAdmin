using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private const int CandidateFetchMultiplier = 10;  // Fetch 10x candidates (≥50 word filter has ~90% rejection rate)
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

        // Convert to domain models
        var samples = new List<TrainingSample>();

        samples.AddRange(explicitSpam.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Spam,
            Source = TrainingSampleSource.Explicit,
            MessageId = x.MessageId,
            LabeledByUserId = x.LabeledByUserId,
            LabeledAt = x.LabeledAt
        }));

        samples.AddRange(implicitSpam.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Spam,
            Source = TrainingSampleSource.Implicit,
            MessageId = x.MessageId,
            LabeledByUserId = null,
            LabeledAt = null
        }));

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

        // Calculate dynamic cap to maintain balanced ratio (target: ~30% spam)
        var dynamicHamCap = (int)(spamCount * HamMultiplier);
        var maxImplicitHam = Math.Max(dynamicHamCap - explicitHam.Count, 0);

        // Implicit ham (never flagged, quality filtered, dynamically capped)
        var candidatesToFetch = Math.Max(maxImplicitHam * CandidateFetchMultiplier, MinCandidatesToFetch);

        // Materialize exclusion sets once (avoids N+1 correlated subqueries) - reuse passed-in labeled IDs
        var spamDetectedMessageIds = (await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.IsSpam)
            .Select(dr => dr.MessageId).ToListAsync(cancellationToken)).ToHashSet();
        var bannedUserIds = (await context.TelegramUsers.AsNoTracking()
            .Where(tu => tu.IsBanned)
            .Select(tu => tu.TelegramUserId).ToListAsync(cancellationToken)).ToHashSet();

        var candidateMessages = await (
            from m in context.Messages.AsNoTracking()
            where !labeledMessageIds.Contains(m.MessageId)
               && !spamDetectedMessageIds.Contains(m.MessageId)
               && !bannedUserIds.Contains(m.UserId)
            from mt in context.MessageTranslations
                .Where(mt => mt.MessageId == m.MessageId && mt.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text.Length > MinTextLength
            orderby m.MessageId  // Deterministic ordering
            select new { Text = text, m.MessageId }
        ).Take(candidatesToFetch).ToListAsync(cancellationToken);

        // Client-side filtering for word count (can't be translated to SQL)
        var implicitHam = candidateMessages
            .Where(x => x.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinHamWordCount)
            .Take(maxImplicitHam)
            .ToList();

        // Convert to domain models
        var samples = new List<TrainingSample>();

        samples.AddRange(explicitHam.Select(x => new TrainingSample
        {
            Text = x.Text!,
            Label = TrainingLabel.Ham,
            Source = TrainingSampleSource.Explicit,
            MessageId = x.MessageId,
            LabeledByUserId = x.LabeledByUserId,
            LabeledAt = x.LabeledAt
        }));

        samples.AddRange(implicitHam.Select(x => new TrainingSample
        {
            Text = x.Text,
            Label = TrainingLabel.Ham,
            Source = TrainingSampleSource.Implicit,
            MessageId = x.MessageId,
            LabeledByUserId = null,
            LabeledAt = null
        }));

        _logger.LogInformation(
            "Loaded {Count} ham training samples ({Explicit} explicit + {Implicit} implicit, capped at {Cap} for balance)",
            samples.Count, explicitHam.Count, implicitHam.Count, dynamicHamCap);

        return samples;
    }
}

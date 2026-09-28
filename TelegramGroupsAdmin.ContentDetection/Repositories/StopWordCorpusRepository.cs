using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for the spam/ham/scan corpora consumed by stop word recommendation generation.
/// Every corpus follows each message's current verdict (message_verdicts view).
/// </summary>
public class StopWordCorpusRepository(IDbContextFactory<AppDbContext> contextFactory) : IStopWordCorpusRepository
{
    public async Task<StopWordCorpusCounts> GetCountsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var spam = await (from v in context.MessageVerdicts
                          where VerdictClassifications.TrainingSpamValues.Contains(v.Classification)
                          join m in context.Messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
                          where m.Timestamp >= since
                          select v.MessageId).CountAsync(cancellationToken);
        var legit = await (from v in context.MessageVerdicts
                           where !v.IsSpam
                           join m in context.Messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
                           where m.Timestamp >= since
                           select v.MessageId).CountAsync(cancellationToken);
        var scans = await context.DetectionResults
            .CountAsync(d => d.Source == (int)VerdictSource.ContentScan && d.DetectedAt >= since, cancellationToken);
        return new StopWordCorpusCounts(spam, legit, scans);
    }

    public Task<IReadOnlyList<string>> GetSpamTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => GetTextsAsync(since, spam: true, cancellationToken);

    public Task<IReadOnlyList<string>> GetLegitTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => GetTextsAsync(since, spam: false, cancellationToken);

    private async Task<IReadOnlyList<string>> GetTextsAsync(DateTimeOffset since, bool spam, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var texts = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where spam ? VerdictClassifications.TrainingSpamValues.Contains(v.Classification) : !v.IsSpam
            join m in context.Messages.AsNoTracking() on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            where m.Timestamp >= since
            from mt in context.MessageTranslations
                .Where(t => t.MessageId == m.MessageId && t.ChatId == m.ChatId && t.EditId == null)
                .DefaultIfEmpty()
            let text = mt != null ? mt.TranslatedText : m.MessageText
            where text != null && text != ""
            select text
        ).ToListAsync(cancellationToken);
        return texts!;
    }

    public async Task<IReadOnlyList<ScanCheckResults>> GetScanCheckResultsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.DetectionResults.AsNoTracking()
            .Where(d => d.Source == (int)VerdictSource.ContentScan && d.DetectedAt >= since && d.CheckResultsJson != null)
            .Select(d => new { d.Id, d.Classification, d.CheckResultsJson, d.DetectedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ScanCheckResults(r.Id, ((VerdictClassification)r.Classification!.Value).IsSpam(), r.CheckResultsJson!, r.DetectedAt)).ToList();
    }
}

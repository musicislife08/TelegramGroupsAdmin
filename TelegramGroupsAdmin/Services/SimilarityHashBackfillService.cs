using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// One-time backfill of similarity hashes for existing messages and translations.
/// Called from RunDatabaseMigrationsAsync, runs during --migrate-only.
/// Self-skipping: checks if backfill already complete.
/// </summary>
public class SimilarityHashBackfillService(
    IDbContextFactory<AppDbContext> contextFactory,
    SimHashService simHashService,
    ILogger<SimilarityHashBackfillService> logger)
{
    /// <summary>
    /// Backfills similarity hashes for all messages and translations that don't have one.
    /// Skips if backfill already complete.
    /// </summary>
    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Check if backfill needed (any messages OR translations without hash)
        var messagesNeedBackfill = await context.Messages
            .AnyAsync(m => m.SimilarityHash == null && m.MessageText != null, cancellationToken);
        var translationsNeedBackfill = await context.MessageTranslations
            .AnyAsync(mt => mt.SimilarityHash == null && mt.TranslatedText != null, cancellationToken);

        if (!messagesNeedBackfill && !translationsNeedBackfill)
        {
            logger.LogInformation("Similarity hash backfill already complete, skipping");
            return;
        }

        logger.LogInformation("Starting similarity hash backfill...");
        var sw = Stopwatch.StartNew();

        var messageCount = await BackfillMessagesAsync(context, cancellationToken);
        var translationCount = await BackfillTranslationsAsync(context, cancellationToken);

        logger.LogInformation(
            "Similarity hash backfill complete: {Messages} messages, {Translations} translations in {Elapsed}ms",
            messageCount, translationCount, sw.ElapsedMilliseconds);
    }

    private async Task<int> BackfillMessagesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        int totalProcessed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await context.Messages
                .Where(m => m.SimilarityHash == null && m.MessageText != null)
                .OrderBy(m => m.MessageId)  // Deterministic ordering for batch processing
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            foreach (var message in batch)
            {
                message.SimilarityHash = simHashService.ComputeHash(message.MessageText);
            }

            await context.SaveChangesAsync(cancellationToken);
            totalProcessed += batch.Count;
            logger.LogDebug("Backfilled {Count} messages ({Total} total)", batch.Count, totalProcessed);
        }

        return totalProcessed;
    }

    private async Task<int> BackfillTranslationsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        // Translations are typically much fewer than messages, so process all at once
        var translations = await context.MessageTranslations
            .Where(mt => mt.SimilarityHash == null)
            .ToListAsync(cancellationToken);

        foreach (var translation in translations)
        {
            translation.SimilarityHash = simHashService.ComputeHash(translation.TranslatedText);
        }

        if (translations.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        return translations.Count;
    }
}

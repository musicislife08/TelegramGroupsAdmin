using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// One-time backfill of perceptual hashes for existing ban celebration GIFs.
/// Called from RunDatabaseMigrationsAsync, runs during --migrate-only.
/// Self-skipping: checks if backfill already complete.
/// </summary>
public class BanCelebrationHashBackfillService(
    IDbContextFactory<AppDbContext> contextFactory,
    IBanCelebrationGifRepository gifRepository,
    IPhotoHashService photoHashService,
    ILogger<BanCelebrationHashBackfillService> logger)
{
    /// <summary>
    /// Backfills perceptual hashes for all ban celebration GIFs that don't have one.
    /// Skips if backfill already complete.
    /// </summary>
    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Check if backfill needed
        var gifsNeedBackfill = await context.BanCelebrationGifs
            .AnyAsync(g => g.PhotoHash == null, cancellationToken);

        if (!gifsNeedBackfill)
        {
            logger.LogInformation("Ban celebration GIF hash backfill already complete, skipping");
            return;
        }

        logger.LogInformation("Starting ban celebration GIF hash backfill...");
        var sw = Stopwatch.StartNew();

        var gifsWithoutHash = await context.BanCelebrationGifs
            .Where(g => g.PhotoHash == null)
            .ToListAsync(cancellationToken);

        var processedCount = 0;
        var failedCount = 0;

        foreach (var gif in gifsWithoutHash)
        {
            try
            {
                var fullPath = gifRepository.GetFullPath(gif.FilePath);

                if (!File.Exists(fullPath))
                {
                    logger.LogWarning("GIF file not found for backfill: {Id} at {Path}", gif.Id, fullPath);
                    failedCount++;
                    continue;
                }

                var hash = await photoHashService.ComputePhotoHashAsync(fullPath);

                if (hash != null)
                {
                    gif.PhotoHash = hash;
                    processedCount++;
                }
                else
                {
                    logger.LogWarning("Failed to compute hash for GIF {Id}", gif.Id);
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error computing hash for GIF {Id}", gif.Id);
                failedCount++;
            }
        }

        if (processedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Ban celebration GIF hash backfill complete: {Processed} processed, {Failed} failed in {Elapsed}ms",
            processedCount, failedCount, sw.ElapsedMilliseconds);
    }
}

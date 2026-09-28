using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <inheritdoc />
public sealed class PhotoHashRehashService(
    IDbContextFactory<AppDbContext> contextFactory,
    IPhotoHashService photoHashService,
    IMessageHistoryRepository messageHistoryRepository,
    IMediaFeatureExtractor mediaFeatureExtractor,
    IOptions<AppOptions> appOptions,
    ILogger<PhotoHashRehashService> logger) : IPhotoHashRehashService
{
    public async Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default)
    {
        var total = PhotoHashRehashResult.Empty
            .Add(await RehashUsersAsync(ct))
            .Add(await RehashLinkedChannelsAsync(ct))
            .Add(await RehashBanCelebrationGifsAsync(ct))
            .Add(await BackfillMessageMediaFeaturesAsync(ct));

        if (total != PhotoHashRehashResult.Empty)
        {
            logger.LogInformation(
                "Photo hash rehash: {Recomputed} recomputed, {Unrecoverable} unrecoverable (source file gone), {Skipped} skipped",
                total.Recomputed, total.Unrecoverable, total.Skipped);
        }

        return total;
    }

    /// <summary>
    /// Users are the one store where <c>photo_hash</c> is a Base64-encoded string column
    /// (not <c>bytea</c>) — matching the convention already used by
    /// <c>FetchUserPhotoJob</c> when it first populates the column.
    /// </summary>
    private async Task<PhotoHashRehashResult> RehashUsersAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.TelegramUsers
            .Where(u => u.PhotoHash == null && u.UserPhotoPath != null)
            .Select(u => new { u.TelegramUserId, u.UserPhotoPath, u.BannedAt })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            // A banned user's photo was overwritten in place with a blurred copy by
            // ProfileScanService, so hashing the file would store a hash of the blur.
            if (candidate.BannedAt is not null)
            {
                skipped++;
                continue;
            }

            var hash = await ComputeAsync(candidate.UserPhotoPath!);
            if (hash is null)
            {
                unrecoverable++;
                continue;
            }

            var photoHashBase64 = Convert.ToBase64String(hash);
            await context.TelegramUsers
                .Where(u => u.TelegramUserId == candidate.TelegramUserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PhotoHash, photoHashBase64), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, skipped);
    }

    private async Task<PhotoHashRehashResult> RehashLinkedChannelsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.LinkedChannels
            .Where(c => c.PhotoHash == null && c.ChannelIconPath != null)
            .Select(c => new { c.Id, c.ChannelIconPath })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.ChannelIconPath!);
            if (hash is null) { unrecoverable++; continue; }

            await context.LinkedChannels
                .Where(c => c.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    private async Task<PhotoHashRehashResult> RehashBanCelebrationGifsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.BanCelebrationGifs
            .Where(g => g.PhotoHash == null)
            .Select(g => new { g.Id, g.FilePath })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.FilePath);
            if (hash is null) { unrecoverable++; continue; }

            await context.BanCelebrationGifs
                .Where(g => g.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    /// <summary>
    /// Most messages whose features the backfill computes per start. Internal so tests can shrink it.
    /// </summary>
    internal int MediaBackfillLimit { get; init; } = 500;

    /// <summary>
    /// Fills messages.media_features for curated media messages that have none (scanned before
    /// features existed, or never scanned before an admin decision), so Layer 1 can match them.
    /// Candidates whose file is missing, unreadable or fails stay candidates (they are retried next
    /// start), so the backfill pages past them: they are counted and skipped with a cheap existence
    /// check, and cannot starve later recoverable candidates out of the run.
    /// </summary>
    private async Task<PhotoHashRehashResult> BackfillMessageMediaFeaturesAsync(CancellationToken ct)
    {
        int recomputed = 0, missing = 0, unreadable = 0, failed = 0;

        // Each non-recovered candidate stays in the result set, so it advances the offset; a recovered
        // one leaves the set (it now has features), so it does not.
        var offset = 0;
        while (recomputed < MediaBackfillLimit)
        {
            var page = await messageHistoryRepository.GetMediaFeatureBackfillCandidatesAsync(MediaBackfillLimit, offset, ct);
            foreach (var candidate in page)
            {
                if (recomputed >= MediaBackfillLimit)
                    break;

                var path = ResolveMediaPath(candidate);
                if (path is null || !File.Exists(path))
                {
                    missing++;
                    offset++;
                    logger.LogDebug("Media-feature backfill: file missing for message {MessageId} in chat {ChatId} at {Path}",
                        candidate.MessageId, candidate.ChatId, path);
                    continue;
                }

                try
                {
                    MediaFeatures? features = candidate.PhotoLocalPath is not null || candidate.MediaType == MediaType.Photo
                        ? await mediaFeatureExtractor.ExtractPhotoAsync(path)
                        : await mediaFeatureExtractor.ExtractVideoAsync(path, ct);

                    if (features is null)
                    {
                        unreadable++;
                        offset++;
                        logger.LogDebug("Media-feature backfill: file unreadable for message {MessageId} in chat {ChatId} at {Path}",
                            candidate.MessageId, candidate.ChatId, path);
                        continue;
                    }

                    await messageHistoryRepository.SetMediaFeaturesAsync(candidate.MessageId, candidate.ChatId, features, ct);
                    recomputed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    offset++;
                    logger.LogWarning(ex, "Media-feature backfill failed for message {MessageId} in chat {ChatId}, continuing",
                        candidate.MessageId, candidate.ChatId);
                }
            }

            if (page.Count < MediaBackfillLimit)
                break;
        }

        var unrecoverable = missing + unreadable + failed;
        if (unrecoverable > 0)
        {
            logger.LogWarning(
                "Media-feature backfill: {Unrecoverable} curated media messages have no features ({Missing} file missing, {Unreadable} unreadable, {Failed} failed); they are retried on the next start",
                unrecoverable, missing, unreadable, failed);
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    /// <summary>The candidate's absolute media path, or null when it has none.</summary>
    private string? ResolveMediaPath(MediaBackfillCandidate candidate)
    {
        if (candidate.PhotoLocalPath is not null)
            return MediaUtilities.ToAbsolutePath(candidate.PhotoLocalPath, appOptions.Value.DataPath);

        if (candidate is { MediaLocalPath: not null, MediaType: { } mediaType })
        {
            MediaUtilities.ValidateMediaPath(candidate.MediaLocalPath, (int)mediaType, appOptions.Value.DataPath, out var path);
            return path;
        }

        return null;
    }

    private async Task<byte[]?> ComputeAsync(string relativePath)
    {
        var absolutePath = MediaUtilities.ToAbsolutePath(relativePath, appOptions.Value.DataPath);
        return await photoHashService.ComputePhotoHashAsync(absolutePath);
    }
}

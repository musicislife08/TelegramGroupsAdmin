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
    /// Fills messages.media_features for curated media messages that have none (scanned before
    /// features existed, or never scanned before an admin decision), so Layer 1 can match them.
    /// </summary>
    private async Task<PhotoHashRehashResult> BackfillMessageMediaFeaturesAsync(CancellationToken ct)
    {
        var candidates = await messageHistoryRepository.GetMediaFeatureBackfillCandidatesAsync(limit: 500, ct);
        int recomputed = 0, unrecoverable = 0;
        foreach (var candidate in candidates)
        {
            MediaFeatures? features = null;
            string? path = null;
            if (candidate.PhotoLocalPath is not null)
            {
                path = MediaUtilities.ToAbsolutePath(candidate.PhotoLocalPath, appOptions.Value.DataPath);
                features = await mediaFeatureExtractor.ExtractPhotoAsync(path);
            }
            else if (candidate is { MediaLocalPath: not null, MediaType: { } mediaType })
            {
                MediaUtilities.ValidateMediaPath(candidate.MediaLocalPath, (int)mediaType, appOptions.Value.DataPath, out path);
                if (path is not null)
                {
                    features = mediaType == MediaType.Photo
                        ? await mediaFeatureExtractor.ExtractPhotoAsync(path)
                        : await mediaFeatureExtractor.ExtractVideoAsync(path, ct);
                }
            }

            if (features is null)
            {
                unrecoverable++;
                logger.LogWarning("Cannot compute media features for message {MessageId} in chat {ChatId}: file missing or unreadable at {Path}",
                    candidate.MessageId, candidate.ChatId, path);
                continue;
            }

            await messageHistoryRepository.SetMediaFeaturesAsync(candidate.MessageId, candidate.ChatId, features, ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    private async Task<byte[]?> ComputeAsync(string relativePath)
    {
        var absolutePath = MediaUtilities.ToAbsolutePath(relativePath, appOptions.Value.DataPath);
        return await photoHashService.ComputePhotoHashAsync(absolutePath);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <inheritdoc />
public sealed class PhotoHashRehashService(
    IDbContextFactory<AppDbContext> contextFactory,
    IPhotoHashService photoHashService,
    IOptions<AppOptions> appOptions,
    ILogger<PhotoHashRehashService> logger) : IPhotoHashRehashService
{
    public async Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default)
    {
        var total = PhotoHashRehashResult.Empty
            .Add(await RehashUsersAsync(ct))
            .Add(await RehashLinkedChannelsAsync(ct))
            .Add(await RehashBanCelebrationGifsAsync(ct))
            .Add(await RehashImageTrainingSamplesAsync(ct));

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

    private async Task<PhotoHashRehashResult> RehashImageTrainingSamplesAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // PhotoPath on the sample row is never populated (it is [Required] but never
        // assigned on insert), so the source image has to come from the joined message.
        var candidates = await context.ImageTrainingSamples
            .Where(its => its.PhotoHash == null)
            .Join(context.Messages,
                its => new { its.MessageId, its.ChatId },
                m => new { m.MessageId, m.ChatId },
                (its, m) => new { its.Id, m.MediaLocalPath })
            .Where(x => x.MediaLocalPath != null)
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.MediaLocalPath!);
            if (hash is null) { unrecoverable++; continue; }

            await context.ImageTrainingSamples
                .Where(its => its.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(its => its.PhotoHash, hash), ct);
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing Telegram user records (profile photos, trust status, warnings).
/// Centralized user tracking across all managed chats.
/// </summary>
public class TelegramUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<TelegramUserRepository> _logger;

    public TelegramUserRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<TelegramUserRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get Telegram user by ID
    /// </summary>
    public async Task<UiModels.TelegramUser?> GetByTelegramIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        return entity?.ToModel();
    }

    /// <summary>
    /// Get user photo path by Telegram user ID (fast lookup for UI rendering)
    /// </summary>
    public async Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == telegramUserId)
            .Select(u => u.UserPhotoPath)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Upsert (insert or update) Telegram user record.
    /// Used by FetchUserPhotoJob and message processing to maintain user data.
    /// </summary>
    public async Task UpsertAsync(UiModels.TelegramUser user, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == user.TelegramUserId, ct);

        if (existing != null)
        {
            // Update existing record
            existing.Username = user.Username;
            existing.FirstName = user.FirstName;
            existing.LastName = user.LastName;
            existing.UserPhotoPath = user.UserPhotoPath;
            existing.PhotoHash = user.PhotoHash;
            existing.IsTrusted = user.IsTrusted;
            existing.LastSeenAt = user.LastSeenAt;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "Updated Telegram user {TelegramUserId} (@{Username})",
                user.TelegramUserId,
                user.Username);
        }
        else
        {
            // Insert new record
            var entity = user.ToDto();
            entity.CreatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            context.TelegramUsers.Add(entity);

            _logger.LogInformation(
                "Created Telegram user {TelegramUserId} (@{Username})",
                user.TelegramUserId,
                user.Username);
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Update only the user photo path (used by FetchUserPhotoJob)
    /// </summary>
    public async Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        if (entity != null)
        {
            entity.UserPhotoPath = photoPath;
            if (photoHash != null)
                entity.PhotoHash = photoHash;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Updated photo path for Telegram user {TelegramUserId}: {PhotoPath}",
                telegramUserId,
                photoPath);
        }
    }

    /// <summary>
    /// Update trust status (Phase 5.5: Auto-trust feature)
    /// </summary>
    public async Task UpdateTrustStatusAsync(long telegramUserId, bool isTrusted, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        if (entity != null)
        {
            entity.IsTrusted = isTrusted;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Updated trust status for Telegram user {TelegramUserId}: {IsTrusted}",
                telegramUserId,
                isTrusted);
        }
    }

    /// <summary>
    /// Get all trusted users (for whitelist checking)
    /// </summary>
    public async Task<List<long>> GetTrustedUserIdsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsTrusted)
            .Select(u => u.TelegramUserId)
            .ToListAsync(ct);
    }
}

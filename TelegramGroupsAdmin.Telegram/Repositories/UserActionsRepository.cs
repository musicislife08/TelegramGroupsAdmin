using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class UserActionsRepository : IUserActionsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<UserActionsRepository> _logger;

    public UserActionsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<UserActionsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> InsertAsync(UserActionRecord action)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = action.ToDto();
        context.UserActions.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Inserted user action {ActionType} for user {UserId} (expires: {ExpiresAt})",
            action.ActionType,
            action.UserId,
            action.ExpiresAt?.ToString() ?? "never");

        return entity.Id;
    }

    public async Task<UserActionRecord?> GetByIdAsync(long id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.UserActions
            .AsNoTracking()
            .FirstOrDefaultAsync(ua => ua.Id == id);

        return entity?.ToModel();
    }

    public async Task<List<UserActionRecord>> GetByUserIdAsync(long userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId)
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var entities = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveBansAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var entities = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<bool> IsUserBannedAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Check for active ban (all bans are global now)
        var now = DateTimeOffset.UtcNow;
        var isBanned = await context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return isBanned;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Check for active 'trust' action (all trusts are global now)
        var now = DateTimeOffset.UtcNow;
        var isTrusted = await context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return isTrusted;
    }

    public async Task<int> GetWarnCountAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Count active warns for user (all warns are global now)
        var now = DateTimeOffset.UtcNow;
        var count = await context.UserActions
            .AsNoTracking()
            .CountAsync(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return count;
    }

    public async Task ExpireActionAsync(long actionId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.UserActions.FindAsync(actionId);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();

            _logger.LogDebug("Expired action {ActionId}", actionId);
        }
    }

    public async Task ExpireBansForUserAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Expire all active bans for user (all bans are global now)
        var now = DateTimeOffset.UtcNow;
        var bansToExpire = await context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        foreach (var ban in bansToExpire)
        {
            ban.ExpiresAt = now;
        }

        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Expired {Count} bans for user {UserId}",
            bansToExpire.Count,
            userId);
    }

    public async Task ExpireTrustsForUserAsync(long userId, long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Expire all active trusts for user (all trusts are global now)
        var now = DateTimeOffset.UtcNow;
        var trustsToExpire = await context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        foreach (var trust in trustsToExpire)
        {
            trust.ExpiresAt = now;
        }

        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Expired {Count} trusts for user {UserId}",
            trustsToExpire.Count,
            userId);
    }

    public async Task<List<UserActionRecord>> GetRecentAsync(int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.UserActions
            .AsNoTracking()
            .OrderByDescending(ua => ua.IssuedAt)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Delete old actions (e.g., expired warns older than 1 year)
        var toDelete = await context.UserActions
            .Where(ua => ua.IssuedAt < timestamp
                && ua.ExpiresAt != null
                && ua.ExpiresAt < timestamp)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.UserActions.RemoveRange(toDelete);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Deleted {Count} old user actions (issued before {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }

    public async Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var dataActionType = (DataModels.UserActionType)(int)actionType;
        var entities = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId
                && ua.ActionType == dataActionType
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task DeactivateAsync(long actionId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.UserActions.FindAsync(actionId);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();

            _logger.LogDebug("Deactivated user action {ActionId}", actionId);
        }
    }
}

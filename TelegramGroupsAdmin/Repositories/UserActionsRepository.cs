using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class UserActionsRepository : IUserActionsRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserActionsRepository> _logger;

    public UserActionsRepository(
        AppDbContext context,
        ILogger<UserActionsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<long> InsertAsync(UserActionRecord action)
    {
        var entity = action.ToDataModel();
        _context.UserActions.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Inserted user action {ActionType} for user {UserId} (expires: {ExpiresAt})",
            action.ActionType,
            action.UserId,
            action.ExpiresAt?.ToString() ?? "never");

        return entity.Id;
    }

    public async Task<UserActionRecord?> GetByIdAsync(long id)
    {
        var entity = await _context.UserActions
            .AsNoTracking()
            .FirstOrDefaultAsync(ua => ua.Id == id);

        return entity?.ToUiModel();
    }

    public async Task<List<UserActionRecord>> GetByUserIdAsync(long userId)
    {
        var entities = await _context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId)
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entities = await _context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveBansAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entities = await _context.UserActions
            .AsNoTracking()
            .Where(ua => ua.ActionType == Models.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<bool> IsUserBannedAsync(long userId, long? chatId = null)
    {
        // Check for active ban (all bans are global now)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isBanned = await _context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return isBanned;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        // Check for active 'trust' action (all trusts are global now)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isTrusted = await _context.UserActions
            .AsNoTracking()
            .AnyAsync(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return isTrusted;
    }

    public async Task<int> GetWarnCountAsync(long userId, long? chatId = null)
    {
        // Count active warns for user (all warns are global now)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await _context.UserActions
            .AsNoTracking()
            .CountAsync(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now));

        return count;
    }

    public async Task ExpireActionAsync(long actionId)
    {
        var entity = await _context.UserActions.FindAsync(actionId);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _context.SaveChangesAsync();

            _logger.LogDebug("Expired action {ActionId}", actionId);
        }
    }

    public async Task ExpireBansForUserAsync(long userId, long? chatId = null)
    {
        // Expire all active bans for user (all bans are global now)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bansToExpire = await _context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        foreach (var ban in bansToExpire)
        {
            ban.ExpiresAt = now;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Expired {Count} bans for user {UserId}",
            bansToExpire.Count,
            userId);
    }

    public async Task ExpireTrustsForUserAsync(long userId, long? chatId = null)
    {
        // Expire all active trusts for user (all trusts are global now)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var trustsToExpire = await _context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == Models.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        foreach (var trust in trustsToExpire)
        {
            trust.ExpiresAt = now;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Expired {Count} trusts for user {UserId}",
            trustsToExpire.Count,
            userId);
    }

    public async Task<List<UserActionRecord>> GetRecentAsync(int limit = 100)
    {
        var entities = await _context.UserActions
            .AsNoTracking()
            .OrderByDescending(ua => ua.IssuedAt)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<int> DeleteOlderThanAsync(long timestamp)
    {
        // Delete old actions (e.g., expired warns older than 1 year)
        var toDelete = await _context.UserActions
            .Where(ua => ua.IssuedAt < timestamp
                && ua.ExpiresAt != null
                && ua.ExpiresAt < timestamp)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            _context.UserActions.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Deleted {Count} old user actions (issued before {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }

    public async Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entities = await _context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == userId
                && ua.ActionType == actionType
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task DeactivateAsync(long actionId)
    {
        var entity = await _context.UserActions.FindAsync(actionId);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _context.SaveChangesAsync();

            _logger.LogDebug("Deactivated user action {ActionId}", actionId);
        }
    }
}

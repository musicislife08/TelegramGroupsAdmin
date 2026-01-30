using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
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

    public async Task<long> InsertAsync(UserActionRecord action, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = action.ToDto();
        context.UserActions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        // Get user display name for logging (single additional query, same context)
        var targetUser = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == action.UserId)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync(cancellationToken);

        _logger.LogInformation(
            "Inserted user action {ActionType} for {User} (expires: {ExpiresAt})",
            action.ActionType,
            LogDisplayName.UserInfo(targetUser?.FirstName, targetUser?.LastName, targetUser?.Username, action.UserId),
            action.ExpiresAt?.ToString() ?? "never");

        return entity.Id;
    }

    public async Task<UserActionRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .FirstOrDefaultAsync(ua => ua.Id == id, cancellationToken);

        return entity?.ToModel(
            targetUsername: entity?.TargetUser?.Username,
            targetFirstName: entity?.TargetUser?.FirstName,
            targetLastName: entity?.TargetUser?.LastName);
    }

    public async Task<List<UserActionRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .Where(ua => ua.UserId == userId)
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entities = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .Where(ua => ua.UserId == userId
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveBansAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entities = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .Where(ua => ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();
    }

    // REFACTOR-5: Removed IsUserBannedAsync, IsUserTrustedAsync, GetWarnCountAsync
    // Source of truth is now telegram_users table (is_banned, is_trusted, warnings JSONB)
    // Use ITelegramUserRepository.IsBannedAsync, IsTrustedAsync, GetActiveWarningCountAsync

    public async Task ExpireActionAsync(long actionId, Actor expiredBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.UserActions
            .Include(ua => ua.TargetUser)
            .FirstOrDefaultAsync(ua => ua.Id == actionId, cancellationToken);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Expired action {ActionId} ({ActionType} for {User}) by {ExpiredBy}",
                actionId,
                entity.ActionType,
                LogDisplayName.UserInfo(entity.TargetUser?.FirstName, entity.TargetUser?.LastName, entity.TargetUser?.Username, entity.UserId),
                expiredBy);
        }
    }

    public async Task ExpireBansForUserAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Expire all active bans for user (all bans are global now)
        var now = DateTimeOffset.UtcNow;
        var bansToExpire = await context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync(cancellationToken);

        foreach (var ban in bansToExpire)
        {
            ban.ExpiresAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Get user display name for logging
        var targetUser = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync(cancellationToken);

        _logger.LogInformation(
            "Expired {Count} bans for {User}",
            bansToExpire.Count,
            LogDisplayName.UserInfo(targetUser?.FirstName, targetUser?.LastName, targetUser?.Username, userId));
    }

    public async Task ExpireTrustsForUserAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Expire all active trusts for user (all trusts are global now)
        var now = DateTimeOffset.UtcNow;
        var trustsToExpire = await context.UserActions
            .Where(ua => ua.UserId == userId
                && ua.ActionType == DataModels.UserActionType.Trust
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync(cancellationToken);

        foreach (var trust in trustsToExpire)
        {
            trust.ExpiresAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Get user display name for logging
        var targetUser = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync(cancellationToken);

        _logger.LogInformation(
            "Expired {Count} trusts for {User}",
            trustsToExpire.Count,
            LogDisplayName.UserInfo(targetUser?.FirstName, targetUser?.LastName, targetUser?.Username, userId));
    }

    public async Task<List<UserActionRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .OrderByDescending(ua => ua.IssuedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();
    }

    public async Task<(List<UserActionRecord> Actions, int TotalCount)> GetPagedActionsAsync(
        int skip,
        int take,
        UserActionType? actionTypeFilter = null,
        long? userIdFilter = null,
        string? issuedByFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Build query with filters
        var query = context.UserActions.AsNoTracking();

        if (actionTypeFilter.HasValue)
        {
            var dataActionType = (DataModels.UserActionType)(int)actionTypeFilter.Value;
            query = query.Where(ua => ua.ActionType == dataActionType);
        }

        if (userIdFilter.HasValue)
        {
            query = query.Where(ua => ua.UserId == userIdFilter.Value);
        }

        if (!string.IsNullOrEmpty(issuedByFilter))
        {
            // Filter by issued_by using exclusive arc columns
            // IssuedBy is stored as three columns: WebUserId, TelegramUserId, SystemIdentifier
            // We'll filter by SystemIdentifier (case-insensitive contains)
            // For WebUserId/TelegramUserId filtering, user would need to use exact IDs
            query = query.Where(ua =>
                (ua.SystemIdentifier != null && EF.Functions.ILike(ua.SystemIdentifier, $"%{issuedByFilter}%")));
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Get page of results with user enrichment
        var entities = await query
            .Include(ua => ua.TargetUser)
            .OrderByDescending(ua => ua.IssuedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var actions = entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();

        return (actions, totalCount);
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Delete old actions (e.g., expired warns older than 1 year)
        var toDelete = await context.UserActions
            .Where(ua => ua.IssuedAt < timestamp
                && ua.ExpiresAt != null
                && ua.ExpiresAt < timestamp)
            .ToListAsync(cancellationToken);

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.UserActions.RemoveRange(toDelete);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Deleted {Count} old user actions (issued before {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }

    public async Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var dataActionType = (DataModels.UserActionType)(int)actionType;
        var entities = await context.UserActions
            .AsNoTracking()
            .Include(ua => ua.TargetUser)
            .Where(ua => ua.UserId == userId
                && ua.ActionType == dataActionType
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel(
            targetUsername: e.TargetUser?.Username,
            targetFirstName: e.TargetUser?.FirstName,
            targetLastName: e.TargetUser?.LastName)).ToList();
    }

    public async Task<int> GetTodaysBanCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get today's start in server local time, converted to UTC for PostgreSQL
        // PostgreSQL timestamptz only accepts UTC values via Npgsql
        var todayStart = new DateTimeOffset(DateTime.Today).ToUniversalTime();

        return await context.UserActions
            .CountAsync(a =>
                a.ActionType == DataModels.UserActionType.Ban &&
                a.IssuedAt >= todayStart,
                cancellationToken);
    }

    public async Task DeactivateAsync(long actionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.UserActions.FindAsync([actionId], cancellationToken);
        if (entity != null)
        {
            entity.ExpiresAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Deactivated user action {ActionId}", actionId);
        }
    }
}

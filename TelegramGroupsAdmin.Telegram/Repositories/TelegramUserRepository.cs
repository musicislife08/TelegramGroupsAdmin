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

    /// <summary>
    /// Get all users with computed stats for list view
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var users = await (
            from u in context.TelegramUsers
            select new UiModels.TelegramUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                IsTrusted = u.IsTrusted,
                LastSeenAt = u.LastSeenAt,

                // Chat count (distinct chats user has posted in)
                ChatCount = context.Messages
                    .Where(m => m.UserId == u.TelegramUserId)
                    .Select(m => m.ChatId)
                    .Distinct()
                    .Count(),

                // Active warning count
                WarningCount = context.UserActions
                    .Count(ua => ua.UserId == u.TelegramUserId
                        && ua.ActionType == DataModels.UserActionType.Warn
                        && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow)),

                // Note count (Phase 4.12)
                NoteCount = context.AdminNotes
                    .Count(n => n.TelegramUserId == u.TelegramUserId),

                // Is banned
                IsBanned = context.UserActions
                    .Any(ua => ua.UserId == u.TelegramUserId
                        && ua.ActionType == DataModels.UserActionType.Ban
                        && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow)),

                // Has warnings
                HasWarnings = context.UserActions
                    .Any(ua => ua.UserId == u.TelegramUserId
                        && ua.ActionType == DataModels.UserActionType.Warn
                        && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow)),

                // Is flagged (Phase 4.12: has notes or tags)
                IsFlagged = context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId)
                    || context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId)
            }
        )
        .AsNoTracking()
        .ToListAsync(ct);

        return users;
    }

    /// <summary>
    /// Get users flagged for review (has notes, tags, borderline spam, or warnings)
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetFlaggedUsersAsync(CancellationToken ct = default)
    {
        var allUsers = await GetAllWithStatsAsync(ct);

        // Filter to flagged users
        return allUsers
            .Where(u => u.IsFlagged || u.HasWarnings)
            .ToList();
    }

    /// <summary>
    /// Get banned users with ban details
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetBannedUsersAsync(CancellationToken ct = default)
    {
        var allUsers = await GetAllWithStatsAsync(ct);

        // Filter to banned users
        return allUsers
            .Where(u => u.IsBanned)
            .ToList();
    }

    /// <summary>
    /// Get banned users with full ban details (date, issuer, reason, expiry, trigger message)
    /// Phase 5: Enhanced banned users tab
    /// </summary>
    public async Task<List<UiModels.BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Query with LEFT JOINs to resolve actor display names (Phase 4.19)
        var bannedUsers = await (
            from u in context.TelegramUsers
            join ua in context.UserActions on u.TelegramUserId equals ua.UserId
            join webUser in context.Users on ua.WebUserId equals webUser.Id into webUsers
            from webUser in webUsers.DefaultIfEmpty()
            join tgUser in context.TelegramUsers on ua.TelegramUserId equals tgUser.TelegramUserId into tgUsers
            from tgUser in tgUsers.DefaultIfEmpty()
            where ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow)
            orderby ua.IssuedAt descending
            select new UiModels.BannedUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                LastSeenAt = u.LastSeenAt,

                // Active warning count
                WarningCount = context.UserActions
                    .Count(w => w.UserId == u.TelegramUserId
                        && w.ActionType == DataModels.UserActionType.Warn
                        && (w.ExpiresAt == null || w.ExpiresAt > DateTimeOffset.UtcNow)),

                // Ban details (Phase 4.19: Actor system)
                BanDate = ua.IssuedAt,
                BannedBy = ua.WebUserId != null
                    ? (webUser != null ? webUser.Email ?? "User " + ua.WebUserId!.Substring(0, 8) + "..." : "User " + ua.WebUserId!.Substring(0, 8) + "...")
                    : ua.TelegramUserId != null
                        ? (tgUser != null
                            ? (tgUser.Username != null ? "@" + tgUser.Username : tgUser.FirstName ?? "User " + ua.TelegramUserId.ToString())
                            : "User " + ua.TelegramUserId.ToString())
                        : ua.SystemIdentifier ?? "System",
                BanReason = ua.Reason,
                BanExpires = ua.ExpiresAt,
                TriggerMessageId = ua.MessageId
            }
        )
        .AsNoTracking()
        .ToListAsync(ct);

        return bannedUsers;
    }

    /// <summary>
    /// Get trusted users
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken ct = default)
    {
        var allUsers = await GetAllWithStatsAsync(ct);

        // Filter to trusted users
        return allUsers
            .Where(u => u.IsTrusted)
            .ToList();
    }

    /// <summary>
    /// Get top active users (30-day message count)
    /// </summary>
    public async Task<List<UiModels.TopActiveUser>> GetTopActiveUsersAsync(int limit = 3, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        var topUsers = await (
            from u in context.TelegramUsers
            join m in context.Messages on u.TelegramUserId equals m.UserId
            where m.Timestamp >= since
            group m by new { u.TelegramUserId, u.Username, u.FirstName, u.UserPhotoPath } into g
            orderby g.Count() descending
            select new UiModels.TopActiveUser
            {
                TelegramUserId = g.Key.TelegramUserId,
                Username = g.Key.Username,
                FirstName = g.Key.FirstName,
                UserPhotoPath = g.Key.UserPhotoPath,
                MessageCount = g.Count()
            }
        )
        .AsNoTracking()
        .Take(limit)
        .ToListAsync(ct);

        return topUsers;
    }

    /// <summary>
    /// Get moderation queue statistics
    /// </summary>
    public async Task<UiModels.ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var stats = new UiModels.ModerationQueueStats
        {
            // Banned users count
            BannedCount = await context.UserActions
                .Where(ua => ua.ActionType == DataModels.UserActionType.Ban
                    && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow))
                .Select(ua => ua.UserId)
                .Distinct()
                .CountAsync(ct),

            // Warned users count
            WarnedCount = await context.UserActions
                .Where(ua => ua.ActionType == DataModels.UserActionType.Warn
                    && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow))
                .Select(ua => ua.UserId)
                .Distinct()
                .CountAsync(ct),

            // Flagged count (Phase 4.12: users with notes or tags)
            FlaggedCount = await context.TelegramUsers
                .Where(u => context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId)
                    || context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId))
                .CountAsync(ct),

            // Notes count (Phase 4.12)
            NotesCount = await context.AdminNotes.CountAsync(ct)
        };

        return stats;
    }

    /// <summary>
    /// Get detailed user info with all related data
    /// </summary>
    public async Task<UiModels.TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Get base user
        var user = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        if (user == null)
            return null;

        // Get chat memberships
        var chatMemberships = await (
            from m in context.Messages
            where m.UserId == telegramUserId
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            group new { m, chat } by new { m.ChatId, ChatName = chat != null ? chat.ChatName : null } into g
            select new UiModels.UserChatMembership
            {
                ChatId = g.Key.ChatId,
                ChatName = g.Key.ChatName,
                MessageCount = g.Count(),
                LastActivityAt = g.Max(x => x.m.Timestamp),
                FirstSeenAt = g.Min(x => x.m.Timestamp)
            }
        )
        .AsNoTracking()
        .ToListAsync(ct);

        // Get user actions (warnings, bans, trusts)
        var actions = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == telegramUserId)
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync(ct);

        // Get detection history (join through messages to filter by user)
        var detectionHistory = await (
            from dr in context.DetectionResults
            join m in context.Messages on dr.MessageId equals m.MessageId
            where m.UserId == telegramUserId
            select dr
        )
        .AsNoTracking()
        .OrderByDescending(dr => dr.DetectedAt)
        .ToListAsync(ct);

        // Get admin notes (Phase 4.12)
        var notes = await context.AdminNotes
            .AsNoTracking()
            .Where(n => n.TelegramUserId == telegramUserId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

        // Get user tags (Phase 4.12)
        var tags = await context.UserTags
            .AsNoTracking()
            .Where(t => t.TelegramUserId == telegramUserId)
            .OrderBy(t => t.TagType)
            .ToListAsync(ct);

        return new UiModels.TelegramUserDetail
        {
            TelegramUserId = user.TelegramUserId,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            UserPhotoPath = user.UserPhotoPath,
            PhotoHash = user.PhotoHash,
            IsTrusted = user.IsTrusted,
            FirstSeenAt = user.FirstSeenAt,
            LastSeenAt = user.LastSeenAt,
            ChatMemberships = chatMemberships,
            Actions = actions.Select(a => a.ToModel()).ToList(),
            DetectionHistory = detectionHistory.Select(d => d.ToModel()).ToList(),
            Notes = notes.Select(n => n.ToModel()).ToList(),
            Tags = tags.Select(t => t.ToModel()).ToList()
        };
    }
}

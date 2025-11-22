using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing Telegram user records (profile photos, trust status, warnings).
/// Centralized user tracking across all managed chats.
/// </summary>
public class TelegramUserRepository : ITelegramUserRepository
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
            // NOTE: IsTrusted is NOT updated here - it's only set via UpdateTrustStatusAsync()
            // to prevent message processing from clearing trust status set by admin/auto-trust
            // NOTE: BotDmEnabled is NOT updated here - it's only set via SetBotDmEnabledAsync()
            // to prevent message processing from resetting DM status after user completes /start
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

            // Always trust Telegram service account (channel posts, anonymous admin posts)
            if (user.TelegramUserId == TelegramConstants.ServiceAccountUserId)
            {
                entity.IsTrusted = true;
                _logger.LogInformation(
                    "Created Telegram service account (user {TelegramUserId}) with automatic trust",
                    user.TelegramUserId);
            }
            else
            {
                _logger.LogInformation(
                    "Created Telegram user {TelegramUserId} (@{Username})",
                    user.TelegramUserId,
                    user.Username);
            }

            context.TelegramUsers.Add(entity);
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

    public Task<UiModels.TelegramUser?> GetByIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        return GetByTelegramIdAsync(telegramUserId, ct);
    }

    public async Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        if (entity != null)
        {
            entity.PhotoFileUniqueId = fileUniqueId;
            if (photoPath != null)
                entity.UserPhotoPath = photoPath;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);

        var entities = await context.TelegramUsers
            .Where(u => u.LastSeenAt >= cutoffDate)
            .AsNoTracking()
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <summary>
    /// Update trust status (Phase 5.5: Auto-trust feature)
    /// </summary>
    public async Task UpdateTrustStatusAsync(long telegramUserId, bool isTrusted, CancellationToken ct = default)
    {
        // Protect Telegram service account - cannot remove trust
        if (telegramUserId == TelegramConstants.ServiceAccountUserId && !isTrusted)
        {
            _logger.LogWarning(
                "Blocked attempt to remove trust from Telegram service account (user {TelegramUserId}). " +
                "Service account must always remain trusted.",
                telegramUserId);
            return;
        }

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
    /// Set bot DM enabled status (user accepted bot communication)
    /// Set to true when user sends /start in private chat
    /// Set to false when bot receives Forbidden error (user blocked bot)
    /// </summary>
    public async Task SetBotDmEnabledAsync(long telegramUserId, bool enabled, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

        if (entity != null)
        {
            entity.BotDmEnabled = enabled;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Updated bot DM status for Telegram user {TelegramUserId}: {BotDmEnabled}",
                telegramUserId,
                enabled);
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

        // PERF-DATA-1: Pre-compute all stats in separate queries (7000+ queries → 8 queries)
        // Query 1: Chat counts per user (exclude bots via JOIN to telegram_users)
        var chatCounts = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where !u.IsBot
            group m by m.UserId into g
            select new { UserId = g.Key, Count = g.Select(m => m.ChatId).Distinct().Count() }
        ).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 2: Warning counts per user (active only)
        var now = DateTimeOffset.UtcNow;
        var warningCounts = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .GroupBy(ua => ua.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 3: Note counts per user
        var noteCounts = await context.AdminNotes
            .GroupBy(n => n.TelegramUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 4: Banned user IDs
        var bannedUserIds = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .Select(ua => ua.UserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 5: Users with warnings (for HasWarnings flag)
        var usersWithWarnings = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now))
            .Select(ua => ua.UserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 6: Users with notes
        var usersWithNotes = await context.AdminNotes
            .Select(n => n.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 7: Users with tags
        var usersWithTags = await context.UserTags
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 8: Users who are admins in at least one chat
        var usersWhoAreAdmins = await context.ChatAdmins
            .Where(ca => ca.IsActive)
            .Select(ca => ca.TelegramId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 9: Get all users and populate with pre-computed stats (O(1) lookups)
        var users = await context.TelegramUsers
            .AsNoTracking()
            .Select(u => new UiModels.TelegramUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                IsTrusted = u.IsTrusted,
                LastSeenAt = u.LastSeenAt,

                // Populated after query using dictionary lookups
                ChatCount = 0,
                WarningCount = 0,
                NoteCount = 0,
                IsBanned = false,
                HasWarnings = false,
                IsTagged = false,
                IsAdmin = false
            })
            .OrderBy(u => u.Username ?? u.FirstName ?? u.LastName ?? u.TelegramUserId.ToString())
            .ToListAsync(ct);

        // Populate stats using pre-computed dictionaries (in-memory, fast)
        foreach (var user in users)
        {
            user.ChatCount = chatCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.WarningCount = warningCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.NoteCount = noteCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.IsBanned = bannedUserIds.Contains(user.TelegramUserId);
            user.HasWarnings = usersWithWarnings.Contains(user.TelegramUserId);
            user.IsTagged = usersWithNotes.Contains(user.TelegramUserId) || usersWithTags.Contains(user.TelegramUserId);
            user.IsAdmin = usersWhoAreAdmins.Contains(user.TelegramUserId);
        }

        return users;
    }

    /// <summary>
    /// Get all users with computed stats filtered by chat IDs (for Admin users with chat-scoped access)
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(List<long> chatIds, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Get users who have posted in the specified chats (exclude bots)
        var userIdsInChats = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where chatIds.Contains(m.ChatId) && !u.IsBot
            select m.UserId
        ).Distinct().ToHashSetAsync(ct);

        // PERF-DATA-1: Pre-compute all stats in separate queries (filtered by chat IDs)
        // Query 1: Chat counts per user (only count chats in accessible list, exclude bots)
        var chatCounts = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where chatIds.Contains(m.ChatId) && userIdsInChats.Contains(m.UserId) && !u.IsBot
            group m by m.UserId into g
            select new { UserId = g.Key, Count = g.Select(m => m.ChatId).Distinct().Count() }
        ).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 2: Warning counts per user (all warnings, not filtered by chat)
        var now = DateTimeOffset.UtcNow;
        var warningCounts = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now)
                && userIdsInChats.Contains(ua.UserId))
            .GroupBy(ua => ua.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 3: Note counts per user (all notes, not filtered by chat)
        var noteCounts = await context.AdminNotes
            .Where(n => userIdsInChats.Contains(n.TelegramUserId))
            .GroupBy(n => n.TelegramUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Query 4: Banned user IDs (all bans, not filtered by chat)
        var bannedUserIds = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > now)
                && userIdsInChats.Contains(ua.UserId))
            .Select(ua => ua.UserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 5: Users with warnings (for HasWarnings flag)
        var usersWithWarnings = await context.UserActions
            .Where(ua => ua.ActionType == DataModels.UserActionType.Warn
                && (ua.ExpiresAt == null || ua.ExpiresAt > now)
                && userIdsInChats.Contains(ua.UserId))
            .Select(ua => ua.UserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 6: Users with notes
        var usersWithNotes = await context.AdminNotes
            .Where(n => userIdsInChats.Contains(n.TelegramUserId))
            .Select(n => n.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 7: Users with tags
        var usersWithTags = await context.UserTags
            .Where(t => userIdsInChats.Contains(t.TelegramUserId))
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 8: Users who are admins in at least one chat
        var usersWhoAreAdmins = await context.ChatAdmins
            .Where(ca => ca.IsActive && userIdsInChats.Contains(ca.TelegramId))
            .Select(ca => ca.TelegramId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Query 9: Get users who have posted in accessible chats
        var users = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => userIdsInChats.Contains(u.TelegramUserId))
            .Select(u => new UiModels.TelegramUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                IsTrusted = u.IsTrusted,
                LastSeenAt = u.LastSeenAt,

                // Populated after query using dictionary lookups
                ChatCount = 0,
                WarningCount = 0,
                NoteCount = 0,
                IsBanned = false,
                HasWarnings = false,
                IsTagged = false,
                IsAdmin = false
            })
            .OrderBy(u => u.Username ?? u.FirstName ?? u.LastName ?? u.TelegramUserId.ToString())
            .ToListAsync(ct);

        // Populate stats using pre-computed dictionaries (in-memory, fast)
        foreach (var user in users)
        {
            user.ChatCount = chatCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.WarningCount = warningCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.NoteCount = noteCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.IsBanned = bannedUserIds.Contains(user.TelegramUserId);
            user.HasWarnings = usersWithWarnings.Contains(user.TelegramUserId);
            user.IsTagged = usersWithNotes.Contains(user.TelegramUserId) || usersWithTags.Contains(user.TelegramUserId);
            user.IsAdmin = usersWhoAreAdmins.Contains(user.TelegramUserId);
        }

        return users;
    }

    /// <summary>
    /// Get users with tags or notes for tracking (includes warned users)
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken ct = default)
    {
        var allUsers = await GetAllWithStatsAsync(ct);

        // Filter to tagged users (includes warnings since those show in tagged status)
        return allUsers
            .Where(u => u.IsTagged || u.HasWarnings)
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

                // User flags
                IsTrusted = u.IsTrusted,
                IsAdmin = context.ChatAdmins.Any(ca => ca.IsActive && ca.TelegramId == u.TelegramUserId),
                IsTagged = context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId) || context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId),

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
    /// Get top active users by message count
    /// </summary>
    /// <param name="limit">Number of users to return (default: 3)</param>
    /// <param name="startDate">Optional start date filter (default: 30 days ago)</param>
    /// <param name="endDate">Optional end date filter (default: now)</param>
    /// <param name="chatIds">Optional chat filter (default: all chats)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<List<UiModels.TopActiveUser>> GetTopActiveUsersAsync(
        int limit = 3,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        List<long>? chatIds = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Default to last 30 days if no date range provided
        var since = startDate ?? DateTimeOffset.UtcNow.AddDays(-30);
        var until = endDate ?? DateTimeOffset.UtcNow;

        // Build base query with date filter
        var query = from u in context.TelegramUsers
                    join m in context.Messages on u.TelegramUserId equals m.UserId
                    where m.Timestamp >= since
                        && m.Timestamp <= until
                        && !u.IsBot
                    select new { u, m };

        // Apply optional chat filter
        if (chatIds != null && chatIds.Count > 0)
        {
            query = query.Where(x => chatIds.Contains(x.m.ChatId));
        }

        // Get total message count for percentage calculation
        var totalMessages = await query.CountAsync(ct);

        // Get top users
        var topUsers = await query
            .GroupBy(x => new { x.u.TelegramUserId, x.u.Username, x.u.FirstName, x.u.UserPhotoPath })
            .Select(g => new UiModels.TopActiveUser
            {
                TelegramUserId = g.Key.TelegramUserId,
                Username = g.Key.Username,
                FirstName = g.Key.FirstName,
                UserPhotoPath = g.Key.UserPhotoPath,
                MessageCount = g.Count(),
                Percentage = totalMessages > 0 ? (g.Count() / (double)totalMessages * 100.0) : 0
            })
            .OrderByDescending(u => u.MessageCount)
            .Take(limit)
            .AsNoTracking()
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

            // Tagged count (users with notes or tags for tracking)
            TaggedCount = await context.TelegramUsers
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

        // Get chat memberships (exclude deleted service messages from count)
        // Check if user is currently banned (global cross-chat ban)
        var isCurrentlyBanned = await context.UserActions
            .AnyAsync(ua => ua.UserId == telegramUserId
                && ua.ActionType == DataModels.UserActionType.Ban
                && (ua.ExpiresAt == null || ua.ExpiresAt > DateTimeOffset.UtcNow), ct);

        var chatMemberships = await (
            from m in context.Messages
            where m.UserId == telegramUserId && m.DeletedAt == null
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            group new { m, chat } by new { m.ChatId, ChatName = chat != null ? chat.ChatName : null } into g
            select new UiModels.UserChatMembership
            {
                ChatId = g.Key.ChatId,
                ChatName = g.Key.ChatName,
                MessageCount = g.Count(),
                LastActivityAt = g.Max(x => x.m.Timestamp),
                FirstSeenAt = g.Min(x => x.m.Timestamp),
                IsBanned = isCurrentlyBanned // Global ban applies to all chats
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
            .Where(t => t.TelegramUserId == telegramUserId && t.RemovedAt == null)
            .OrderBy(t => t.TagName)
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
            BotDmEnabled = user.BotDmEnabled,
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

using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Extensions;
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
    public async Task<UiModels.TelegramUser?> GetByTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        return entity?.ToModel();
    }

    /// <inheritdoc/>
    public async Task<List<UiModels.TelegramUser>> GetByTelegramIdsAsync(
        IEnumerable<long> telegramIds,
        CancellationToken cancellationToken = default)
    {
        var idList = telegramIds.ToList();
        if (idList.Count == 0)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => idList.Contains(u.TelegramUserId))
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <summary>
    /// Get user photo path by Telegram user ID (fast lookup for UI rendering)
    /// </summary>
    public async Task<string?> GetUserPhotoPathAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == telegramUserId)
            .Select(u => u.UserPhotoPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Upsert (insert or update) Telegram user record.
    /// Used by FetchUserPhotoJob and message processing to maintain user data.
    /// </summary>
    public async Task UpsertAsync(UiModels.TelegramUser user, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == user.TelegramUserId, cancellationToken);

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
            // NOTE: IsActive IS updated here - sending a message definitively makes user active
            // (unlike IsTrusted which is admin-controlled, IsActive is behavior-driven)
            existing.IsActive = true;
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

            // Always trust Telegram system accounts (channel posts, anonymous admin posts, etc.)
            if (TelegramConstants.IsSystemUser(user.TelegramUserId))
            {
                entity.IsTrusted = true;
                _logger.LogInformation(
                    "Created Telegram system account {User} with automatic trust",
                    user.ToLogInfo());
            }
            else
            {
                _logger.LogInformation(
                    "Created Telegram user {User}",
                    user.ToLogDebug());
            }

            context.TelegramUsers.Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Update only the user photo path (used by FetchUserPhotoJob)
    /// </summary>
    public async Task UpdateUserPhotoPathAsync(long telegramUserId, string? photoPath, string? photoHash = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.UserPhotoPath = photoPath;
            if (photoHash != null)
                entity.PhotoHash = photoHash;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Updated photo path for Telegram user {TelegramUserId}: {PhotoPath}",
                telegramUserId,
                photoPath);
        }
    }

    public Task<UiModels.TelegramUser?> GetByIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return GetByTelegramIdAsync(telegramUserId, cancellationToken);
    }

    public async Task UpdatePhotoFileUniqueIdAsync(long telegramUserId, string? fileUniqueId, string? photoPath, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.PhotoFileUniqueId = fileUniqueId;
            if (photoPath != null)
                entity.UserPhotoPath = photoPath;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<UiModels.TelegramUser>> GetActiveUsersAsync(int days, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);

        var entities = await context.TelegramUsers
            .Where(u => u.LastSeenAt >= cutoffDate && !u.IsBanned && u.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <summary>
    /// Update trust status (Phase 5.5: Auto-trust feature)
    /// </summary>
    public async Task UpdateTrustStatusAsync(long telegramUserId, bool isTrusted, CancellationToken cancellationToken = default)
    {
        // Protect Telegram system accounts - cannot remove trust
        if (TelegramConstants.IsSystemUser(telegramUserId) && !isTrusted)
        {
            _logger.LogWarning(
                "Blocked attempt to remove trust from Telegram system account (user {TelegramUserId}). " +
                "System accounts must always remain trusted.",
                telegramUserId);
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.IsTrusted = isTrusted;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated trust status for {User}: {IsTrusted}",
                entity.ToLogInfo(),
                isTrusted);
        }
    }

    /// <summary>
    /// Set bot DM enabled status (user accepted bot communication)
    /// Set to true when user sends /start in private chat
    /// Set to false when bot receives Forbidden error (user blocked bot)
    /// </summary>
    public async Task SetBotDmEnabledAsync(long telegramUserId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.BotDmEnabled = enabled;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated bot DM status for {User}: {BotDmEnabled}",
                entity.ToLogInfo(),
                enabled);
        }
    }

    /// <summary>
    /// Get all trusted users (for whitelist checking)
    /// </summary>
    public async Task<List<long>> GetTrustedUserIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsTrusted)
            .Select(u => u.TelegramUserId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get all users with computed stats for list view
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // PERF-DATA-1: Pre-compute all stats in separate queries (7000+ queries → 8 queries)
        // Query 1: Chat counts per user (exclude bots via JOIN to telegram_users)
        var chatCounts = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where !u.IsBot
            group m by m.UserId into g
            select new { UserId = g.Key, Count = g.Select(m => m.ChatId).Distinct().Count() }
        ).ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // Query 2: Note counts per user
        var now = DateTimeOffset.UtcNow;
        var noteCounts = await context.AdminNotes
            .GroupBy(n => n.TelegramUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // Query 3: Users with JSONB warnings (for active warning counts)
        // REFACTOR-5: Warnings are now JSONB on telegram_users, not a separate table
        var usersWithWarnings = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.Warnings != null)
            .Select(u => new { u.TelegramUserId, u.Warnings })
            .ToListAsync(cancellationToken);

        // Compute active warning counts in-memory (JSONB filtering not supported in EF Core)
        var warningCounts = usersWithWarnings
            .Where(u => u.Warnings != null)
            .ToDictionary(
                u => u.TelegramUserId,
                u => u.Warnings!.Count(w => w.ExpiresAt == null || w.ExpiresAt > now));

        // Query 4: Banned user IDs (source of truth: is_banned column)
        var bannedUserIds = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now))
            .Select(u => u.TelegramUserId)
            .ToHashSetAsync(cancellationToken);

        // Users with active warnings (computed from warning counts)
        var userIdsWithActiveWarnings = warningCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        // Query 6: Users with notes
        var usersWithNotes = await context.AdminNotes
            .Select(n => n.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 7: Users with tags
        var usersWithTags = await context.UserTags
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 8: Users who are admins in at least one chat
        var usersWhoAreAdmins = await context.ChatAdmins
            .Where(ca => ca.IsActive)
            .Select(ca => ca.TelegramId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 9: Get active users and populate with pre-computed stats (O(1) lookups)
        // Filters to IsActive=true by default (inactive users shown via GetInactiveUsersAsync)
        var users = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsActive)
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
            .ToListAsync(cancellationToken);

        // Populate stats using pre-computed dictionaries (in-memory, fast)
        foreach (var user in users)
        {
            user.ChatCount = chatCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.WarningCount = warningCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.NoteCount = noteCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.IsBanned = bannedUserIds.Contains(user.TelegramUserId);
            user.HasWarnings = userIdsWithActiveWarnings.Contains(user.TelegramUserId);
            user.IsTagged = usersWithNotes.Contains(user.TelegramUserId) || usersWithTags.Contains(user.TelegramUserId);
            user.IsAdmin = usersWhoAreAdmins.Contains(user.TelegramUserId);
        }

        return users;
    }

    /// <summary>
    /// Get all users with computed stats filtered by chat IDs (for Admin users with chat-scoped access)
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetAllWithStatsAsync(List<long> chatIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get users who have posted in the specified chats (exclude bots)
        var userIdsInChats = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where chatIds.Contains(m.ChatId) && !u.IsBot
            select m.UserId
        ).Distinct().ToHashSetAsync(cancellationToken);

        // PERF-DATA-1: Pre-compute all stats in separate queries (filtered by chat IDs)
        // Query 1: Chat counts per user (only count chats in accessible list, exclude bots)
        var chatCounts = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where chatIds.Contains(m.ChatId) && userIdsInChats.Contains(m.UserId) && !u.IsBot
            group m by m.UserId into g
            select new { UserId = g.Key, Count = g.Select(m => m.ChatId).Distinct().Count() }
        ).ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // Query 2: Note counts per user (all notes, not filtered by chat)
        var now = DateTimeOffset.UtcNow;
        var noteCounts = await context.AdminNotes
            .Where(n => userIdsInChats.Contains(n.TelegramUserId))
            .GroupBy(n => n.TelegramUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // Query 3: Users with JSONB warnings (for active warning counts)
        // REFACTOR-5: Warnings are now JSONB on telegram_users, not a separate table
        var usersWithWarnings = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => userIdsInChats.Contains(u.TelegramUserId) && u.Warnings != null)
            .Select(u => new { u.TelegramUserId, u.Warnings })
            .ToListAsync(cancellationToken);

        // Compute active warning counts in-memory (JSONB filtering not supported in EF Core)
        var warningCounts = usersWithWarnings
            .Where(u => u.Warnings != null)
            .ToDictionary(
                u => u.TelegramUserId,
                u => u.Warnings!.Count(w => w.ExpiresAt == null || w.ExpiresAt > now));

        // Query 4: Banned user IDs (source of truth: is_banned column)
        var bannedUserIds = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => userIdsInChats.Contains(u.TelegramUserId)
                && u.IsBanned
                && (u.BanExpiresAt == null || u.BanExpiresAt > now))
            .Select(u => u.TelegramUserId)
            .ToHashSetAsync(cancellationToken);

        // Users with active warnings (computed from warning counts)
        var userIdsWithActiveWarnings = warningCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        // Query 6: Users with notes
        var usersWithNotes = await context.AdminNotes
            .Where(n => userIdsInChats.Contains(n.TelegramUserId))
            .Select(n => n.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 7: Users with tags
        var usersWithTags = await context.UserTags
            .Where(t => userIdsInChats.Contains(t.TelegramUserId))
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 8: Users who are admins in at least one chat
        var usersWhoAreAdmins = await context.ChatAdmins
            .Where(ca => ca.IsActive && userIdsInChats.Contains(ca.TelegramId))
            .Select(ca => ca.TelegramId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Query 9: Get active users who have posted in accessible chats
        // Filters to IsActive=true (users with messages are active by definition, but filter for consistency)
        var users = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => userIdsInChats.Contains(u.TelegramUserId) && u.IsActive)
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
            .ToListAsync(cancellationToken);

        // Populate stats using pre-computed dictionaries (in-memory, fast)
        foreach (var user in users)
        {
            user.ChatCount = chatCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.WarningCount = warningCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.NoteCount = noteCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.IsBanned = bannedUserIds.Contains(user.TelegramUserId);
            user.HasWarnings = userIdsWithActiveWarnings.Contains(user.TelegramUserId);
            user.IsTagged = usersWithNotes.Contains(user.TelegramUserId) || usersWithTags.Contains(user.TelegramUserId);
            user.IsAdmin = usersWhoAreAdmins.Contains(user.TelegramUserId);
        }

        return users;
    }

    /// <summary>
    /// Get users with tags or notes for tracking (includes warned users)
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default)
    {
        var allUsers = await GetAllWithStatsAsync(cancellationToken);

        // Filter to tagged users (includes warnings since those show in tagged status)
        return allUsers
            .Where(u => u.IsTagged || u.HasWarnings)
            .ToList();
    }

    /// <summary>
    /// Get banned users with ban details
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default)
    {
        var allUsers = await GetAllWithStatsAsync(cancellationToken);

        // Filter to banned users
        return allUsers
            .Where(u => u.IsBanned)
            .ToList();
    }

    /// <summary>
    /// Get banned users with full ban details (date, issuer, reason, expiry, trigger message)
    /// Phase 5: Enhanced banned users tab
    /// REFACTOR-5: Uses is_banned column as source of truth, joins user_actions for audit history
    /// </summary>
    public async Task<List<UiModels.BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Step 1: Get banned users with their warnings (source of truth: is_banned column)
        var bannedUsersWithWarnings = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now))
            .Select(u => new
            {
                u.TelegramUserId,
                u.Username,
                u.FirstName,
                u.LastName,
                u.UserPhotoPath,
                u.LastSeenAt,
                u.IsTrusted,
                u.Warnings
            })
            .ToListAsync(cancellationToken);

        if (bannedUsersWithWarnings.Count == 0)
            return [];

        var bannedUserIds = bannedUsersWithWarnings.Select(u => u.TelegramUserId).ToHashSet();

        // Step 2: Get ban details from audit log (most recent ban per user)
        var banActions = await (
            from ua in context.UserActions
            join webUser in context.Users on ua.WebUserId equals webUser.Id into webUsers
            from webUser in webUsers.DefaultIfEmpty()
            join tgUser in context.TelegramUsers on ua.TelegramUserId equals tgUser.TelegramUserId into tgUsers
            from tgUser in tgUsers.DefaultIfEmpty()
            where ua.ActionType == DataModels.UserActionType.Ban
                && bannedUserIds.Contains(ua.UserId)
            orderby ua.IssuedAt descending
            select new
            {
                ua.UserId,
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
        .ToListAsync(cancellationToken);

        // Get most recent ban per user
        var mostRecentBans = banActions
            .GroupBy(b => b.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        // Step 3: Get admin and tag info
        var adminUserIds = await context.ChatAdmins
            .Where(ca => ca.IsActive && bannedUserIds.Contains(ca.TelegramId))
            .Select(ca => ca.TelegramId)
            .ToHashSetAsync(cancellationToken);

        var taggedUserIds = await context.UserTags
            .Where(t => bannedUserIds.Contains(t.TelegramUserId))
            .Select(t => t.TelegramUserId)
            .Union(context.AdminNotes
                .Where(n => bannedUserIds.Contains(n.TelegramUserId))
                .Select(n => n.TelegramUserId))
            .ToHashSetAsync(cancellationToken);

        // Step 4: Combine results
        var result = bannedUsersWithWarnings.Select(u =>
        {
            var banInfo = mostRecentBans.GetValueOrDefault(u.TelegramUserId);
            var activeWarningCount = u.Warnings?.Count(w => w.ExpiresAt == null || w.ExpiresAt > now) ?? 0;

            return new UiModels.BannedUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                LastSeenAt = u.LastSeenAt,

                // Warning count from JSONB
                WarningCount = activeWarningCount,

                // User flags
                IsTrusted = u.IsTrusted,
                IsAdmin = adminUserIds.Contains(u.TelegramUserId),
                IsTagged = taggedUserIds.Contains(u.TelegramUserId),

                // Ban details from audit log
                BanDate = banInfo?.BanDate ?? now,
                BannedBy = banInfo?.BannedBy ?? "Unknown",
                BanReason = banInfo?.BanReason,
                BanExpires = banInfo?.BanExpires,
                TriggerMessageId = banInfo?.TriggerMessageId
            };
        })
        .OrderByDescending(u => u.BanDate)
        .ToList();

        return result;
    }

    /// <summary>
    /// Get trusted users
    /// </summary>
    public async Task<List<UiModels.TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default)
    {
        var allUsers = await GetAllWithStatsAsync(cancellationToken);

        // Filter to trusted users
        return allUsers
            .Where(u => u.IsTrusted)
            .ToList();
    }

    /// <summary>
    /// Get moderation queue statistics
    /// REFACTOR-5: Uses is_banned column and JSONB warnings as source of truth
    /// </summary>
    public async Task<UiModels.ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Banned users count (source of truth: is_banned column)
        var bannedCount = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now))
            .CountAsync(cancellationToken);

        // Warned users count (from JSONB - fetch users with warnings and filter in-memory)
        var usersWithWarnings = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.Warnings != null)
            .Select(u => u.Warnings)
            .ToListAsync(cancellationToken);

        var warnedCount = usersWithWarnings
            .Count(warnings => warnings != null && warnings.Any(w => w.ExpiresAt == null || w.ExpiresAt > now));

        var stats = new UiModels.ModerationQueueStats
        {
            BannedCount = bannedCount,
            WarnedCount = warnedCount,

            // Tagged count (users with notes or tags for tracking)
            TaggedCount = await context.TelegramUsers
                .Where(u => context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId)
                    || context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId))
                .CountAsync(cancellationToken),

            // Notes count (Phase 4.12)
            NotesCount = await context.AdminNotes.CountAsync(cancellationToken)
        };

        return stats;
    }

    /// <summary>
    /// Get detailed user info with all related data
    /// </summary>
    public async Task<UiModels.TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get base user
        var user = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null)
            return null;

        // Get chat memberships (exclude deleted service messages from count)
        // REFACTOR-5: Use is_banned column as source of truth (not user_actions)
        var now = DateTimeOffset.UtcNow;
        var isCurrentlyBanned = user.IsBanned && (user.BanExpiresAt == null || user.BanExpiresAt > now);

        var chatMemberships = (await (
            from m in context.Messages
            where m.UserId == telegramUserId && m.DeletedAt == null
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            group new { m, chat } by new { m.ChatId, ChatName = chat != null ? chat.ChatName : null } into g
            select new
            {
                g.Key.ChatId,
                g.Key.ChatName,
                MessageCount = g.Count(),
                LastActivityAt = g.Max(x => x.m.Timestamp),
                FirstSeenAt = g.Min(x => x.m.Timestamp)
            }
        )
        .AsNoTracking()
        .ToListAsync(cancellationToken))
        .Select(g => new UiModels.UserChatMembership
        {
            Identity = new ChatIdentity(g.ChatId, g.ChatName),
            MessageCount = g.MessageCount,
            LastActivityAt = g.LastActivityAt,
            FirstSeenAt = g.FirstSeenAt,
            IsBanned = isCurrentlyBanned
        })
        .ToList();

        // Get user actions (warnings, bans, trusts)
        var actions = await context.UserActions
            .AsNoTracking()
            .Where(ua => ua.UserId == telegramUserId)
            .OrderByDescending(ua => ua.IssuedAt)
            .ToListAsync(cancellationToken);

        // Get detection history (join through messages to filter by user)
        var detectionHistory = await (
            from dr in context.DetectionResults
            join m in context.Messages on dr.MessageId equals m.MessageId
            where m.UserId == telegramUserId
            select dr
        )
        .AsNoTracking()
        .OrderByDescending(dr => dr.DetectedAt)
        .ToListAsync(cancellationToken);

        // Get admin notes (Phase 4.12)
        var notes = await context.AdminNotes
            .AsNoTracking()
            .Where(n => n.TelegramUserId == telegramUserId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        // Get user tags (Phase 4.12)
        var tags = await context.UserTags
            .AsNoTracking()
            .Where(t => t.TelegramUserId == telegramUserId && t.RemovedAt == null)
            .OrderBy(t => t.TagName)
            .ToListAsync(cancellationToken);

        // Warnings come from JSONB on user entity (REFACTOR-5: embedded collection)
        // Filter to active (non-expired) warnings (reuse 'now' from line 754)
        var activeWarnings = (user.Warnings ?? [])
            .Where(w => w.ExpiresAt == null || w.ExpiresAt > now)
            .OrderByDescending(w => w.IssuedAt)
            .ToList();

        return new UiModels.TelegramUserDetail
        {
            User = new UserIdentity(user.TelegramUserId, user.FirstName, user.LastName, user.Username),
            UserPhotoPath = user.UserPhotoPath,
            PhotoHash = user.PhotoHash,
            IsTrusted = user.IsTrusted,
            IsBanned = user.IsBanned,
            BanExpiresAt = user.BanExpiresAt,
            BotDmEnabled = user.BotDmEnabled,
            FirstSeenAt = user.FirstSeenAt,
            LastSeenAt = user.LastSeenAt,
            ChatMemberships = chatMemberships,
            Actions = actions.Select(a => a.ToModel()).ToList(),
            Warnings = activeWarnings,
            DetectionHistory = detectionHistory.Select(d => d.ToModel()).ToList(),
            Notes = notes.Select(n => n.ToModel()).ToList(),
            Tags = tags.Select(t => t.ToModel()).ToList()
        };
    }

    // ============================================================================
    // Moderation State Methods (REFACTOR-5: Source of truth on telegram_users)
    // ============================================================================

    /// <inheritdoc />
    public async Task SetBanStatusAsync(
        long telegramUserId,
        bool isBanned,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Cannot set ban status for unknown user {User}", user.ToLogDebug(telegramUserId));
            return;
        }

        user.IsBanned = isBanned;
        user.BanExpiresAt = isBanned ? expiresAt : null; // Clear expiry on unban
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set ban status for {User}: IsBanned={IsBanned}, ExpiresAt={ExpiresAt}",
            user.ToLogInfo(), isBanned, expiresAt);
    }

    /// <inheritdoc />
    public async Task<int> AddWarningAsync(
        long telegramUserId,
        DataModels.WarningEntry warning,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException($"Cannot add warning for unknown user {telegramUserId}");
        }

        // Initialize warnings list if null
        user.Warnings ??= [];

        // Add the new warning
        user.Warnings.Add(warning);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        // Return count of active (non-expired) warnings
        var now = DateTimeOffset.UtcNow;
        return user.Warnings.Count(w => w.ExpiresAt == null || w.ExpiresAt > now);
    }

    /// <inheritdoc />
    public async Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user?.Warnings == null) return 0;

        var now = DateTimeOffset.UtcNow;
        return user.Warnings.Count(w => w.ExpiresAt == null || w.ExpiresAt > now);
    }

    /// <inheritdoc />
    public async Task<bool> IsBannedAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null) return false;

        // Check if banned and not expired
        if (!user.IsBanned) return false;
        if (user.BanExpiresAt == null) return true; // Permanent ban
        return user.BanExpiresAt > DateTimeOffset.UtcNow; // Temp ban not yet expired
    }

    /// <inheritdoc />
    public async Task<bool> IsTrustedAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // REFACTOR-5: Source of truth is telegram_users.is_trusted column
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == telegramUserId)
            .Select(u => u.IsTrusted)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ============================================================================
    // IsActive Methods (Phase: /ban @username support)
    // ============================================================================

    /// <inheritdoc />
    public async Task<UiModels.TelegramUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.TrimStart('@').ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.Username != null
                && u.Username.ToLower() == normalizedUsername
                && u.IsActive) // Only return active users
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(long telegramUserId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Cannot set active status for unknown user {User}", user.ToLogDebug(telegramUserId));
            return;
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set active status for {User}: IsActive={IsActive}",
            user.ToLogInfo(), isActive);
    }

    /// <inheritdoc />
    public async Task<List<UiModels.TelegramUser>> SearchByNameAsync(string searchText, int limit = 10, CancellationToken cancellationToken = default)
    {
        var searchLower = searchText.ToLowerInvariant().Trim();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Fuzzy search: match against combined "first last" name OR username
        // Searches ALL users (active and inactive) for ban command
        var matches = await context.TelegramUsers
            .AsNoTracking()
            .Where(u =>
                // Match against combined full name (first + space + last)
                (u.FirstName + " " + u.LastName).ToLower().Contains(searchLower) ||
                // Or match against username
                (u.Username != null && u.Username.ToLower().Contains(searchLower)))
            .OrderBy(u => u.FirstName) // Alphabetical for consistent ordering
            .Take(limit)
            .ToListAsync(cancellationToken);

        return matches.Select(u => u.ToModel()).ToList();
    }

    /// <inheritdoc />
    public async Task<List<UiModels.TelegramUserListItem>> GetInactiveUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get inactive users (joined but never engaged)
        // Uses partial index ix_telegram_users_is_active for efficient filtering
        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => !u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UiModels.TelegramUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                IsTrusted = u.IsTrusted,
                LastSeenAt = u.LastSeenAt,
                // Stats not needed for inactive users (they haven't engaged)
                ChatCount = 0,
                WarningCount = 0,
                NoteCount = 0,
                IsBanned = u.IsBanned,
                HasWarnings = false,
                IsTagged = false,
                IsAdmin = false
            })
            .ToListAsync(cancellationToken);
    }
}

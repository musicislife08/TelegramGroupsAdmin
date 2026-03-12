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
    public async Task<UiModels.TelegramUser> GetOrCreateAsync(
        long telegramUserId, string? username, string? firstName, string? lastName, bool isBot,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.TelegramUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (existing != null)
            return existing.ToModel();

        var now = DateTimeOffset.UtcNow;
        var entity = new DataModels.TelegramUserDto
        {
            TelegramUserId = telegramUserId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            IsBot = isBot,
            IsTrusted = TelegramConstants.IsSystemUser(telegramUserId),
            IsBanned = false,
            BotDmEnabled = false,
            FirstSeenAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = false
        };

        context.TelegramUsers.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        if (entity.IsTrusted)
        {
            _logger.LogInformation("Created Telegram system account {TelegramUserId} with automatic trust", telegramUserId);
        }
        else
        {
            _logger.LogInformation("Created Telegram user {FirstName} {LastName} ({TelegramUserId})",
                firstName, lastName, telegramUserId);
        }

        return entity.ToModel();
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
            // NOTE: IsTrusted is NOT updated here - it's only set via TrustUserAsync/UntrustUserAsync
            // to prevent message processing from clearing trust status set by admin/auto-trust
            // NOTE: BotDmEnabled is NOT updated here - it's only set via EnableBotDmAsync/DisableBotDmAsync
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
    /// Mark user as trusted
    /// </summary>
    public async Task TrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.IsTrusted = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Trusted user {User}",
                entity.ToLogInfo());
        }
    }

    /// <summary>
    /// Remove trust from user
    /// </summary>
    public async Task UntrustUserAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        // Protect Telegram system accounts - cannot remove trust
        if (TelegramConstants.IsSystemUser(telegramUserId))
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
            entity.IsTrusted = false;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Untrusted user {User}",
                entity.ToLogInfo());
        }
    }

    /// <summary>
    /// Enable bot DMs for user (called when user sends /start in private chat)
    /// </summary>
    public async Task EnableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.BotDmEnabled = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Enabled bot DMs for {User}",
                entity.ToLogInfo());
        }
    }

    /// <summary>
    /// Disable bot DMs for user (called when bot receives Forbidden error / user blocked bot)
    /// </summary>
    public async Task DisableBotDmAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (entity != null)
        {
            entity.BotDmEnabled = false;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Disabled bot DMs for {User}",
                entity.ToLogInfo());
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
            .Where(u => u.Warnings!.Any())
            .Select(u => new { u.TelegramUserId, u.Warnings })
            .ToListAsync(cancellationToken);

        // Compute active warning counts in-memory (JSONB filtering not supported in EF Core)
        var warningCounts = usersWithWarnings
            .Where(u => u.Warnings!.Any())
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
        // Filters to IsActive=true by default (kicked users shown via GetPagedUsersAsync with Kicked filter)
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
                IsAdmin = false,
                ProfileScanScore = u.ProfileScanScore,
                IsScam = u.IsScam,
                IsFake = u.IsFake
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

    // ============================================================================
    // Paginated Methods (server-side pagination for Users page)
    // ============================================================================

    /// <inheritdoc />
    public async Task<(List<UiModels.TelegramUserListItem> Items, int TotalCount)> GetPagedUsersAsync(
        UiModels.UserListFilter filter, int skip, int take,
        string? searchText, List<long>? chatIds,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Build base queryable with filter predicate
        var query = context.TelegramUsers.AsNoTracking().Where(u => u.TelegramUserId != 0);

        // Apply tab filter
        switch (filter)
        {
            case UiModels.UserListFilter.Active:
                query = query.Where(u => u.IsActive && !u.IsBanned);
                break;
            case UiModels.UserListFilter.Tagged:
                query = query.Where(u => u.IsActive &&
                    (context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId) ||
                     context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId) ||
                     u.Warnings!.Any()));
                break;
            case UiModels.UserListFilter.Trusted:
                query = query.Where(u => u.IsActive && u.IsTrusted);
                break;
            case UiModels.UserListFilter.Kicked:
                query = query.Where(u => !u.IsActive && !u.IsBanned);
                break;
        }

        // Apply chatIds filter (Admin users have scoped access via messages table)
        if (chatIds is { Count: > 0 })
        {
            query = query.Where(u => context.Messages.Any(m => m.UserId == u.TelegramUserId && chatIds.Contains(m.ChatId)));
        }

        // Apply search text filter
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim().ToLower();
            query = query.Where(u =>
                (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
                EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%"));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
            return ([], 0);

        // Apply sort
        IOrderedQueryable<DataModels.TelegramUserDto> orderedQuery = sortLabel switch
        {
            "User" => sortDescending
                ? query.OrderByDescending(u => u.Username ?? u.FirstName ?? u.LastName)
                : query.OrderBy(u => u.Username ?? u.FirstName ?? u.LastName),
            "Status" => sortDescending
                ? query.OrderByDescending(u => u.IsTrusted).ThenByDescending(u => u.IsBanned)
                : query.OrderBy(u => u.IsTrusted).ThenBy(u => u.IsBanned),
            "LastSeen" => sortDescending
                ? query.OrderByDescending(u => u.LastSeenAt)
                : query.OrderBy(u => u.LastSeenAt),
            _ => query.OrderBy(u => u.Username ?? u.FirstName ?? u.LastName)
        };

        // Project to list items with Skip/Take
        var users = await orderedQuery
            .Skip(skip)
            .Take(take)
            .Select(u => new UiModels.TelegramUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                IsTrusted = u.IsTrusted,
                IsBanned = u.IsBanned,
                LastSeenAt = u.LastSeenAt,
                ProfileScanScore = u.ProfileScanScore,
                IsScam = u.IsScam,
                IsFake = u.IsFake,
                // Computed from joins — enriched below for this page only
                ChatCount = 0,
                WarningCount = 0,
                NoteCount = 0,
                HasWarnings = false,
                IsTagged = false,
                IsAdmin = false
            })
            .ToListAsync(cancellationToken);

        // Enrich stats for this page only (bounded by page size)
        await EnrichUserStatsAsync(context, users, filter, cancellationToken);

        return (users, totalCount);
    }

    /// <inheritdoc />
    public async Task<(List<UiModels.BannedUserListItem> Items, int TotalCount)> GetPagedBannedUsersWithDetailsAsync(
        int skip, int take, string? searchText,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Base query: banned users (source of truth: is_banned column)
        var query = context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now) && u.TelegramUserId != 0);

        // Apply search text
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim().ToLower();
            query = query.Where(u =>
                (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
                EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
            return ([], 0);

        // Sort dispatch using entity columns (no user_actions dependency)
        IOrderedQueryable<DataModels.TelegramUserDto> orderedQuery = sortLabel switch
        {
            "User" => sortDescending
                ? query.OrderByDescending(u => u.Username ?? u.FirstName ?? u.LastName)
                : query.OrderBy(u => u.Username ?? u.FirstName ?? u.LastName),
            "BannedAt" => sortDescending
                ? query.OrderByDescending(u => u.BannedAt ?? DateTimeOffset.MinValue)
                : query.OrderBy(u => u.BannedAt ?? DateTimeOffset.MinValue),
            _ => query.OrderByDescending(u => u.BannedAt ?? DateTimeOffset.MinValue)
        };

        // Get the page of banned users with warnings
        var bannedUsersWithWarnings = await orderedQuery
            .Skip(skip)
            .Take(take)
            .Select(u => new
            {
                u.TelegramUserId,
                u.Username,
                u.FirstName,
                u.LastName,
                u.UserPhotoPath,
                u.LastSeenAt,
                u.IsTrusted,
                u.BannedAt,
                u.BanExpiresAt,
                u.Warnings
            })
            .ToListAsync(cancellationToken);

        if (bannedUsersWithWarnings.Count == 0)
            return ([], totalCount);

        var bannedUserIds = bannedUsersWithWarnings.Select(u => u.TelegramUserId).ToHashSet();

        // Get admin and tag info for this page
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

        var result = bannedUsersWithWarnings.Select(u =>
        {
            var activeWarningCount = u.Warnings?.Count(w => w.ExpiresAt == null || w.ExpiresAt > now) ?? 0;

            return new UiModels.BannedUserListItem
            {
                TelegramUserId = u.TelegramUserId,
                Username = u.Username,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UserPhotoPath = u.UserPhotoPath,
                LastSeenAt = u.LastSeenAt,
                WarningCount = activeWarningCount,
                IsTrusted = u.IsTrusted,
                IsAdmin = adminUserIds.Contains(u.TelegramUserId),
                IsTagged = taggedUserIds.Contains(u.TelegramUserId),
                BannedAt = u.BannedAt,
                BanExpires = u.BanExpiresAt
            };
        })
        .ToList();

        return (result, totalCount);
    }

    /// <inheritdoc />
    public async Task<UiModels.UserTabCounts> GetUserTabCountsAsync(
        List<long>? chatIds, string? searchText,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Build base queryable (exclude system user)
        var baseQuery = context.TelegramUsers.AsNoTracking().Where(u => u.TelegramUserId != 0);

        // Apply chatIds filter
        if (chatIds is { Count: > 0 })
        {
            baseQuery = baseQuery.Where(u => context.Messages.Any(m => m.UserId == u.TelegramUserId && chatIds.Contains(m.ChatId)));
        }

        // Apply search text filter
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.Trim().ToLower();
            baseQuery = baseQuery.Where(u =>
                (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
                EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%"));
        }

        // Run 5 count queries sequentially (DbContext is not thread-safe)
        var activeCount = await baseQuery.Where(u => u.IsActive && !u.IsBanned).CountAsync(cancellationToken);
        var taggedCount = await baseQuery.Where(u => u.IsActive &&
            (context.AdminNotes.Any(n => n.TelegramUserId == u.TelegramUserId) ||
             context.UserTags.Any(t => t.TelegramUserId == u.TelegramUserId) ||
             u.Warnings!.Any())).CountAsync(cancellationToken);
        var trustedCount = await baseQuery.Where(u => u.IsActive && u.IsTrusted).CountAsync(cancellationToken);
        var bannedCount = await baseQuery.Where(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now)).CountAsync(cancellationToken);
        var kickedCount = await baseQuery.Where(u => !u.IsActive && !u.IsBanned).CountAsync(cancellationToken);

        return new UiModels.UserTabCounts
        {
            ActiveCount = activeCount,
            TaggedCount = taggedCount,
            TrustedCount = trustedCount,
            BannedCount = bannedCount,
            KickedCount = kickedCount
        };
    }

    /// <summary>
    /// Enrich a page of TelegramUserListItem with computed stats (ChatCount, WarningCount, etc.).
    /// Queries are scoped to the provided userIds so cost is bounded by page size.
    /// </summary>
    private static async Task EnrichUserStatsAsync(
        AppDbContext context, List<UiModels.TelegramUserListItem> users,
        UiModels.UserListFilter filter, CancellationToken cancellationToken)
    {
        if (users.Count == 0) return;

        var userIds = users.Select(u => u.TelegramUserId).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        // Chat counts per user (exclude bots via JOIN)
        var chatCounts = await (
            from m in context.Messages
            join u in context.TelegramUsers on m.UserId equals u.TelegramUserId
            where userIds.Contains(m.UserId) && !u.IsBot
            group m by m.UserId into g
            select new { UserId = g.Key, Count = g.Select(m => m.ChatId).Distinct().Count() }
        ).ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // Note counts
        var noteCounts = await context.AdminNotes
            .Where(n => userIds.Contains(n.TelegramUserId))
            .GroupBy(n => n.TelegramUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        // JSONB warnings (must compute in-memory)
        var usersWithWarnings = await context.TelegramUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.TelegramUserId) && u.Warnings!.Any())
            .Select(u => new { u.TelegramUserId, u.Warnings })
            .ToListAsync(cancellationToken);

        var warningCounts = usersWithWarnings
            .ToDictionary(
                u => u.TelegramUserId,
                u => u.Warnings!.Count(w => w.ExpiresAt == null || w.ExpiresAt > now));

        // Banned user IDs — skip query for Active/Kicked (filter predicate guarantees !IsBanned)
        HashSet<long> bannedUserIds;
        if (filter is UiModels.UserListFilter.Active or UiModels.UserListFilter.Kicked)
        {
            bannedUserIds = [];
        }
        else
        {
            bannedUserIds = await context.TelegramUsers
                .AsNoTracking()
                .Where(u => userIds.Contains(u.TelegramUserId) && u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now))
                .Select(u => u.TelegramUserId)
                .ToHashSetAsync(cancellationToken);
        }

        // Users with notes
        var usersWithNotes = await context.AdminNotes
            .Where(n => userIds.Contains(n.TelegramUserId))
            .Select(n => n.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Users with tags
        var usersWithTags = await context.UserTags
            .Where(t => userIds.Contains(t.TelegramUserId))
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Users who are admins
        var usersWhoAreAdmins = await context.ChatAdmins
            .Where(ca => ca.IsActive && userIds.Contains(ca.TelegramId))
            .Select(ca => ca.TelegramId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        // Populate stats
        foreach (var user in users)
        {
            user.ChatCount = chatCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.WarningCount = warningCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.NoteCount = noteCounts.GetValueOrDefault(user.TelegramUserId, 0);
            user.IsBanned = bannedUserIds.Contains(user.TelegramUserId);
            user.HasWarnings = warningCounts.GetValueOrDefault(user.TelegramUserId, 0) > 0;
            user.IsTagged = usersWithNotes.Contains(user.TelegramUserId) || usersWithTags.Contains(user.TelegramUserId);
            user.IsAdmin = usersWhoAreAdmins.Contains(user.TelegramUserId);
        }
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
            .Where(u => u.Warnings!.Any())
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

        // Get user actions with actor display name enrichment (LEFT JOINs for IssuedBy)
        var actions = await (
            from ua in context.UserActions.AsNoTracking()
            join tu in context.TelegramUsers.AsNoTracking() on ua.TelegramUserId equals tu.TelegramUserId into telegramActors
            from ta in telegramActors.DefaultIfEmpty()
            join wu in context.Users.AsNoTracking() on ua.WebUserId equals wu.Id into webActors
            from wa in webActors.DefaultIfEmpty()
            join target in context.TelegramUsers.AsNoTracking() on ua.UserId equals target.TelegramUserId into targets
            from t in targets.DefaultIfEmpty()
            where ua.UserId == telegramUserId
            orderby ua.IssuedAt descending
            select new
            {
                Action = ua,
                TelegramActorUsername = ta.Username,
                TelegramActorFirstName = ta.FirstName,
                TelegramActorLastName = ta.LastName,
                WebActorEmail = wa.Email,
                TargetUsername = t.Username,
                TargetFirstName = t.FirstName,
                TargetLastName = t.LastName
            }
        ).ToListAsync(cancellationToken);

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

        // Get latest profile scan result for AI reason/signals display
        var latestScanResult = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == telegramUserId)
            .OrderByDescending(r => r.ScannedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new UiModels.TelegramUserDetail
        {
            User = new UserIdentity(user.TelegramUserId, user.FirstName, user.LastName, user.Username),
            UserPhotoPath = user.UserPhotoPath,
            PhotoHash = user.PhotoHash,
            IsTrusted = user.IsTrusted,
            IsBanned = user.IsBanned,
            BanExpiresAt = user.BanExpiresAt,
            KickCount = user.KickCount,
            BotDmEnabled = user.BotDmEnabled,
            FirstSeenAt = user.FirstSeenAt,
            LastSeenAt = user.LastSeenAt,
            Bio = user.Bio,
            PersonalChannelId = user.PersonalChannelId,
            PersonalChannelTitle = user.PersonalChannelTitle,
            PersonalChannelAbout = user.PersonalChannelAbout,
            HasPinnedStories = user.HasPinnedStories,
            PinnedStoryCaptions = user.PinnedStoryCaptions,
            IsScam = user.IsScam,
            IsFake = user.IsFake,
            IsVerified = user.IsVerified,
            ProfileScanExcluded = user.ProfileScanExcluded,
            ProfileScannedAt = user.ProfileScannedAt,
            ProfileScanScore = user.ProfileScanScore,
            LatestAiReason = latestScanResult?.AiReason,
            LatestAiSignals = latestScanResult?.AiSignals,
            ChatMemberships = chatMemberships,
            Actions = actions.Select(a => a.Action.ToModel(
                webUserEmail: a.WebActorEmail,
                telegramUsername: a.TelegramActorUsername,
                telegramFirstName: a.TelegramActorFirstName,
                telegramLastName: a.TelegramActorLastName,
                targetUsername: a.TargetUsername,
                targetFirstName: a.TargetFirstName,
                targetLastName: a.TargetLastName)).ToList(),
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
        user.BannedAt = isBanned ? DateTimeOffset.UtcNow : null; // Clear on unban
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

        if (user?.Warnings is not { Count: > 0 }) return 0;

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

    /// <inheritdoc />
    public async Task<int> IncrementKickCountAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rowsAffected = await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.KickCount, u => u.KickCount + 1)
                .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (rowsAffected == 0)
        {
            _logger.LogWarning("Cannot increment kick count for unknown user {UserId}", telegramUserId);
            return 0;
        }

        _logger.LogInformation("Incremented kick count for user {UserId}", telegramUserId);

        return rowsAffected;
    }

    /// <inheritdoc />
    public async Task<int> GetKickCountAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.TelegramUsers
            .AsNoTracking()
            .Where(u => u.TelegramUserId == telegramUserId)
            .Select(u => u.KickCount)
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
    public async Task ActivateAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.TelegramUsers
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Cannot activate unknown user {User}", user.ToLogDebug(telegramUserId));
            return;
        }

        user.IsActive = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Activated {User}", user.ToLogInfo());
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

    // ============================================================================
    // Profile Scan Methods
    // ============================================================================

    /// <inheritdoc />
    public async Task<ChatIdentity?> GetFirstChatForUserAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var result = await (
            from m in context.Messages
            where m.UserId == telegramUserId && m.DeletedAt == null
            join c in context.ManagedChats on m.ChatId equals c.ChatId into chatGroup
            from chat in chatGroup.DefaultIfEmpty()
            group new { m, chat } by new { m.ChatId, ChatName = chat != null ? chat.ChatName : null } into g
            orderby g.Max(x => x.m.Timestamp) descending
            select new { g.Key.ChatId, g.Key.ChatName }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        return result is null ? null : new ChatIdentity(result.ChatId, result.ChatName);
    }

    /// <inheritdoc />
    public async Task ExcludeFromProfileScanAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileScanExcluded, true)
                .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <inheritdoc />
    public async Task IncludeInProfileScanAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileScanExcluded, false)
                .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<long>> GetEligibleUsersForRescanAsync(int batchSize, DateTimeOffset rescanCutoff, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TelegramUsers
            .Where(u => !u.IsBanned && !u.IsBot && !u.IsTrusted && !u.ProfileScanExcluded)
            .Where(u => u.ProfileScannedAt == null || u.ProfileScannedAt < rescanCutoff)
            .OrderBy(u => u.ProfileScannedAt) // NULLS FIRST is PostgreSQL default for ASC
            .Take(batchSize)
            .Select(u => u.TelegramUserId)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateProfileScanDataAsync(
        long telegramUserId,
        string? bio,
        long? personalChannelId,
        string? personalChannelTitle,
        string? personalChannelAbout,
        bool hasPinnedStories,
        string? pinnedStoryCaptions,
        bool isScam,
        bool isFake,
        bool isVerified,
        decimal profileScanScore,
        long? profilePhotoId,
        long? personalChannelPhotoId,
        string? pinnedStoryIds,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Bio, bio)
                .SetProperty(u => u.PersonalChannelId, personalChannelId)
                .SetProperty(u => u.PersonalChannelTitle, personalChannelTitle)
                .SetProperty(u => u.PersonalChannelAbout, personalChannelAbout)
                .SetProperty(u => u.HasPinnedStories, hasPinnedStories)
                .SetProperty(u => u.PinnedStoryCaptions, pinnedStoryCaptions)
                .SetProperty(u => u.IsScam, isScam)
                .SetProperty(u => u.IsFake, isFake)
                .SetProperty(u => u.IsVerified, isVerified)
                .SetProperty(u => u.ProfileScanScore, profileScanScore)
                .SetProperty(u => u.ProfilePhotoId, profilePhotoId)
                .SetProperty(u => u.PersonalChannelPhotoId, personalChannelPhotoId)
                .SetProperty(u => u.PinnedStoryIds, pinnedStoryIds)
                .SetProperty(u => u.ProfileScannedAt, DateTimeOffset.UtcNow)
                .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateProfileScannedAtAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileScannedAt, DateTimeOffset.UtcNow)
                .SetProperty(u => u.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }
}

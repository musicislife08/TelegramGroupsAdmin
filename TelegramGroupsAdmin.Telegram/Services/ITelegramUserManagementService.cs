using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing Telegram users - queries, trust status, bans, and user details.
/// </summary>
public interface ITelegramUserManagementService
{
    /// <summary>Gets all Telegram users (used by WebAdminAccounts for account linking).</summary>
    Task<List<TelegramUserListItem>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets moderation queue statistics (pending reports, reviews).</summary>
    Task<ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a page of users filtered by tab, search, and accessible chats.</summary>
    Task<(List<TelegramUserListItem> Items, int TotalCount)> GetPagedUsersAsync(
        UserListFilter filter, int skip, int take,
        string? searchText, List<long>? chatIds,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a page of banned users with full ban details.</summary>
    Task<(List<BannedUserListItem> Items, int TotalCount)> GetPagedBannedUsersWithDetailsAsync(
        int skip, int take, string? searchText,
        string? sortLabel, bool sortDescending,
        CancellationToken cancellationToken = default);

    /// <summary>Gets counts for all user tabs (5 parallel COUNT queries).</summary>
    Task<UserTabCounts> GetUserTabCountsAsync(
        List<long>? chatIds, string? searchText,
        CancellationToken cancellationToken = default);

    /// <summary>Gets detailed information for a specific user.</summary>
    Task<TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>Toggles the trust status of a user.</summary>
    Task<bool> ToggleTrustAsync(long telegramUserId, Actor modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>Gets the action history for a user (bans, warnings, etc.).</summary>
    Task<List<UserActionRecord>> GetUserActionsAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>Checks if a user is currently banned.</summary>
    Task<bool> IsBannedAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>Gets the count of active (non-expired) warnings for a user.</summary>
    Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>Unbans a user from all chats.</summary>
    Task<bool> UnbanAsync(long telegramUserId, Actor unbannedBy, string? reason = null, CancellationToken cancellationToken = default);
}

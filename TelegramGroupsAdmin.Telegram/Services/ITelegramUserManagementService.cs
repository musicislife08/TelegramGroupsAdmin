using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing Telegram users - queries, trust status, bans, and user details.
/// </summary>
public interface ITelegramUserManagementService
{
    /// <summary>Gets all Telegram users.</summary>
    Task<List<TelegramUserListItem>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets users filtered by specific chat IDs.</summary>
    Task<List<TelegramUserListItem>> GetAllUsersAsync(List<long> chatIds, CancellationToken cancellationToken = default);

    /// <summary>Gets users with any tags applied.</summary>
    Task<List<TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets all banned users.</summary>
    Task<List<TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets banned users with detailed ban information.</summary>
    Task<List<BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets all trusted users.</summary>
    Task<List<TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets inactive users based on activity threshold.</summary>
    Task<List<TelegramUserListItem>> GetInactiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets moderation queue statistics (pending reports, reviews).</summary>
    Task<ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default);

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

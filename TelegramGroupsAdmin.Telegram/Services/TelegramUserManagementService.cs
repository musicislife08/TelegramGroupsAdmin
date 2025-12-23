using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;

using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Orchestration service for Telegram user management operations.
/// Coordinates between TelegramUserRepository, UserActionRepository, and DetectionResultRepository.
/// </summary>
public class TelegramUserManagementService
{
    private readonly ITelegramUserRepository _userRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<TelegramUserManagementService> _logger;

    public TelegramUserManagementService(
        ITelegramUserRepository userRepository,
        IUserActionsRepository userActionsRepository,
        ILogger<TelegramUserManagementService> logger)
    {
        _userRepository = userRepository;
        _userActionsRepository = userActionsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all Telegram users with computed stats for list view
    /// </summary>
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetAllWithStatsAsync(cancellationToken);
    }

    /// <summary>
    /// Get Telegram users filtered by accessible chats
    /// </summary>
    /// <param name="chatIds">List of accessible chat IDs (empty = all users for GlobalAdmin/Owner)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(List<long> chatIds, CancellationToken cancellationToken = default)
    {
        // Empty list means all chats (GlobalAdmin/Owner)
        if (chatIds.Count == 0)
        {
            return _userRepository.GetAllWithStatsAsync(cancellationToken);
        }

        return _userRepository.GetAllWithStatsAsync(chatIds, cancellationToken);
    }

    /// <summary>
    /// Get users with tags or notes for tracking (includes warned users)
    /// </summary>
    public Task<List<TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTaggedUsersAsync(cancellationToken);
    }

    /// <summary>
    /// Get banned users
    /// </summary>
    public Task<List<TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetBannedUsersAsync(cancellationToken);
    }

    /// <summary>
    /// Get banned users with detailed ban information
    /// Includes ban date, banned by, reason, expiry, and trigger message
    /// </summary>
    public Task<List<BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetBannedUsersWithDetailsAsync(cancellationToken);
    }

    /// <summary>
    /// Get trusted (whitelisted) users
    /// </summary>
    public Task<List<TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTrustedUsersAsync(cancellationToken);
    }

    /// <summary>
    /// Get inactive users (joined but never engaged - no welcome accept or message)
    /// </summary>
    public Task<List<TelegramUserListItem>> GetInactiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetInactiveUsersAsync(cancellationToken);
    }

    /// <summary>
    /// Get top active users by 30-day message count
    /// </summary>
    public Task<List<TopActiveUser>> GetTopActiveUsersAsync(int limit = 3, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTopActiveUsersAsync(limit: limit, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get moderation queue statistics (banned, warned, flagged counts)
    /// </summary>
    public Task<ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetModerationQueueStatsAsync(cancellationToken);
    }

    /// <summary>
    /// Get detailed user info with all related data (chat memberships, actions, detection history)
    /// </summary>
    public Task<TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetUserDetailAsync(telegramUserId, cancellationToken);
    }

    /// <summary>
    /// Toggle user trust status (whitelist on/off)
    /// Creates audit trail via user_actions table.
    /// </summary>
    public async Task<bool> ToggleTrustAsync(long telegramUserId, Actor modifiedBy, CancellationToken cancellationToken = default)
    {
        // Get current user
        var user = await _userRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Cannot toggle trust for non-existent user {TelegramUserId}", telegramUserId);
            return false;
        }

        // Toggle trust status
        var newTrustStatus = !user.IsTrusted;

        // Protect Telegram system accounts - cannot remove trust
        if (TelegramConstants.IsSystemUser(telegramUserId) && !newTrustStatus)
        {
            _logger.LogWarning(
                "Blocked attempt to remove trust from Telegram system account (user {TelegramUserId}). " +
                "System accounts must always remain trusted.",
                telegramUserId);
            return false;
        }
        await _userRepository.UpdateTrustStatusAsync(telegramUserId, newTrustStatus, cancellationToken);

        if (newTrustStatus)
        {
            // Create trust action record
            var userAction = new UserActionRecord(
                Id: 0, // Will be set by database
                UserId: telegramUserId,
                ActionType: UserActionType.Trust,
                MessageId: null, // No specific message associated
                IssuedBy: modifiedBy,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Trust doesn't expire
                Reason: "User manually trusted"
            );

            await _userActionsRepository.InsertAsync(userAction);
        }
        else
        {
            // Expire all active trusts
            await _userActionsRepository.ExpireTrustsForUserAsync(telegramUserId);

            // Create untrust action record (audit trail for who removed trust and when)
            var untrustAction = new UserActionRecord(
                Id: 0,
                UserId: telegramUserId,
                ActionType: UserActionType.Untrust,
                MessageId: null,
                IssuedBy: modifiedBy,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Untrust doesn't need expiration - it's a point-in-time action
                Reason: "User manually untrusted"
            );

            await _userActionsRepository.InsertAsync(untrustAction);
        }

        _logger.LogInformation(
            "User {TelegramUserId} trust toggled to {IsTrusted} by {ModifiedBy}",
            telegramUserId,
            newTrustStatus,
            modifiedBy);

        return true;
    }

    /// <summary>
    /// Get all user actions (warnings, bans, trusts) for a specific user
    /// </summary>
    public Task<List<UserActionRecord>> GetUserActionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userActionsRepository.GetByUserIdAsync(telegramUserId);
    }

    /// <summary>
    /// Check if user is currently banned.
    /// REFACTOR-5: Uses is_banned column as source of truth (not user_actions audit log).
    /// </summary>
    public Task<bool> IsBannedAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.IsBannedAsync(telegramUserId, cancellationToken);
    }

    /// <summary>
    /// Get active warning count for user.
    /// REFACTOR-5: Uses warnings JSONB column as source of truth (not user_actions audit log).
    /// </summary>
    public Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetActiveWarningCountAsync(telegramUserId, cancellationToken);
    }

    /// <summary>
    /// Unban a user (expire all active bans and create unban action record)
    /// Phase 5: Banned users tab enhancements
    /// </summary>
    public async Task<bool> UnbanAsync(long telegramUserId, Actor unbannedBy, string? reason = null, CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _userRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Cannot unban non-existent user {TelegramUserId}", telegramUserId);
            return false;
        }

        // REFACTOR-5: Check if user is actually banned using source of truth (is_banned column)
        var isBanned = await _userRepository.IsBannedAsync(telegramUserId, cancellationToken);
        if (!isBanned)
        {
            _logger.LogWarning("User {TelegramUserId} is not currently banned", telegramUserId);
            return false;
        }

        // Expire all active bans (global - chatId: null)
        await _userActionsRepository.ExpireBansForUserAsync(telegramUserId, chatId: null, cancellationToken);

        // Create unban action record for audit trail
        var unbanAction = new UserActionRecord(
            Id: 0, // Will be set by database
            UserId: telegramUserId,
            ActionType: UserActionType.Unban,
            MessageId: null, // No specific message associated
            IssuedBy: unbannedBy,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null, // Unban doesn't expire
            Reason: reason ?? "User manually unbanned from web interface"
        );

        await _userActionsRepository.InsertAsync(unbanAction, cancellationToken);

        _logger.LogInformation(
            "User {TelegramUserId} unbanned by {UnbannedBy}. Reason: {Reason}",
            telegramUserId,
            unbannedBy,
            reason ?? "Manual unban");

        return true;
    }
}

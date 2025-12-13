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
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetAllWithStatsAsync(ct);
    }

    /// <summary>
    /// Get Telegram users filtered by accessible chats
    /// </summary>
    /// <param name="chatIds">List of accessible chat IDs (empty = all users for GlobalAdmin/Owner)</param>
    /// <param name="ct">Cancellation token</param>
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(List<long> chatIds, CancellationToken ct = default)
    {
        // Empty list means all chats (GlobalAdmin/Owner)
        if (chatIds.Count == 0)
        {
            return _userRepository.GetAllWithStatsAsync(ct);
        }

        return _userRepository.GetAllWithStatsAsync(chatIds, ct);
    }

    /// <summary>
    /// Get users with tags or notes for tracking (includes warned users)
    /// </summary>
    public Task<List<TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetTaggedUsersAsync(ct);
    }

    /// <summary>
    /// Get banned users
    /// </summary>
    public Task<List<TelegramUserListItem>> GetBannedUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetBannedUsersAsync(ct);
    }

    /// <summary>
    /// Get banned users with detailed ban information
    /// Includes ban date, banned by, reason, expiry, and trigger message
    /// </summary>
    public Task<List<BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken ct = default)
    {
        return _userRepository.GetBannedUsersWithDetailsAsync(ct);
    }

    /// <summary>
    /// Get trusted (whitelisted) users
    /// </summary>
    public Task<List<TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetTrustedUsersAsync(ct);
    }

    /// <summary>
    /// Get top active users by 30-day message count
    /// </summary>
    public Task<List<TopActiveUser>> GetTopActiveUsersAsync(int limit = 3, CancellationToken ct = default)
    {
        return _userRepository.GetTopActiveUsersAsync(limit: limit, ct: ct);
    }

    /// <summary>
    /// Get moderation queue statistics (banned, warned, flagged counts)
    /// </summary>
    public Task<ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken ct = default)
    {
        return _userRepository.GetModerationQueueStatsAsync(ct);
    }

    /// <summary>
    /// Get detailed user info with all related data (chat memberships, actions, detection history)
    /// </summary>
    public Task<TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userRepository.GetUserDetailAsync(telegramUserId, ct);
    }

    /// <summary>
    /// Toggle user trust status (whitelist on/off)
    /// Creates audit trail via user_actions table.
    /// </summary>
    public async Task<bool> ToggleTrustAsync(long telegramUserId, Actor modifiedBy, CancellationToken ct = default)
    {
        // Get current user
        var user = await _userRepository.GetByTelegramIdAsync(telegramUserId, ct);
        if (user == null)
        {
            _logger.LogWarning("Cannot toggle trust for non-existent user {TelegramUserId}", telegramUserId);
            return false;
        }

        // Toggle trust status
        var newTrustStatus = !user.IsTrusted;

        // Protect Telegram service account - cannot remove trust
        if (telegramUserId == TelegramConstants.ServiceAccountUserId && !newTrustStatus)
        {
            _logger.LogWarning(
                "Blocked attempt to remove trust from Telegram service account (user {TelegramUserId}). " +
                "Service account must always remain trusted.",
                telegramUserId);
            return false;
        }
        await _userRepository.UpdateTrustStatusAsync(telegramUserId, newTrustStatus, ct);

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
    public Task<List<UserActionRecord>> GetUserActionsAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userActionsRepository.GetByUserIdAsync(telegramUserId);
    }

    /// <summary>
    /// Check if user is currently banned.
    /// REFACTOR-5: Uses is_banned column as source of truth (not user_actions audit log).
    /// </summary>
    public Task<bool> IsBannedAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userRepository.IsBannedAsync(telegramUserId, ct);
    }

    /// <summary>
    /// Get active warning count for user.
    /// REFACTOR-5: Uses warnings JSONB column as source of truth (not user_actions audit log).
    /// </summary>
    public Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userRepository.GetActiveWarningCountAsync(telegramUserId, ct);
    }

    /// <summary>
    /// Unban a user (expire all active bans and create unban action record)
    /// Phase 5: Banned users tab enhancements
    /// </summary>
    public async Task<bool> UnbanAsync(long telegramUserId, Actor unbannedBy, string? reason = null, CancellationToken ct = default)
    {
        // Verify user exists
        var user = await _userRepository.GetByTelegramIdAsync(telegramUserId, ct);
        if (user == null)
        {
            _logger.LogWarning("Cannot unban non-existent user {TelegramUserId}", telegramUserId);
            return false;
        }

        // REFACTOR-5: Check if user is actually banned using source of truth (is_banned column)
        var isBanned = await _userRepository.IsBannedAsync(telegramUserId, ct);
        if (!isBanned)
        {
            _logger.LogWarning("User {TelegramUserId} is not currently banned", telegramUserId);
            return false;
        }

        // Expire all active bans (global - chatId: null)
        await _userActionsRepository.ExpireBansForUserAsync(telegramUserId, chatId: null, ct);

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

        await _userActionsRepository.InsertAsync(unbanAction, ct);

        _logger.LogInformation(
            "User {TelegramUserId} unbanned by {UnbannedBy}. Reason: {Reason}",
            telegramUserId,
            unbannedBy,
            reason ?? "Manual unban");

        return true;
    }
}

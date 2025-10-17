using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Orchestration service for Telegram user management operations.
/// Coordinates between TelegramUserRepository, UserActionRepository, and DetectionResultRepository.
/// </summary>
public class TelegramUserManagementService
{
    private readonly TelegramUserRepository _userRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ILogger<TelegramUserManagementService> _logger;

    public TelegramUserManagementService(
        TelegramUserRepository userRepository,
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
    /// Get users flagged for review (warnings, notes, tags)
    /// </summary>
    public Task<List<TelegramUserListItem>> GetFlaggedUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetFlaggedUsersAsync(ct);
    }

    /// <summary>
    /// Get banned users
    /// </summary>
    public Task<List<TelegramUserListItem>> GetBannedUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.GetBannedUsersAsync(ct);
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
        return _userRepository.GetTopActiveUsersAsync(limit, ct);
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
    public async Task<bool> ToggleTrustAsync(long telegramUserId, string modifiedBy, CancellationToken ct = default)
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
    /// Check if user is currently banned (has active ban action)
    /// </summary>
    public Task<bool> IsBannedAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userActionsRepository.IsUserBannedAsync(telegramUserId);
    }

    /// <summary>
    /// Get active warning count for user
    /// </summary>
    public Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken ct = default)
    {
        return _userActionsRepository.GetWarnCountAsync(telegramUserId);
    }
}

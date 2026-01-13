using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Orchestration service for Telegram user management operations.
/// Coordinates between TelegramUserRepository, UserActionRepository, and DetectionResultRepository.
/// </summary>
public class TelegramUserManagementService : ITelegramUserManagementService
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

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetAllWithStatsAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetAllUsersAsync(List<long> chatIds, CancellationToken cancellationToken = default)
    {
        // Empty list means all chats (GlobalAdmin/Owner)
        if (chatIds.Count == 0)
        {
            return _userRepository.GetAllWithStatsAsync(cancellationToken);
        }

        return _userRepository.GetAllWithStatsAsync(chatIds, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetTaggedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTaggedUsersAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetBannedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetBannedUsersAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<BannedUserListItem>> GetBannedUsersWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetBannedUsersWithDetailsAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetTrustedUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTrustedUsersAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TelegramUserListItem>> GetInactiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetInactiveUsersAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<TopActiveUser>> GetTopActiveUsersAsync(int limit = 3, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetTopActiveUsersAsync(limit: limit, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ModerationQueueStats> GetModerationQueueStatsAsync(CancellationToken cancellationToken = default)
    {
        return _userRepository.GetModerationQueueStatsAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TelegramUserDetail?> GetUserDetailAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetUserDetailAsync(telegramUserId, cancellationToken);
    }

    /// <inheritdoc/>
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
                "Blocked attempt to remove trust from Telegram system account ({User}). " +
                "System accounts must always remain trusted.",
                user.ToLogDebug());
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
            "{User} trust toggled to {IsTrusted} by {ModifiedBy}",
            user.ToLogInfo(),
            newTrustStatus,
            modifiedBy);

        return true;
    }

    /// <inheritdoc/>
    public Task<List<UserActionRecord>> GetUserActionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userActionsRepository.GetByUserIdAsync(telegramUserId);
    }

    /// <inheritdoc/>
    public Task<bool> IsBannedAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.IsBannedAsync(telegramUserId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<int> GetActiveWarningCountAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _userRepository.GetActiveWarningCountAsync(telegramUserId, cancellationToken);
    }

    /// <inheritdoc/>
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
            _logger.LogWarning("{User} is not currently banned", user.ToLogDebug());
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
            "{User} unbanned by {UnbannedBy}. Reason: {Reason}",
            user.ToLogInfo(),
            unbannedBy,
            reason ?? "Manual unban");

        return true;
    }
}

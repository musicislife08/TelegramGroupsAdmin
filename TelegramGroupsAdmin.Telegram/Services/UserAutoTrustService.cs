using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles automatic user trust (whitelisting) after users prove themselves with N non-spam messages.
/// Implements the FirstMessageOnly feature - users are checked for first N messages, then auto-trusted.
/// </summary>
public class UserAutoTrustService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly IContentDetectionConfigRepository _contentDetectionConfigRepository;
    private readonly ITelegramUserRepository _userRepository;
    private readonly ILogger<UserAutoTrustService> _logger;

    public UserAutoTrustService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        IContentDetectionConfigRepository contentDetectionConfigRepository,
        ITelegramUserRepository userRepository,
        ILogger<UserAutoTrustService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _contentDetectionConfigRepository = contentDetectionConfigRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <summary>
    /// Check if user should be auto-trusted based on recent non-spam messages.
    /// Called after storing a non-spam detection result.
    /// Uses AddOrUpdate pattern - safe to call even if user already trusted.
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <param name="chatId">Chat ID where message was posted (used for config lookup)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CheckAndApplyAutoTrustAsync(long userId, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get effective config (chat-specific overrides, global defaults)
            var config = await _contentDetectionConfigRepository.GetEffectiveConfigAsync(chatId, cancellationToken);

            // Feature disabled - skip
            if (!config.FirstMessageOnly)
            {
                _logger.LogDebug(
                    "FirstMessageOnly disabled for chat {ChatId}, skipping auto-trust check for user {UserId}",
                    chatId,
                    userId);
                return;
            }

            // Check account age requirement (prevents quick hit-and-run attacks)
            // Skip check entirely if AutoTrustMinAccountAgeHours = 0 (disabled)
            if (config.AutoTrustMinAccountAgeHours > 0)
            {
                var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
                if (user == null)
                {
                    _logger.LogDebug("User {UserId} not found in telegram_users, skipping auto-trust", userId);
                    return;
                }

                var accountAge = DateTimeOffset.UtcNow - user.FirstSeenAt;
                var requiredAge = TimeSpan.FromHours(config.AutoTrustMinAccountAgeHours);
                if (accountAge < requiredAge)
                {
                    _logger.LogDebug(
                        "User {UserId} account age {AgeHours:F1}h < required {RequiredHours}h, not yet eligible for auto-trust",
                        userId,
                        accountAge.TotalHours,
                        config.AutoTrustMinAccountAgeHours);
                    return;
                }
            }

            // Get last N non-spam detection results for this user (global, across all chats)
            // Filter by minimum message length to prevent trust gaming with short replies
            var recentResults = await _detectionResultsRepository.GetRecentNonSpamResultsForUserAsync(
                userId,
                limit: config.FirstMessagesCount,
                minMessageLength: config.AutoTrustMinMessageLength,
                cancellationToken);

            // Not enough qualifying non-spam messages yet
            if (recentResults.Count < config.FirstMessagesCount)
            {
                _logger.LogDebug(
                    "User {UserId} has {Count}/{Threshold} qualifying messages (min {MinLength} chars), not yet eligible for auto-trust",
                    userId,
                    recentResults.Count,
                    config.FirstMessagesCount,
                    config.AutoTrustMinMessageLength);
                return;
            }

            // User has N consecutive non-spam messages - add to trust (global)
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                IssuedBy: Actor.AutoTrust, // System-issued
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent (until revoked)
                Reason: $"Auto-trusted after {config.FirstMessagesCount} non-spam messages"
            );

            // AddOrUpdate pattern - safe even if already trusted
            var actionId = await _userActionsRepository.InsertAsync(trustAction, cancellationToken);

            // Update telegram_users.is_trusted flag for UI display (same as manual trust)
            await _userRepository.UpdateTrustStatusAsync(userId, isTrusted: true, cancellationToken);

            _logger.LogInformation(
                "Auto-trusted user {UserId} after {Count} non-spam messages (action ID: {ActionId})",
                userId,
                config.FirstMessagesCount,
                actionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check/apply auto-trust for user {UserId} in chat {ChatId}",
                userId,
                chatId);
        }
    }
}

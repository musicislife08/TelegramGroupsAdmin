using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;

using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles automatic user trust (whitelisting) after users prove themselves with N non-spam messages.
/// Implements the FirstMessageOnly feature - users are checked for first N messages, then auto-trusted.
/// </summary>
public class UserAutoTrustService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly ISpamDetectionConfigRepository _spamDetectionConfigRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserAutoTrustService> _logger;

    public UserAutoTrustService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        ISpamDetectionConfigRepository spamDetectionConfigRepository,
        IAuditLogRepository auditLogRepository,
        ILogger<UserAutoTrustService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _spamDetectionConfigRepository = spamDetectionConfigRepository;
        _auditLogRepository = auditLogRepository;
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
            var config = await _spamDetectionConfigRepository.GetEffectiveConfigAsync(chatId, cancellationToken);

            // Feature disabled - skip
            if (!config.FirstMessageOnly)
            {
                _logger.LogDebug(
                    "FirstMessageOnly disabled for chat {ChatId}, skipping auto-trust check for user {UserId}",
                    chatId,
                    userId);
                return;
            }

            // Get last N non-spam detection results for this user (global, across all chats)
            var recentResults = await _detectionResultsRepository.GetRecentNonSpamResultsForUserAsync(
                userId,
                limit: config.FirstMessagesCount,
                cancellationToken);

            // Not enough non-spam messages yet
            if (recentResults.Count < config.FirstMessagesCount)
            {
                _logger.LogDebug(
                    "User {UserId} has {Count}/{Threshold} non-spam messages, not yet eligible for auto-trust",
                    userId,
                    recentResults.Count,
                    config.FirstMessagesCount);
                return;
            }

            // User has N consecutive non-spam messages - add to trust (global)
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                IssuedBy: Actor.FromSystem("auto_trust"), // System-issued
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent (until revoked)
                Reason: $"Auto-trusted after {config.FirstMessagesCount} non-spam messages"
            );

            // AddOrUpdate pattern - safe even if already trusted
            var actionId = await _userActionsRepository.InsertAsync(trustAction, cancellationToken);

            _logger.LogInformation(
                "Auto-trusted user {UserId} after {Count} non-spam messages (action ID: {ActionId})",
                userId,
                config.FirstMessagesCount,
                actionId);

            // Log to audit log (Telegram user as target)
            await _auditLogRepository.LogEventAsync(
                Data.Models.AuditEventType.UserAutoWhitelisted,
                actorUserId: "system",
                targetUserId: userId.ToString(), // Telegram user ID as target
                value: $"Auto-trusted after {config.FirstMessagesCount} non-spam messages",
                cancellationToken);
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

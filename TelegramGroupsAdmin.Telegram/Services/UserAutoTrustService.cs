using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TgUser = Telegram.Bot.Types.User;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles automatic user trust (whitelisting) after users prove themselves with N non-spam messages.
/// Implements the FirstMessageOnly feature - users are checked for first N messages, then auto-trusted.
/// </summary>
public class UserAutoTrustService
{
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly IConfigService _configService;
    private readonly ITelegramUserRepository _userRepository;
    private readonly ILogger<UserAutoTrustService> _logger;

    public UserAutoTrustService(
        IDetectionResultsRepository detectionResultsRepository,
        IUserActionsRepository userActionsRepository,
        IConfigService configService,
        ITelegramUserRepository userRepository,
        ILogger<UserAutoTrustService> logger)
    {
        _detectionResultsRepository = detectionResultsRepository;
        _userActionsRepository = userActionsRepository;
        _configService = configService;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <summary>
    /// Check if user should be auto-trusted based on recent non-spam messages.
    /// Called after storing a non-spam detection result.
    /// Uses AddOrUpdate pattern - safe to call even if user already trusted.
    /// </summary>
    /// <param name="tgUser">Telegram SDK User object</param>
    /// <param name="chat">Telegram SDK Chat object (used for config lookup)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CheckAndApplyAutoTrustAsync(TgUser tgUser, Chat chat, CancellationToken cancellationToken = default)
    {
        var userId = tgUser.Id;
        var chatId = chat.Id;

        try
        {
            // Get effective config via ConfigService (single entry point for all config)
            var config = await _configService.GetEffectiveAsync<ContentDetectionConfig>(ConfigType.ContentDetection, chatId)
                        ?? new ContentDetectionConfig();

            // Feature disabled - skip
            if (!config.FirstMessageOnly)
            {
                _logger.LogDebug(
                    "FirstMessageOnly disabled for {Chat}, skipping auto-trust check for {User}",
                    chat.ToLogDebug(),
                    tgUser.ToLogDebug());
                return;
            }

            // Check account age requirement (prevents quick hit-and-run attacks)
            // Skip check entirely if AutoTrustMinAccountAgeHours = 0 (disabled)
            if (config.AutoTrustMinAccountAgeHours > 0)
            {
                var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
                if (user == null)
                {
                    _logger.LogDebug("{User} not found in telegram_users, skipping auto-trust",
                        tgUser.ToLogDebug());
                    return;
                }

                var accountAge = DateTimeOffset.UtcNow - user.FirstSeenAt;
                var requiredAge = TimeSpan.FromHours(config.AutoTrustMinAccountAgeHours);
                if (accountAge < requiredAge)
                {
                    _logger.LogDebug(
                        "{User} account age {AgeHours:F1}h < required {RequiredHours}h, not yet eligible for auto-trust",
                        tgUser.ToLogDebug(),
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
                    "{User} has {Count}/{Threshold} qualifying messages (min {MinLength} chars), not yet eligible for auto-trust",
                    tgUser.ToLogDebug(),
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
                "Auto-trusted {User} after {Count} non-spam messages (action ID: {ActionId})",
                tgUser.ToLogInfo(),
                config.FirstMessagesCount,
                actionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check/apply auto-trust for {User} in {Chat}",
                tgUser.ToLogDebug(),
                chat.ToLogDebug());
        }
    }
}

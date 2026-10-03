using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
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
            // Get effective config via IConfigService
            var config = await _configService.GetEffectiveContentDetectionAsync(chatId, cancellationToken);

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

            // Last N messages (latest version each), consecutively: any spam or short message in the
            // window blocks trust. Edits never add messages. UntrainedHam counts as ham.
            var recent = await _detectionResultsRepository.GetRecentMessageVerdictsForUserAsync(
                userId, config.FirstMessagesCount, cancellationToken);

            if (recent.Count < config.FirstMessagesCount
                || recent.Any(r => r.IsSpam || r.TextLength < config.AutoTrustMinMessageLength))
            {
                _logger.LogDebug(
                    "{User} not eligible for auto-trust: {Count}/{Threshold} messages, {Spam} spam, {Short} shorter than {MinLength}",
                    tgUser.ToLogDebug(), recent.Count, config.FirstMessagesCount,
                    recent.Count(r => r.IsSpam), recent.Count(r => r.TextLength < config.AutoTrustMinMessageLength),
                    config.AutoTrustMinMessageLength);
                return;
            }

            // User has N consecutive non-spam messages - add to trust (global)
            var trustAction = new UserActionRecord(
                Id: 0,
                UserId: userId,
                ActionType: UserActionType.Trust,
                MessageId: null,
                ChatId: null,
                IssuedBy: Actor.AutoTrust, // System-issued
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null, // Permanent (until revoked)
                Reason: $"Auto-trusted after {config.FirstMessagesCount} consecutive non-spam messages"
            );

            // AddOrUpdate pattern - safe even if already trusted
            var actionId = await _userActionsRepository.InsertAsync(trustAction, cancellationToken);

            // Update telegram_users.is_trusted flag for UI display (same as manual trust)
            await _userRepository.TrustUserAsync(userId, cancellationToken);

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

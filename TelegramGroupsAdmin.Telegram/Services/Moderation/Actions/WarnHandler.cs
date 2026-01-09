using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for warning operations.
/// Updates the JSONB warnings collection on telegram_users table.
/// Does NOT know about bans, trust, or notifications (orchestrator composes those).
/// </summary>
public class WarnHandler : IWarnHandler
{

    private readonly ITelegramUserRepository _userRepository;
    private readonly ILogger<WarnHandler> _logger;

    public WarnHandler(
        ITelegramUserRepository userRepository,
        ILogger<WarnHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WarnResult> WarnAsync(
        long userId,
        Actor executor,
        string? reason,
        long chatId,
        long? messageId = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Issuing warning for user {User} by {Executor}",
            user.ToLogDebug(userId), executor.GetDisplayText());

        try
        {
            var now = DateTimeOffset.UtcNow;

            // Create warning entry for JSONB
            var warning = new WarningEntry
            {
                IssuedAt = now,
                ExpiresAt = now.Add(ModerationConstants.DefaultWarningExpiry),
                Reason = reason,
                ActorType = GetActorType(executor),
                ActorId = GetActorId(executor),
                ChatId = chatId,
                MessageId = messageId
            };

            // Add warning to user's JSONB collection and get active count
            var activeCount = await _userRepository.AddWarningAsync(userId, warning, cancellationToken);

            _logger.LogInformation(
                "Warning issued for {User}: total active warnings {WarnCount}",
                user.ToLogInfo(userId), activeCount);

            return WarnResult.Succeeded(activeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to issue warning for user {User}", user.ToLogDebug(userId));
            return WarnResult.Failed(ex.Message);
        }
    }

    private static string GetActorType(Actor actor)
    {
        if (actor.WebUserId != null) return "web_user";
        if (actor.TelegramUserId != null) return "telegram_user";
        return "system";
    }

    private static string GetActorId(Actor actor)
    {
        if (actor.WebUserId != null) return actor.WebUserId;
        if (actor.TelegramUserId != null) return actor.TelegramUserId.Value.ToString();
        return actor.SystemIdentifier ?? "unknown";
    }
}

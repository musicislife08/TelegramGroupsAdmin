using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Checks if warning threshold is exceeded and signals auto-ban.
/// Order: 20 (runs after trust revocation, before training data)
/// </summary>
public class WarningThresholdHandler : IModerationHandler
{
    private readonly IConfigService _configService;
    private readonly ILogger<WarningThresholdHandler> _logger;

    public int Order => 20;

    public ModerationActionType[] AppliesTo => [ModerationActionType.Warn];

    public WarningThresholdHandler(
        IConfigService configService,
        ILogger<WarningThresholdHandler> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task<ModerationFollowUp> HandleAsync(ModerationEvent evt, CancellationToken ct = default)
    {
        // Get warning config (chat-specific or global)
        var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(
            ConfigType.Moderation,
            evt.ChatId) ?? WarningSystemConfig.Default;

        // Check if auto-ban is enabled and threshold reached
        if (!warningConfig.AutoBanEnabled || warningConfig.AutoBanThreshold <= 0)
        {
            _logger.LogDebug(
                "Auto-ban disabled for user {UserId} (enabled={Enabled}, threshold={Threshold})",
                evt.UserId, warningConfig.AutoBanEnabled, warningConfig.AutoBanThreshold);
            return ModerationFollowUp.None;
        }

        if (evt.WarningCount >= warningConfig.AutoBanThreshold)
        {
            _logger.LogWarning(
                "Warning threshold exceeded: User {UserId} has {WarnCount} warnings (threshold: {Threshold}). " +
                "Requesting auto-ban follow-up.",
                evt.UserId, evt.WarningCount, warningConfig.AutoBanThreshold);

            return ModerationFollowUp.Ban;
        }

        _logger.LogDebug(
            "User {UserId} warning count {WarnCount} below threshold {Threshold}",
            evt.UserId, evt.WarningCount, warningConfig.AutoBanThreshold);

        return ModerationFollowUp.None;
    }
}

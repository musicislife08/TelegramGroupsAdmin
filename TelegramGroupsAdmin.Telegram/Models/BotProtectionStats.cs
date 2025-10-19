namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Analytics data for bot protection system
/// </summary>
public record BotProtectionStats(
    int TotalBotsDetected,
    int BotsBanned,
    int BotsAllowed,
    int AdminInvitedBots,
    int WhitelistedBots
);

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// User context for the Messages page (permission level, linked accounts, bot features).
/// Included in page response instead of separate auth/me call.
/// </summary>
public record MessagesPageUserContext(
    int PermissionLevel,
    List<long> LinkedTelegramIds,
    bool CanSendAsBot,
    string? LinkedUsername,
    long? BotUserId,
    string? BotFeatureUnavailableReason
);

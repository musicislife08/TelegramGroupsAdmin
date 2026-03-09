namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

public record ConnectionStatus(
    bool IsConnected,
    string? DisplayName,
    long? TelegramUserId,
    DateTimeOffset? ConnectedAt);

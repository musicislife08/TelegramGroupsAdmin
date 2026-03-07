namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

public enum AuthStep
{
    Disconnected,
    CodeSent,
    Requires2FA,
    Connected,
    Failed
}

public record AuthFlowState(AuthStep Step, string? ErrorMessage = null);

public record ConnectionStatus(
    bool IsConnected,
    string? DisplayName,
    long? TelegramUserId,
    DateTimeOffset? ConnectedAt);

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

public enum AuthStep
{
    Disconnected,
    CodeSent,
    Requires2FA,
    Connected,
    Failed
}

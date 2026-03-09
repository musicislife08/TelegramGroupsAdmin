namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

public record AuthFlowState(AuthStep Step, string? ErrorMessage = null);

namespace TelegramGroupsAdmin.Ui.Server.Services;

public record RegisterResult(
    bool Success,
    string? UserId,
    string? ErrorMessage
);

namespace TelegramGroupsAdmin.Services;

public record RegisterResult(
    bool Success,
    string? UserId,
    string? ErrorMessage
);

namespace TelegramGroupsAdmin.Ui.Server.Services;

public record TotpVerificationResult(
    bool Success,
    bool SetupExpired,
    string? ErrorMessage,
    string? UserId = null,
    string? Email = null,
    int? PermissionLevel = null
);

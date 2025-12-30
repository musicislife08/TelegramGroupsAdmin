namespace TelegramGroupsAdmin.Ui.Server.Services;

public record AuthResult(
    bool Success,
    string? UserId,
    string? Email,
    int? PermissionLevel,
    string? SecurityStamp,
    bool TotpEnabled,
    bool RequiresTotp,
    string? ErrorMessage
);

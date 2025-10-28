namespace TelegramGroupsAdmin.Services;

public record AuthResult(
    bool Success,
    string? UserId,
    string? Email,
    int? PermissionLevel,
    bool TotpEnabled,
    bool RequiresTotp,
    string? ErrorMessage
);

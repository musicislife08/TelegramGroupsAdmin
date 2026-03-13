using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services;

public record AuthResult(
    bool Success,
    string? UserId,
    string? Email,
    PermissionLevel? PermissionLevel,
    bool TotpEnabled,
    bool RequiresTotp,
    string? ErrorMessage
);

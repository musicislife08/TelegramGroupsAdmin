using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Models;

public sealed record InviteValidationResult(bool IsValid, string? ErrorMessage, string? InvitedBy, PermissionLevel PermissionLevel);

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Lightweight identity for web (ASP.NET Identity) users.
/// Carries just what's needed for logging, display, and authorization checks.
/// Constructed at the boundary (MainLayout cascade, repo fetch), threaded through the call chain.
/// </summary>
public sealed record WebUserIdentity(string Id, string? Email, PermissionLevel PermissionLevel)
{
    public string DisplayName { get; } = !string.IsNullOrWhiteSpace(Email) ? Email : $"User {Id}";
    public bool IsOwner { get; } = PermissionLevel is PermissionLevel.Owner;
    public bool IsGlobalAdminOrHigher { get; } = PermissionLevel >= PermissionLevel.GlobalAdmin;

    /// <summary>
    /// Fallback factory for intermediate auth pages that only have a user ID (no email/permission from claims).
    /// Grants minimum permission (Admin = lowest level). Safe because callers only perform user-scoped operations.
    /// </summary>
    public static WebUserIdentity FromId(string id) => new(id, null, PermissionLevel.Admin);
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Helper service for accessing current user authentication state in Blazor components.
/// </summary>
public interface IBlazorAuthHelper
{
    /// <summary>Gets the current authenticated user's ID.</summary>
    Task<string?> GetCurrentUserIdAsync();

    /// <summary>Gets the current authenticated user's email address.</summary>
    Task<string?> GetCurrentUserEmailAsync();

    /// <summary>Gets the current user as an Actor for audit logging. Throws if not authenticated.</summary>
    Task<Actor> GetCurrentActorAsync();

    /// <summary>Attempts to get the current user as an Actor, returning null if not authenticated.</summary>
    Task<Actor?> TryGetCurrentActorAsync();

    /// <summary>Gets the current user's permission level.</summary>
    Task<PermissionLevel> GetCurrentPermissionLevelAsync();

    /// <summary>Checks if current user can edit infrastructure settings (Owner only).</summary>
    Task<bool> CanEditInfrastructureAsync();

    /// <summary>Checks if current user can edit content detection settings (GlobalAdmin or higher).</summary>
    Task<bool> CanEditContentSettingsAsync();

    /// <summary>Checks if current user can manage admin accounts (GlobalAdmin or higher).</summary>
    Task<bool> CanManageAdminAccountsAsync();

    /// <summary>Checks if current user is an Owner.</summary>
    Task<bool> IsOwnerAsync();

    /// <summary>Checks if current user is GlobalAdmin or higher.</summary>
    Task<bool> IsGlobalAdminOrHigherAsync();
}

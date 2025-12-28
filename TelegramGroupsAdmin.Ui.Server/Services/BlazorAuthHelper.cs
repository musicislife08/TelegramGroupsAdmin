using System.Security.Claims;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Ui.Server.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Helper service for extracting authenticated user information from HttpContext.
/// Provides consistent authentication context extraction across API endpoints.
/// </summary>
/// <remarks>
/// This is the API-optimized version that uses IHttpContextAccessor for direct
/// synchronous access to the current user's claims from HttpContext.
/// </remarks>
public class BlazorAuthHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BlazorAuthHelper(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <summary>
    /// Get the current authenticated user's ID (GUID string).
    /// Returns null if user is not authenticated or NameIdentifier claim is missing.
    /// </summary>
    public string? GetCurrentUserId()
    {
        return User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Get the current authenticated user's email.
    /// Returns null if user is not authenticated or Email claim is missing.
    /// </summary>
    public string? GetCurrentUserEmail()
    {
        return User?.FindFirst(ClaimTypes.Email)?.Value;
    }

    /// <summary>
    /// Get an Actor representing the current authenticated web user.
    /// Throws InvalidOperationException if user is not authenticated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when user is not authenticated or NameIdentifier claim is missing</exception>
    public Actor GetCurrentActor()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Unable to identify authenticated user for audit logging. User may not be logged in.");
        }

        var email = GetCurrentUserEmail();
        return Actor.FromWebUser(userId, email);
    }

    /// <summary>
    /// Try to get an Actor representing the current authenticated web user.
    /// Returns null if user is not authenticated instead of throwing.
    /// </summary>
    public Actor? TryGetCurrentActor()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var email = GetCurrentUserEmail();
        return Actor.FromWebUser(userId, email);
    }

    /// <summary>
    /// Get the current authenticated user's permission level from claims.
    /// Returns PermissionLevel.Admin (0) if not authenticated or PermissionLevel claim is missing/invalid.
    /// </summary>
    public PermissionLevel GetCurrentPermissionLevel()
    {
        var permissionClaim = User?.FindFirst(CustomClaimTypes.PermissionLevel);

        if (permissionClaim != null && int.TryParse(permissionClaim.Value, out var level))
        {
            return (PermissionLevel)level;
        }

        return PermissionLevel.Admin; // Default to lowest permission level
    }

    /// <summary>
    /// Check if current user can edit infrastructure settings (backups, API keys, logging, bot config).
    /// Only Owner can edit infrastructure.
    /// </summary>
    public bool CanEditInfrastructure()
    {
        var permissionLevel = GetCurrentPermissionLevel();
        return permissionLevel >= PermissionLevel.Owner;
    }

    /// <summary>
    /// Check if current user can edit content settings (spam detection, URL filters, training data).
    /// GlobalAdmin and Owner can edit content.
    /// </summary>
    public bool CanEditContentSettings()
    {
        var permissionLevel = GetCurrentPermissionLevel();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }

    /// <summary>
    /// Check if current user can manage admin accounts.
    /// GlobalAdmin and Owner can manage admin accounts.
    /// GlobalAdmin can only create Admin/GlobalAdmin users (escalation prevention enforced at API level).
    /// </summary>
    public bool CanManageAdminAccounts()
    {
        var permissionLevel = GetCurrentPermissionLevel();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }

    /// <summary>
    /// Check if current user is Owner.
    /// </summary>
    public bool IsOwner()
    {
        var permissionLevel = GetCurrentPermissionLevel();
        return permissionLevel >= PermissionLevel.Owner;
    }

    /// <summary>
    /// Check if current user is GlobalAdmin or higher.
    /// </summary>
    public bool IsGlobalAdminOrHigher()
    {
        var permissionLevel = GetCurrentPermissionLevel();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }
}

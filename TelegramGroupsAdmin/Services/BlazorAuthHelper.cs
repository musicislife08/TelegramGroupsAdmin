using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Auth;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Helper service for extracting authenticated user information in Blazor components.
/// Provides consistent authentication context extraction across UI layer.
/// </summary>
public class BlazorAuthHelper
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public BlazorAuthHelper(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    /// <summary>
    /// Get the current authenticated user's ID (GUID string).
    /// Returns null if user is not authenticated or NameIdentifier claim is missing.
    /// </summary>
    public async Task<string?> GetCurrentUserIdAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Get the current authenticated user's email.
    /// Returns null if user is not authenticated or Email claim is missing.
    /// </summary>
    public async Task<string?> GetCurrentUserEmailAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.FindFirst(ClaimTypes.Email)?.Value;
    }

    /// <summary>
    /// Get an Actor representing the current authenticated web user.
    /// Throws InvalidOperationException if user is not authenticated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when user is not authenticated or NameIdentifier claim is missing</exception>
    public async Task<Actor> GetCurrentActorAsync()
    {
        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Unable to identify authenticated user for audit logging. User may not be logged in.");
        }

        var email = await GetCurrentUserEmailAsync();
        return Actor.FromWebUser(userId, email);
    }

    /// <summary>
    /// Try to get an Actor representing the current authenticated web user.
    /// Returns null if user is not authenticated instead of throwing.
    /// </summary>
    public async Task<Actor?> TryGetCurrentActorAsync()
    {
        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var email = await GetCurrentUserEmailAsync();
        return Actor.FromWebUser(userId, email);
    }

    /// <summary>
    /// Get the current authenticated user's permission level from claims.
    /// Returns 0 if not authenticated or PermissionLevel claim is missing/invalid.
    /// </summary>
    public async Task<int> GetCurrentPermissionLevelAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var permissionClaim = authState.User.FindFirst(CustomClaimTypes.PermissionLevel);

        if (permissionClaim != null && int.TryParse(permissionClaim.Value, out var level))
        {
            return level;
        }

        return 0;
    }

    /// <summary>
    /// Check if current user can edit infrastructure settings (backups, API keys, logging, bot config).
    /// Only Owner (2) can edit infrastructure.
    /// </summary>
    public async Task<bool> CanEditInfrastructureAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= 2; // Owner only
    }

    /// <summary>
    /// Check if current user can edit content settings (spam detection, URL filters, training data).
    /// GlobalAdmin (1) and Owner (2) can edit content.
    /// </summary>
    public async Task<bool> CanEditContentSettingsAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= 1; // GlobalAdmin or Owner
    }

    /// <summary>
    /// Check if current user can manage admin accounts.
    /// Only Owner (2) can manage admin accounts.
    /// </summary>
    public async Task<bool> CanManageAdminAccountsAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= 2; // Owner only
    }

    /// <summary>
    /// Check if current user is Owner.
    /// </summary>
    public async Task<bool> IsOwnerAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= 2;
    }

    /// <summary>
    /// Check if current user is GlobalAdmin or higher.
    /// </summary>
    public async Task<bool> IsGlobalAdminOrHigherAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= 1;
    }
}

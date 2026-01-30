using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Auth;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Helper service for extracting authenticated user information in Blazor components.
/// Provides consistent authentication context extraction across UI layer.
/// </summary>
public class BlazorAuthHelper : IBlazorAuthHelper
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public BlazorAuthHelper(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    /// <inheritdoc/>
    public async Task<string?> GetCurrentUserIdAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <inheritdoc/>
    public async Task<string?> GetCurrentUserEmailAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.FindFirst(ClaimTypes.Email)?.Value;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<PermissionLevel> GetCurrentPermissionLevelAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var permissionClaim = authState.User.FindFirst(CustomClaimTypes.PermissionLevel);

        if (permissionClaim != null && int.TryParse(permissionClaim.Value, out var level))
        {
            return (PermissionLevel)level;
        }

        return PermissionLevel.Admin; // Default to lowest permission level
    }

    /// <inheritdoc/>
    public async Task<bool> CanEditInfrastructureAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= PermissionLevel.Owner;
    }

    /// <inheritdoc/>
    public async Task<bool> CanEditContentSettingsAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }

    /// <inheritdoc/>
    public async Task<bool> CanManageAdminAccountsAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }

    /// <inheritdoc/>
    public async Task<bool> IsOwnerAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= PermissionLevel.Owner;
    }

    /// <inheritdoc/>
    public async Task<bool> IsGlobalAdminOrHigherAsync()
    {
        var permissionLevel = await GetCurrentPermissionLevelAsync();
        return permissionLevel >= PermissionLevel.GlobalAdmin;
    }
}

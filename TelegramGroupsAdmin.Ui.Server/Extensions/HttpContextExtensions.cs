using System.Security.Claims;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Ui.Server.Auth;

namespace TelegramGroupsAdmin.Ui.Server.Extensions;

/// <summary>
/// Extension methods for extracting authentication information from HttpContext.
/// Centralizes claim parsing logic used across all API endpoints.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Gets the authenticated user's ID from the claims principal.
    /// </summary>
    public static string? GetUserId(this HttpContext context)
        => context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Gets the authenticated user's permission level from claims.
    /// Defaults to Admin if the claim is missing or invalid.
    /// </summary>
    public static PermissionLevel GetPermissionLevel(this HttpContext context)
    {
        var claim = context.User.FindFirst(CustomClaimTypes.PermissionLevel)?.Value;
        return claim != null && int.TryParse(claim, out var level)
            ? (PermissionLevel)level
            : PermissionLevel.Admin;
    }
}

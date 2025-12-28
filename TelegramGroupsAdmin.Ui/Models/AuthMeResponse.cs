namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response from GET /api/auth/me endpoint.
/// Lean response with just basic auth info for WasmAuthStateProvider.
/// Page-specific context is included in each page's aggregate response.
/// </summary>
public record AuthMeResponse(
    string UserId,
    string Email,
    int PermissionLevel,
    string? DisplayName = null
);

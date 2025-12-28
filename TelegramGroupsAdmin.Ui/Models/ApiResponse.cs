namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Base response for simple success/failure operations.
/// </summary>
public record ApiResponse(bool Success, string? Error = null);

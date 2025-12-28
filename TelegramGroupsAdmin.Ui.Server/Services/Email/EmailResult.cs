namespace TelegramGroupsAdmin.Ui.Server.Services.Email;

/// <summary>
/// Result of an email send operation
/// </summary>
public record EmailResult(
    bool Success,
    string? ErrorMessage = null
);

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to change the user's password.
/// </summary>
public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
);

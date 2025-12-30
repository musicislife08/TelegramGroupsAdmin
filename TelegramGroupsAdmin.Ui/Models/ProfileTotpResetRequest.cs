namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to reset TOTP from Profile page. Requires password verification.
/// </summary>
public record ProfileTotpResetRequest(string Password);

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to verify and enable TOTP from Profile page.
/// </summary>
public record ProfileTotpVerifyRequest(string Code);

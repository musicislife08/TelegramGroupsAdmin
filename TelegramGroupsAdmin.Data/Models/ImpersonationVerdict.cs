namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Admin verdict after reviewing an impersonation alert
/// </summary>
public enum ImpersonationVerdict
{
    /// <summary>Not actually impersonation, safe to ignore</summary>
    FalsePositive = 0,
    /// <summary>Confirmed scammer/impersonator, ban upheld</summary>
    ConfirmedScam = 1,
    /// <summary>Legitimate user, add to whitelist to prevent future alerts</summary>
    Whitelisted = 2
}

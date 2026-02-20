namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Admin callback button actions on a ProfileScanAlert report.
/// Values match the callback data action parameter.
/// </summary>
public enum ProfileScanAction
{
    /// <summary>Allow the user — clear profile gate</summary>
    Allow = 0,

    /// <summary>Ban the user globally</summary>
    Ban = 1,

    /// <summary>Kick the user from the triggering chat</summary>
    Kick = 2
}

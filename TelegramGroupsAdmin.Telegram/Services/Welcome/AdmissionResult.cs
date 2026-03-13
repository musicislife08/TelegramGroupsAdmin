namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Result of an admission check.
/// </summary>
public enum AdmissionResult
{
    /// <summary>All gates cleared — user was unmuted.</summary>
    Admitted,

    /// <summary>One or more gates still pending — user remains muted.</summary>
    StillWaiting
}

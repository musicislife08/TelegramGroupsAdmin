namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// What bot-written text shows for a user's name: the latest profile scan's name flags, applied only
/// while the user is banned. Platform-neutral.
/// </summary>
public enum NameVerdict
{
    /// <summary>No scan has judged this account's name.</summary>
    Unscanned = 0,
    /// <summary>
    /// Shown by real name: the latest scan did not flag the name, or it did but the user is not banned
    /// (a flagged name is masked only while the user is banned).
    /// </summary>
    Clean = 1,
    /// <summary>The name advertises, solicits or lures.</summary>
    Promotional = 2,
    /// <summary>The name contains explicit text.</summary>
    Explicit = 3
}

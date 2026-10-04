namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// What the latest profile scan concluded about a user's name. Platform-neutral.
/// </summary>
public enum NameVerdict
{
    /// <summary>No scan has judged this account's name.</summary>
    Unscanned = 0,
    Clean = 1,
    /// <summary>The name advertises, solicits or lures.</summary>
    Promotional = 2,
    /// <summary>The name contains explicit text.</summary>
    Explicit = 3
}

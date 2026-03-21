namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// How a username blacklist pattern is matched against display names.
/// Stored as int in database for forward compatibility.
/// </summary>
public enum BlacklistMatchType
{
    /// <summary>Case-insensitive exact match against UserIdentity.DisplayName</summary>
    Exact = 0
    // Future: Contains = 1, Regex = 2, Fuzzy = 3
}

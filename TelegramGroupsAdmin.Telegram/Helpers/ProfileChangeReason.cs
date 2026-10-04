using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Helpers;

/// <summary>
/// Builds the human-readable reason for a ProfileChange audit row.
/// </summary>
internal static class ProfileChangeReason
{
    /// <summary>
    /// Lists each changed field as "Field: old → new", comma separated, with "(none)" for a missing value.
    /// </summary>
    internal static string Build(PreviousNames old, ObservedUser current)
    {
        var changes = new List<string>(3);

        if (!string.Equals(old.Username, current.Username, StringComparison.Ordinal))
            changes.Add($"Username: @{old.Username ?? "(none)"} → @{current.Username ?? "(none)"}");

        if (!string.Equals(old.FirstName, current.FirstName, StringComparison.Ordinal))
            changes.Add($"First name: {old.FirstName ?? "(none)"} → {current.FirstName ?? "(none)"}");

        if (!string.Equals(old.LastName, current.LastName, StringComparison.Ordinal))
            changes.Add($"Last name: {old.LastName ?? "(none)"} → {current.LastName ?? "(none)"}");

        return string.Join(", ", changes);
    }
}

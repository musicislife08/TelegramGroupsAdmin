namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Compares backup format versions (metadata "version", e.g. "3.2") numerically, so "3.10" is newer than "3.2".
/// </summary>
internal static class BackupFormatVersion
{
    /// <summary>Whether <paramref name="backupVersion"/> is older than the migration gate <paramref name="gate"/>.</summary>
    /// <exception cref="InvalidOperationException">The backup's version is not a readable version number.</exception>
    public static bool IsOlderThan(string backupVersion, string gate) => Parse(backupVersion) < Version.Parse(gate);

    private static Version Parse(string backupVersion) =>
        Version.TryParse(backupVersion, out var version)
            ? version
            : throw new InvalidOperationException($"Unreadable backup format version '{backupVersion}'");
}

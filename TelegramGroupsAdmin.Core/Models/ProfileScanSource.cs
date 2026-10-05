namespace TelegramGroupsAdmin.Core.Models;

/// <summary>Which scan produced a profile scan row. Stored as smallint.</summary>
public enum ProfileScanSource
{
    /// <summary>The whole profile was read and scored.</summary>
    FullScan = 0,
    /// <summary>The profile could not be read; only the name and username were scored.</summary>
    NameOnly = 1
}

namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of UsernameBlacklistConfig for EF Core JSON column mapping.
/// Entries are managed in a separate database table; only the enabled flag is JSONB-stored.
/// </summary>
public class UsernameBlacklistConfigData
{
    public bool Enabled { get; set; } = true;
}

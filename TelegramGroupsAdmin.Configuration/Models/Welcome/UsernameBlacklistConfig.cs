namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for username blacklist check on join.
/// Enabled flag only — entries are managed in a separate database table.
/// </summary>
public class UsernameBlacklistConfig
{
    /// <summary>Whether the username blacklist check is enabled</summary>
    public bool Enabled { get; set; } = true;
}

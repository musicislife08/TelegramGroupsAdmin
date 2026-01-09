namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of InvisibleCharsConfig for EF Core JSON column mapping.
/// </summary>
public class InvisibleCharsConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool AlwaysRun { get; set; }
}

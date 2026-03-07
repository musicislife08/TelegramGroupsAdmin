namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BayesConfig for EF Core JSON column mapping.
/// </summary>
public class BayesConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool AlwaysRun { get; set; }
}

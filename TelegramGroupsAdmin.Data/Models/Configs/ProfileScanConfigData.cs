namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ProfileScanConfig for EF Core JSON column mapping.
/// </summary>
public class ProfileScanConfigData
{
    public bool Enabled { get; set; }

    public decimal BanThreshold { get; set; } = 4.0m;

    public decimal NotifyThreshold { get; set; } = 2.0m;

    public bool ScanOnJoin { get; set; } = true;

    public bool ScanOnProfileChange { get; set; } = true;

    public bool MaskExplicitUsername { get; set; } = true;

    public string ExplicitUsernameRedactionText { get; set; } = "[explicit username redacted]";
}

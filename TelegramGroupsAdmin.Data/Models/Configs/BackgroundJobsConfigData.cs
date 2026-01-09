namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BackgroundJobsConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class BackgroundJobsConfigData
{
    /// <summary>
    /// Dictionary of job configurations keyed by job name
    /// </summary>
    public Dictionary<string, BackgroundJobConfigData> Jobs { get; set; } = new();
}

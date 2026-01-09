namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of SendGridConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class SendGridConfigData
{
    /// <summary>
    /// Whether SendGrid service is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Email address to send from
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Display name for sender
    /// </summary>
    public string FromName { get; set; } = "TelegramGroupsAdmin";
}

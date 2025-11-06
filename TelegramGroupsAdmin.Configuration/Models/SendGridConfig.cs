namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// SendGrid email service configuration stored in configs.sendgrid_config JSONB column
/// API key is stored separately in configs.api_keys (encrypted)
/// </summary>
public class SendGridConfig
{
    /// <summary>
    /// Whether SendGrid service is enabled
    /// Set to false to disable all email sending (password reset, verification, notifications)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Email address to send from (must be verified in SendGrid)
    /// Example: noreply@example.com
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Display name for sender shown in email clients
    /// Example: "TelegramGroupsAdmin" or "My Community Bot"
    /// </summary>
    public string FromName { get; set; } = "TelegramGroupsAdmin";
}

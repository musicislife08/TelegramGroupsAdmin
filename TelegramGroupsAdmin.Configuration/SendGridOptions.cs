namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Configuration options for SendGrid email service
/// </summary>
public sealed class SendGridOptions
{
    /// <summary>
    /// Whether SendGrid service is enabled. Set to false to disable all email sending.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// SendGrid API key (starts with SG.)
    /// Get from: https://app.sendgrid.com/settings/api_keys
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Email address to send from (must be verified in SendGrid)
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Display name for sender
    /// </summary>
    public string FromName { get; set; } = "TelegramGroupsAdmin";
}

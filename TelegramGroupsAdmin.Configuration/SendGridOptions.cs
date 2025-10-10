namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Configuration options for SendGrid email service
/// </summary>
public sealed record SendGridOptions
{
    /// <summary>
    /// Whether SendGrid service is enabled. Set to false to disable all email sending.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// SendGrid API key (starts with SG.)
    /// Get from: https://app.sendgrid.com/settings/api_keys
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Email address to send from (must be verified in SendGrid)
    /// </summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>
    /// Display name for sender
    /// </summary>
    public string FromName { get; init; } = "TelegramGroupsAdmin";
}

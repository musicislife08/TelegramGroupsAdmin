namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Configuration options for email service
/// </summary>
public sealed class EmailOptions
{
    /// <summary>
    /// Whether email service is enabled. Set to false to disable all email sending.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// SMTP server hostname (e.g., smtp.office365.com)
    /// </summary>
    public string SmtpHost { get; set; } = "smtp.office365.com";

    /// <summary>
    /// SMTP server port (typically 587 for Office365)
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Enable SSL/TLS (should be true for Office365)
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// SMTP username (typically your Office365 email address)
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password or app-specific password
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Email address to send from
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Display name for sender
    /// </summary>
    public string FromName { get; set; } = "TelegramGroupsAdmin";
}

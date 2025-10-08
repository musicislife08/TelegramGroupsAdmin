namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Configuration options for email service
/// </summary>
public sealed record EmailOptions
{
    /// <summary>
    /// Whether email service is enabled. Set to false to disable all email sending.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// SMTP server hostname (e.g., smtp.office365.com)
    /// </summary>
    public string SmtpHost { get; init; } = "smtp.office365.com";

    /// <summary>
    /// SMTP server port (typically 587 for Office365)
    /// </summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>
    /// Enable SSL/TLS (should be true for Office365)
    /// </summary>
    public bool EnableSsl { get; init; } = true;

    /// <summary>
    /// SMTP username (typically your Office365 email address)
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// SMTP password or app-specific password
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Email address to send from
    /// </summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>
    /// Display name for sender
    /// </summary>
    public string FromName { get; init; } = "TelegramGroupsAdmin";
}

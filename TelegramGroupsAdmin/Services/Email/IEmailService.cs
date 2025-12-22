namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Service for sending emails. Implementations can use different providers (Office365, SendGrid, etc.)
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to a single recipient
    /// </summary>
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email to multiple recipients
    /// </summary>
    Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a templated email (for password reset, email verification, etc.)
    /// </summary>
    Task SendTemplatedEmailAsync(string to, EmailTemplate template, Dictionary<string, string> parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies SMTP connection is working
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

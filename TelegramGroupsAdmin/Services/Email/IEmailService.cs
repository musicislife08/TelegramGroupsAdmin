namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Service for sending emails. Implementations can use different providers (Office365, SendGrid, etc.)
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to a single recipient
    /// </summary>
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken ct = default);

    /// <summary>
    /// Sends an email to multiple recipients
    /// </summary>
    Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = true, CancellationToken ct = default);

    /// <summary>
    /// Sends a templated email (for password reset, email verification, etc.)
    /// </summary>
    Task SendTemplatedEmailAsync(string to, EmailTemplate template, Dictionary<string, string> parameters, CancellationToken ct = default);

    /// <summary>
    /// Verifies SMTP connection is working
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Predefined email template types
/// </summary>
public enum EmailTemplate
{
    /// <summary>Password reset email with secure token link</summary>
    PasswordReset,

    /// <summary>Email address verification for new accounts</summary>
    EmailVerification,

    /// <summary>Welcome email sent to newly registered users</summary>
    WelcomeEmail,

    /// <summary>Notification that an invite was created</summary>
    InviteCreated,

    /// <summary>Notification that account has been disabled by admin</summary>
    AccountDisabled
}

/// <summary>
/// Result of an email send operation
/// </summary>
public record EmailResult(
    bool Success,
    string? ErrorMessage = null
);

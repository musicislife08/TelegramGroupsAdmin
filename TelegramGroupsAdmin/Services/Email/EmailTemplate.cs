namespace TelegramGroupsAdmin.Services.Email;

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

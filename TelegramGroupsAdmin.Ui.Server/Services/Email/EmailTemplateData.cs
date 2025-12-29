namespace TelegramGroupsAdmin.Ui.Server.Services.Email;

/// <summary>
/// Type-safe email template data using discriminated union pattern.
/// Each nested record contains exactly the parameters needed for that template,
/// eliminating magic string keys and providing compile-time safety.
/// </summary>
public abstract record EmailTemplateData
{
    private EmailTemplateData() { }

    /// <summary>Password reset email with secure token link</summary>
    public sealed record PasswordReset(string ResetLink, int ExpiryMinutes) : EmailTemplateData;

    /// <summary>Email address verification for new accounts</summary>
    public sealed record EmailVerification(string VerificationToken, string BaseUrl) : EmailTemplateData;

    /// <summary>Welcome email sent to newly registered users</summary>
    public sealed record WelcomeEmail(string Email, string LoginUrl) : EmailTemplateData;

    /// <summary>Notification that an invite was created</summary>
    public sealed record InviteCreated(string InviteLink, string InvitedBy, int ExpiryDays) : EmailTemplateData;

    /// <summary>Notification that account has been disabled by admin</summary>
    public sealed record AccountDisabled(string Email, string Reason) : EmailTemplateData;

    /// <summary>Notification that account has been locked due to failed login attempts</summary>
    public sealed record AccountLocked(string Email, DateTimeOffset LockedUntil, int Attempts) : EmailTemplateData;

    /// <summary>Notification that account has been unlocked by an administrator</summary>
    public sealed record AccountUnlocked(string Email) : EmailTemplateData;
}

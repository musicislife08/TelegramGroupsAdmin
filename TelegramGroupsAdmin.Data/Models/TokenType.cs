namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Types of verification tokens for user authentication flows
/// </summary>
public enum TokenType
{
    /// <summary>Token for verifying email address during registration</summary>
    EmailVerification,
    /// <summary>Token for resetting forgotten password</summary>
    PasswordReset,
    /// <summary>Token for confirming new email address change</summary>
    EmailChange
}

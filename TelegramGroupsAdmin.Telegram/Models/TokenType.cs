namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Types of verification tokens for user authentication flows
/// </summary>
public enum TokenType
{
    /// <summary>Token for verifying email address during registration</summary>
    EmailVerification = 0,

    /// <summary>Token for resetting forgotten password</summary>
    PasswordReset = 1,

    /// <summary>Token for confirming new email address change</summary>
    EmailChange = 2
}

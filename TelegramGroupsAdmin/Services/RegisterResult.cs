namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Outcome of <see cref="IAuthService.RegisterAsync"/>.
/// </summary>
/// <param name="Success">Whether an account was created (or reactivated).</param>
/// <param name="UserId">The new account's id when <paramref name="Success"/> is true.</param>
/// <param name="ErrorMessage">A user-facing error when <paramref name="Success"/> is false.</param>
/// <param name="EmailVerificationRequired">
/// True when an invited account starts unverified and a verification email was issued, so the caller must tell
/// the user to verify before logging in. False otherwise: either the account starts verified (no email service
/// configured), or it is the first-run owner, which starts unverified when an email service is configured but
/// is sent no verification email (the owner recovers through "Resend verification email" on the login page).
/// </param>
/// <param name="VerificationEmailFailed">
/// True when <paramref name="EmailVerificationRequired"/> is true but sending the verification email failed, so
/// the caller must not claim a link was sent; the user can request one through "Resend verification email".
/// </param>
public record RegisterResult(
    bool Success,
    string? UserId,
    string? ErrorMessage,
    bool EmailVerificationRequired = false,
    bool VerificationEmailFailed = false
);

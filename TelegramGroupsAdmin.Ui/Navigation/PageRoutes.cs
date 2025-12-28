namespace TelegramGroupsAdmin.Ui.Navigation;

/// <summary>
/// Centralized page route definitions for navigation.
/// Eliminates hardcoded route strings throughout the application.
/// </summary>
public static class PageRoutes
{
    /// <summary>
    /// Authentication-related pages.
    /// </summary>
    public static class Auth
    {
        public const string Login = "/login";
        public const string Logout = "/logout";
        public const string Register = "/register";
        public const string ForgotPassword = "/forgot-password";
        public const string ResetPassword = "/reset-password";
        public const string ResendVerification = "/resend-verification";
        public const string UseRecoveryCode = "/use-recovery-code";
        public const string VerifyTotpPage = "/login/verify";
        public const string SetupTotpPage = "/login/setup-2fa";

        /// <summary>
        /// Login page with verification status message.
        /// </summary>
        public static string LoginWithVerified(string status) => $"{Login}?verified={status}";

        /// <summary>
        /// Login page with reset status message.
        /// </summary>
        public static string LoginWithReset(string status) => $"{Login}?reset={status}";

        /// <summary>
        /// TOTP verification page (step 2 of login for users with 2FA enabled).
        /// </summary>
        public static string VerifyTotp(string token) => $"{VerifyTotpPage}?token={Uri.EscapeDataString(token)}";

        /// <summary>
        /// TOTP setup page (for users who need to configure 2FA).
        /// </summary>
        public static string SetupTotp(string token) => $"{SetupTotpPage}?token={Uri.EscapeDataString(token)}";

        /// <summary>
        /// Recovery code page with intermediate token.
        /// </summary>
        public static string UseRecoveryCodeWithToken(string token) => $"{UseRecoveryCode}?token={Uri.EscapeDataString(token)}";

        /// <summary>
        /// Reset password page with token.
        /// </summary>
        public static string ResetPasswordWithToken(string token) => $"{ResetPassword}?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Main application pages.
    /// </summary>
    public static class App
    {
        public const string Home = "/";
        public const string Messages = "/messages";
        public const string Reports = "/reports";
        public const string Moderation = "/moderation";
        public const string Profile = "/profile";

        /// <summary>
        /// Messages page filtered to a specific chat.
        /// </summary>
        public static string MessagesForChat(long chatId) => $"{Messages}?chat={chatId}";

        /// <summary>
        /// Messages page with a highlighted message.
        /// </summary>
        public static string MessagesWithHighlight(long chatId, long messageId) =>
            $"{Messages}?chat={chatId}&highlight={messageId}";
    }

    /// <summary>
    /// Settings pages.
    /// </summary>
    public static class Settings
    {
        public const string Index = "/settings";
        public const string ThresholdRecommendations = "/settings/threshold-recommendations";
        public const string Backup = "/settings/backup";
    }
}

namespace TelegramGroupsAdmin.Ui.Api;

/// <summary>
/// Centralized API route definitions for the WASM client.
///
/// Route structure:
/// - /api/pages/* - Aggregate endpoints that return all data for a page in one call
/// - /api/{resource}/* - Focused action endpoints (CRUD, specific actions)
/// - /api/auth/* - Authentication endpoints
/// - /api/events/* - Server-Sent Events
/// </summary>
public static class Routes
{
    public static class Auth
    {
        public const string Base = "/api/auth";

        public const string Me = Base + "/me";
        public const string Login = Base + "/login";
        public const string Register = Base + "/register";
        public const string Logout = Base + "/logout";
        public const string VerifyTotp = Base + "/verify-totp";
        public const string VerifyRecoveryCode = Base + "/verify-recovery-code";
        public const string SetupTotp = Base + "/setup-totp";
        public const string VerifySetupTotp = Base + "/verify-setup-totp";
        public const string ForgotPassword = Base + "/forgot-password";
        public const string ResetPassword = Base + "/reset-password";
    }

    /// <summary>
    /// Aggregate page-oriented endpoints.
    /// Returns all data needed to render a page in a single HTTP call.
    /// </summary>
    public static class Pages
    {
        public const string Base = "/api/pages";

        /// <summary>
        /// Get all data for the Dashboard page: stats, detection stats, recent activity.
        /// </summary>
        public const string Dashboard = Base + "/dashboard";

        /// <summary>
        /// Get all data for the Messages page: chats sidebar, messages list, pagination.
        /// </summary>
        public static string Messages(long? chatId = null, int page = 1, int pageSize = 50)
        {
            var url = $"{Base}/messages?page={page}&pageSize={pageSize}";
            if (chatId.HasValue)
                url += $"&chatId={chatId}";
            return url;
        }

        /// <summary>
        /// Get older messages before a given timestamp (for infinite scroll).
        /// </summary>
        public static string MessagesBefore(long chatId, DateTimeOffset beforeTimestamp, int pageSize = 50)
        {
            return $"{Base}/messages?chatId={chatId}&pageSize={pageSize}&before={beforeTimestamp:o}";
        }

        /// <summary>
        /// Get all data for the message detail modal: message, user, detection history, edit history.
        /// </summary>
        public static string MessageDetail(long messageId) => $"{Base}/messages/{messageId}";

        /// <summary>
        /// Get initialization data for the Register page: first-run status, email verification config.
        /// </summary>
        public const string Register = Base + "/register";

        /// <summary>
        /// Get navigation tree for the Docs page sidebar.
        /// </summary>
        public const string DocsNav = Base + "/docs/nav";

        /// <summary>
        /// Get a documentation page by path (e.g., "algorithms/similarity").
        /// </summary>
        public static string DocsDocument(string path) => $"{DocsBase}/{path}";

        /// <summary>
        /// Get the first/default documentation page.
        /// </summary>
        public const string DocsDefault = Base + "/docs/";

        /// <summary>
        /// Get all data for the Profile page: user info, linked accounts, TOTP status.
        /// </summary>
        public const string Profile = Base + "/profile";

        /// <summary>
        /// Base path for Docs page endpoints.
        /// </summary>
        public const string DocsBase = Base + "/docs";

        /// <summary>
        /// Base path for Users page endpoints.
        /// </summary>
        public const string UsersBase = Base + "/users";
    }

    /// <summary>
    /// Focused action endpoints for message operations.
    /// </summary>
    public static class Messages
    {
        public const string Base = "/api/messages";

        /// <summary>
        /// Get list of accessible chats for the current user.
        /// </summary>
        public const string Chats = Base + "/chats";

        /// <summary>
        /// Delete a message.
        /// </summary>
        public static string Delete(long messageId) => $"{Base}/{messageId}/delete";

        /// <summary>
        /// Mark message as spam.
        /// </summary>
        public static string Spam(long messageId) => $"{Base}/{messageId}/spam";

        /// <summary>
        /// Mark message as ham (not spam).
        /// </summary>
        public static string Ham(long messageId) => $"{Base}/{messageId}/ham";

        /// <summary>
        /// Temporarily ban a user based on a message.
        /// </summary>
        public static string TempBan(long messageId) => $"{Base}/{messageId}/temp-ban";

        /// <summary>
        /// Send a new message as bot.
        /// </summary>
        public const string Send = Base + "/send";

        /// <summary>
        /// Edit an existing bot message.
        /// </summary>
        public static string Edit(long messageId) => $"{Base}/{messageId}/edit";

        /// <summary>
        /// Manually translate a message.
        /// </summary>
        public static string Translate(long messageId) => $"{Base}/{messageId}/translate";
    }

    /// <summary>
    /// User action endpoints (moderation, profile).
    /// </summary>
    public static class Users
    {
        public const string Base = "/api/users";

        /// <summary>
        /// Get user details.
        /// </summary>
        public static string Detail(long telegramUserId) => $"{Base}/{telegramUserId}";

        /// <summary>
        /// Permanently ban a user from all managed chats.
        /// </summary>
        public static string Ban(long telegramUserId) => $"{Base}/{telegramUserId}/ban";
    }

    public static class Events
    {
        public const string Base = "/api/events";

        public const string Stream = Base + "/stream";
    }

    public static class Backup
    {
        public const string Base = "/api/backup";

        public const string CheckEncrypted = Base + "/check-encrypted";
        public const string Metadata = Base + "/metadata";
        public const string Restore = Base + "/restore";
    }

    /// <summary>
    /// Profile action endpoints for password, TOTP, Telegram linking, and notifications.
    /// </summary>
    public static class Profile
    {
        public const string Base = "/api/profile";

        /// <summary>
        /// Change the user's password.
        /// </summary>
        public const string ChangePassword = Base + "/change-password";

        /// <summary>
        /// Initiate TOTP setup (returns otpauth:// URI for client-side QR generation).
        /// </summary>
        public const string TotpSetup = Base + "/totp/setup";

        /// <summary>
        /// Verify TOTP code and enable 2FA.
        /// </summary>
        public const string TotpVerify = Base + "/totp/verify";

        /// <summary>
        /// Reset TOTP (requires password verification).
        /// </summary>
        public const string TotpReset = Base + "/totp/reset";

        /// <summary>
        /// Generate a Telegram link token.
        /// </summary>
        public const string GenerateLinkToken = Base + "/telegram/generate-token";

        /// <summary>
        /// Unlink a Telegram account.
        /// </summary>
        public static string UnlinkTelegram(long mappingId) => $"{Base}/telegram/unlink/{mappingId}";

        /// <summary>
        /// Get notification preferences.
        /// </summary>
        public const string Notifications = Base + "/notifications";

        /// <summary>
        /// Get VAPID public key for WebPush subscription.
        /// </summary>
        public const string VapidKey = Base + "/webpush/vapid-key";

        /// <summary>
        /// Register a WebPush subscription.
        /// </summary>
        public const string PushSubscribe = Base + "/webpush/subscribe";

        /// <summary>
        /// Unregister a WebPush subscription.
        /// </summary>
        public const string PushUnsubscribe = Base + "/webpush/unsubscribe";
    }
}

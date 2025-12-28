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
        private const string Base = "/api/auth";

        public static string Me => $"{Base}/me";
        public static string FirstRun => $"{Base}/first-run";
        public static string Login => $"{Base}/login";
        public static string Register => $"{Base}/register";
        public static string Logout => $"{Base}/logout";
        public static string VerifyTotp => $"{Base}/verify-totp";
        public static string VerifyRecoveryCode => $"{Base}/verify-recovery-code";
        public static string SetupTotp => $"{Base}/setup-totp";
        public static string VerifySetupTotp => $"{Base}/verify-setup-totp";
        public static string ForgotPassword => $"{Base}/forgot-password";
        public static string ResetPassword => $"{Base}/reset-password";
    }

    /// <summary>
    /// Aggregate page-oriented endpoints.
    /// Returns all data needed to render a page in a single HTTP call.
    /// </summary>
    public static class Pages
    {
        private const string Base = "/api/pages";

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
    }

    /// <summary>
    /// Focused action endpoints for message operations.
    /// </summary>
    public static class Messages
    {
        private const string Base = "/api/messages";

        /// <summary>
        /// Get list of accessible chats for the current user.
        /// </summary>
        public static string Chats => $"{Base}/chats";

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
        public static string Send => $"{Base}/send";

        /// <summary>
        /// Edit an existing bot message.
        /// </summary>
        public static string Edit(long messageId) => $"{Base}/{messageId}/edit";
    }

    public static class Events
    {
        private const string Base = "/api/events";

        public static string Stream => $"{Base}/stream";
    }

    public static class Backup
    {
        private const string Base = "/api/backup";

        public static string CheckEncrypted => $"{Base}/check-encrypted";
        public static string Metadata => $"{Base}/metadata";
        public static string Restore => $"{Base}/restore";
    }
}

using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Orchestrates moderation actions across Telegram and database.
/// Handles bans, warnings, trust status, and message deletion with audit logging.
/// The "boss" that knows all workers, decides who to call, and owns business rules.
/// Uses Bot handlers for Telegram API calls (IBotBanHandler, IBotRestrictHandler, etc.).
/// </summary>
public interface IBotModerationService
{
    /// <summary>
    /// Mark message as spam, delete it, ban user globally, and revoke trust.
    /// Composes: EnsureExists → Delete → Ban → Training Data
    /// </summary>
    Task<ModerationResult> MarkAsSpamAndBanAsync(SpamBanIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Ban user globally across all managed chats.
    /// Business rule: Bans always revoke trust.
    /// </summary>
    Task<ModerationResult> BanUserAsync(BanIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Warn user globally with automatic ban after threshold.
    /// Business rule: N warnings = auto-ban (configurable per chat).
    /// </summary>
    Task<ModerationResult> WarnUserAsync(WarnIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// </summary>
    Task<ModerationResult> TrustUserAsync(TrustIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Remove trust from user globally.
    /// </summary>
    Task<ModerationResult> UntrustUserAsync(UntrustIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Unban user globally and optionally restore trust.
    /// </summary>
    Task<ModerationResult> UnbanUserAsync(UnbanIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database.
    /// </summary>
    Task<ModerationResult> DeleteMessageAsync(DeleteMessageIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Temporarily ban user globally with automatic unrestriction.
    /// </summary>
    Task<ModerationResult> TempBanUserAsync(TempBanIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Restrict user (mute) globally or in a specific chat with automatic unrestriction.
    /// </summary>
    Task<ModerationResult> RestrictUserAsync(RestrictIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Sync an existing global ban to a specific chat (lazy sync for chats added after ban).
    /// Also used by BotProtectionService to ban unauthorized bots in a single chat.
    /// </summary>
    Task<ModerationResult> SyncBanToChatAsync(SyncBanIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Restore user permissions to the chat's default permissions.
    /// Used when approving users through welcome/exam flows.
    /// </summary>
    Task<ModerationResult> RestoreUserPermissionsAsync(RestorePermissionsIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Kick user from a specific chat (ban then immediately unban).
    /// Does not affect other chats or create permanent ban record.
    /// </summary>
    Task<ModerationResult> KickUserFromChatAsync(KickIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Handle malware detection: delete message, create admin report, notify admins.
    /// Does NOT auto-ban (malware upload may be accidental).
    /// </summary>
    Task<ModerationResult> HandleMalwareViolationAsync(MalwareViolationIntent intent, CancellationToken ct = default);

    /// <summary>
    /// Handle critical check violation by trusted/admin user: delete message, notify user.
    /// Does NOT ban/warn (trusted users get a pass, but message still removed).
    /// </summary>
    Task<ModerationResult> HandleCriticalViolationAsync(CriticalViolationIntent intent, CancellationToken ct = default);
}

using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Represents who performed an action (Phase 4.19 - Exclusive Arc Actor System)
/// Replaces legacy string-based issued_by/added_by fields
/// </summary>
public record Actor
{
    /// <summary>
    /// Type of actor
    /// </summary>
    public required ActorType Type { get; init; }

    /// <summary>
    /// Web user ID (when Type = WebUser)
    /// </summary>
    public string? WebUserId { get; init; }

    /// <summary>
    /// Telegram user ID (when Type = TelegramUser)
    /// </summary>
    public long? TelegramUserId { get; init; }

    /// <summary>
    /// System identifier (when Type = System)
    /// Examples: "auto_detection", "bot_protection", "initial_seed"
    /// </summary>
    public string? SystemIdentifier { get; init; }

    /// <summary>
    /// Human-readable display name (resolved from JOINs or system identifier)
    /// </summary>
    public string? DisplayName { get; init; }

    // ============================================================================
    // Factory Methods
    // ============================================================================

    // Common system actors (eliminates magic strings)
    public static readonly Actor AutoDetection = FromSystem("auto_detection");
    public static readonly Actor BotProtection = FromSystem("bot_protection");
    public static readonly Actor FileScanner = FromSystem("file_scanner");
    public static readonly Actor AutoTrust = FromSystem("auto_trust");
    public static readonly Actor Impersonation = FromSystem("impersonation");
    public static readonly Actor AutoBan = FromSystem("auto_ban");
    public static readonly Actor Cas = FromSystem("cas"); // CAS (Combot Anti-Spam) user join check
    public static readonly Actor LanguageWarning = FromSystem("language_warning"); // Phase 4.21
    public static readonly Actor SystemSeed = FromSystem("system_seed"); // Initial data seeding
    public static readonly Actor ExamFlow = FromSystem("exam_flow"); // Entrance exam auto-pass
    public static readonly Actor WelcomeFlow = FromSystem("welcome_flow"); // Welcome/rules flow completion
    public static readonly Actor TempbanExpiry = FromSystem("tempban_expiry"); // Scheduled tempban expiry
    public static readonly Actor Unknown = FromSystem("unknown");

    /// <summary>
    /// Create actor from web user
    /// </summary>
    public static Actor FromWebUser(string userId, string? email = null)
    {
        return new Actor
        {
            Type = ActorType.WebUser,
            WebUserId = userId,
            DisplayName = email ?? $"User {userId[..Math.Min(8, userId.Length)]}"
        };
    }

    /// <summary>
    /// Create actor from a UserIdentity (carries full name info)
    /// </summary>
    public static Actor FromUserIdentity(UserIdentity user)
        => FromTelegramUser(user.Id, user.Username, user.FirstName, user.LastName);

    /// <summary>
    /// Create actor from Telegram user
    /// </summary>
    public static Actor FromTelegramUser(long telegramUserId, string? username = null, string? firstName = null, string? lastName = null)
    {
        return new Actor
        {
            Type = ActorType.TelegramUser,
            TelegramUserId = telegramUserId,
            DisplayName = TelegramDisplayName.Format(firstName, lastName, username, telegramUserId)
        };
    }

    /// <summary>
    /// Create actor from system identifier
    /// </summary>
    public static Actor FromSystem(string systemIdentifier)
    {
        var displayName = systemIdentifier switch
        {
            "auto_detection" => "Auto-Detection",
            "bot_protection" => "Bot Protection",
            "file_scanner" => "File Scanner",
            "auto_trust" => "Auto-Trust",
            "impersonation" => "Impersonation Detection",
            "auto_ban" => "Auto-Ban",
            "cas" => "CAS Anti-Spam",
            "language_warning" => "Language Warning",
            "system_seed" => "System Seed",
            "initial_seed" => "Initial Seed",
            "web_admin" => "Web Admin (Legacy)",
            "exam_flow" => "Exam Flow",
            "welcome_flow" => "Welcome Flow",
            "tempban_expiry" => "Tempban Expiry",
            "unknown" => "Unknown",
            _ => systemIdentifier
        };

        return new Actor
        {
            Type = ActorType.System,
            SystemIdentifier = systemIdentifier,
            DisplayName = displayName
        };
    }

    // ============================================================================
    // Conversion to Database Columns
    // ============================================================================

    /// <summary>
    /// Get web_user_id column value for database
    /// </summary>
    public string? GetWebUserId() => Type == ActorType.WebUser ? WebUserId : null;

    /// <summary>
    /// Get telegram_user_id column value for database
    /// </summary>
    public long? GetTelegramUserId() => Type == ActorType.TelegramUser ? TelegramUserId : null;

    /// <summary>
    /// Get system_identifier column value for database
    /// </summary>
    public string? GetSystemIdentifier() => Type == ActorType.System ? SystemIdentifier : null;

    // ============================================================================
    // Display Helpers
    // ============================================================================

    /// <summary>
    /// Get short display text for UI (e.g., "@username", "Auto-Detection")
    /// </summary>
    public string GetDisplayText() => DisplayName ?? "Unknown";

    /// <summary>
    /// Get detailed description for audit logs
    /// </summary>
    public string GetDetailedDescription()
    {
        return Type switch
        {
            ActorType.WebUser => $"Web User: {DisplayName} ({WebUserId})",
            ActorType.TelegramUser => $"Telegram User: {DisplayName} (ID: {TelegramUserId})",
            ActorType.System => $"System: {DisplayName}",
            _ => "Unknown Actor"
        };
    }

    /// <summary>
    /// Override ToString for debugging
    /// </summary>
    public override string ToString() => GetDetailedDescription();
}

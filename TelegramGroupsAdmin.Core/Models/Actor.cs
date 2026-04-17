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
    public static readonly Actor AutoDetection = FromSystem(SystemActorIds.AutoDetection);
    public static readonly Actor BotProtection = FromSystem(SystemActorIds.BotProtection);
    public static readonly Actor FileScanner = FromSystem(SystemActorIds.FileScanner);
    public static readonly Actor AutoTrust = FromSystem(SystemActorIds.AutoTrust);
    public static readonly Actor Impersonation = FromSystem(SystemActorIds.Impersonation);
    public static readonly Actor AutoBan = FromSystem(SystemActorIds.AutoBan);
    public static readonly Actor Cas = FromSystem(SystemActorIds.Cas);
    public static readonly Actor LanguageWarning = FromSystem(SystemActorIds.LanguageWarning);
    public static readonly Actor SystemSeed = FromSystem(SystemActorIds.SystemSeed);
    public static readonly Actor ExamFlow = FromSystem(SystemActorIds.ExamFlow);
    public static readonly Actor WelcomeFlow = FromSystem(SystemActorIds.WelcomeFlow);
    public static readonly Actor TempbanExpiry = FromSystem(SystemActorIds.TempbanExpiry);
    public static readonly Actor Unknown = FromSystem(SystemActorIds.Unknown);
    public static readonly Actor ProfileScan = FromSystem(SystemActorIds.ProfileScan);
    public static readonly Actor UsernameBlacklist = FromSystem(SystemActorIds.UsernameBlacklist);
    public static readonly Actor Bootstrap = FromSystem(SystemActorIds.Bootstrap);
    public static readonly Actor ProfileDiffDetection = FromSystem(SystemActorIds.ProfileDiffDetection);
    public static readonly Actor WelcomeBypass = FromSystem(SystemActorIds.WelcomeBypass);

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
            SystemActorIds.AutoDetection => "Auto-Detection",
            SystemActorIds.BotProtection => "Bot Protection",
            SystemActorIds.FileScanner => "File Scanner",
            SystemActorIds.AutoTrust => "Auto-Trust",
            SystemActorIds.Impersonation => "Impersonation Detection",
            SystemActorIds.AutoBan => "Auto-Ban",
            SystemActorIds.Cas => "CAS Anti-Spam",
            SystemActorIds.LanguageWarning => "Language Warning",
            SystemActorIds.SystemSeed => "System Seed",
            SystemActorIds.InitialSeed => "Initial Seed",
            SystemActorIds.WebAdmin => "Web Admin (Legacy)",
            SystemActorIds.ExamFlow => "Exam Flow",
            SystemActorIds.WelcomeFlow => "Welcome Flow",
            SystemActorIds.TempbanExpiry => "Tempban Expiry",
            SystemActorIds.Unknown => "Unknown",
            SystemActorIds.ProfileScan => "Profile Scan",
            SystemActorIds.UsernameBlacklist => "Username Blacklist",
            SystemActorIds.Bootstrap => "CLI Bootstrap",
            SystemActorIds.ProfileDiffDetection => "Profile Change Detection",
            SystemActorIds.WelcomeBypass => "Welcome Bypass",
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

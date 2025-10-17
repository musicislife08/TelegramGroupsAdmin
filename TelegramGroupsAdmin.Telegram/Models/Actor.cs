namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Actor type for action attribution (Phase 4.19)
/// </summary>
public enum ActorType
{
    /// <summary>Web UI user (authenticated admin)</summary>
    WebUser = 0,

    /// <summary>Telegram user (via bot commands)</summary>
    TelegramUser = 1,

    /// <summary>System/automated action</summary>
    System = 2
}

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

    /// <summary>
    /// Create actor from web user
    /// </summary>
    public static Actor FromWebUser(string userId, string? email = null)
    {
        return new Actor
        {
            Type = ActorType.WebUser,
            WebUserId = userId,
            DisplayName = email ?? $"User {userId[..8]}"
        };
    }

    /// <summary>
    /// Create actor from Telegram user
    /// </summary>
    public static Actor FromTelegramUser(long telegramUserId, string? username = null, string? firstName = null)
    {
        var displayName = username != null ? $"@{username}" :
                         firstName ?? $"User {telegramUserId}";

        return new Actor
        {
            Type = ActorType.TelegramUser,
            TelegramUserId = telegramUserId,
            DisplayName = displayName
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
            "initial_seed" => "Initial Seed",
            "web_admin" => "Web Admin (Legacy)",
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

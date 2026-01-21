namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Enumeration of configuration types stored in the unified configs table.
/// Provides type safety and IntelliSense support when working with configurations.
/// </summary>
public enum ConfigType
{
    /// <summary>
    /// Content detection configuration (content_detection_configs table)
    /// Contains: ContentDetectionConfig model
    /// Stored in separate table, routed via IContentDetectionConfigRepository
    /// </summary>
    ContentDetection,

    /// <summary>
    /// Welcome message configuration (welcome_config column)
    /// Contains: WelcomeConfig model
    /// Phase 4.4: Welcome system
    /// </summary>
    Welcome,

    /// <summary>
    /// Log level configuration (log_config column)
    /// Contains: LogConfig model
    /// Phase 4.7: Dynamic log levels
    /// </summary>
    Log,

    /// <summary>
    /// Moderation configuration including warning system (moderation_config column)
    /// Contains: WarningSystemConfig model
    /// Phase 4.11: Warning/Points System
    /// </summary>
    Moderation,

    /// <summary>
    /// URL filtering configuration (url_filter_config column)
    /// Contains: UrlFilterConfig model
    /// Phase 4.13: URL Filtering System
    /// </summary>
    UrlFilter,

    /// <summary>
    /// Telegram bot service configuration (telegram_bot_config column)
    /// Contains: TelegramBotConfig model
    /// Controls whether the bot polling service is active
    /// GLOBAL ONLY - not available for per-chat configuration
    /// </summary>
    TelegramBot,

    /// <summary>
    /// Service message deletion configuration (service_message_deletion_config column)
    /// Contains: ServiceMessageDeletionConfig model
    /// Controls which service messages (join/leave, photo changes, etc.) are auto-deleted
    /// Supports per-chat overrides
    /// </summary>
    ServiceMessageDeletion,

    /// <summary>
    /// Ban celebration configuration (ban_celebration_config column)
    /// Contains: BanCelebrationConfig model
    /// Controls whether celebratory GIFs are posted when users are banned
    /// Supports per-chat overrides
    /// </summary>
    BanCelebration
}

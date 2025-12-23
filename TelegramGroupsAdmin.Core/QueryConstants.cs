namespace TelegramGroupsAdmin.Core;

/// <summary>
/// Centralized constants for database query limits and pagination.
/// </summary>
public static class QueryConstants
{
    /// <summary>
    /// Default limit for audit log queries.
    /// Used when fetching recent events, user events, or events by type.
    /// </summary>
    public const int DefaultAuditLogLimit = 100;

    /// <summary>
    /// Default limit for web notification queries.
    /// Used when fetching recent notifications for display.
    /// </summary>
    public const int DefaultWebNotificationLimit = 20;

    /// <summary>
    /// Default maximum length for truncated log output in SemanticKernelChatService.
    /// Used when logging response items to prevent excessive log verbosity.
    /// </summary>
    public const int DefaultLogTruncationLength = 200;

    /// <summary>
    /// Maximum length for metadata value truncation in SemanticKernelChatService.
    /// Used when logging SK response metadata to prevent log overflow.
    /// </summary>
    public const int MaxMetadataLogLength = 500;
}

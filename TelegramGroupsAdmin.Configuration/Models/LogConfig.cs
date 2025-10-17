using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration model for dynamic log levels
/// Stored in configs table with ConfigType.Log
/// </summary>
public class LogConfig
{
    /// <summary>
    /// Default log level for all namespaces (if not specified in Overrides)
    /// </summary>
    public LogLevel DefaultLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Per-namespace log level overrides
    /// Key: namespace (e.g., "TelegramGroupsAdmin.Telegram")
    /// Value: log level to apply
    /// </summary>
    public Dictionary<string, LogLevel> Overrides { get; set; } = new();

    /// <summary>
    /// Timestamp of last configuration change
    /// </summary>
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}

namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of LogConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// Note: LogLevel enum stored as int (0=Trace, 1=Debug, 2=Info, etc.)
/// </summary>
public class LogConfigData
{
    /// <summary>
    /// Default log level for all namespaces (stored as int, maps to LogLevel enum)
    /// </summary>
    public int DefaultLevel { get; set; } = 2; // Information

    /// <summary>
    /// Per-namespace log level overrides (stored as int, maps to LogLevel enum)
    /// </summary>
    public Dictionary<string, int> Overrides { get; set; } = new();

    /// <summary>
    /// Timestamp of last configuration change
    /// </summary>
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}

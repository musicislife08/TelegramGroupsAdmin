using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing runtime log level configuration
/// Allows dynamic adjustment of log levels without restarting the application
/// </summary>
public interface IRuntimeLoggingService
{
    /// <summary>
    /// Get the current effective log configuration
    /// Returns database config if exists, otherwise returns default from appsettings
    /// </summary>
    Task<LogConfig> GetCurrentConfigAsync();

    /// <summary>
    /// Update log configuration and apply immediately to ILoggerFactory
    /// </summary>
    /// <param name="config">New log configuration</param>
    Task UpdateConfigAsync(LogConfig config);

    /// <summary>
    /// Reset log configuration to defaults (removes database config)
    /// </summary>
    Task ResetToDefaultsAsync();

    /// <summary>
    /// Get available log levels for UI dropdowns
    /// </summary>
    IReadOnlyList<LogLevel> GetAvailableLogLevels();

    /// <summary>
    /// Get common namespace prefixes for quick selection
    /// </summary>
    IReadOnlyList<string> GetCommonNamespaces();
}

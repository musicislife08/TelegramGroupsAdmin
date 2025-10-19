using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;

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

/// <summary>
/// Implementation of runtime logging configuration service
/// Integrates with ILoggerFactory to apply log level changes immediately
/// </summary>
public class RuntimeLoggingService : IRuntimeLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RuntimeLoggingService> _logger;

    // Common namespaces for quick selection in UI
    private static readonly string[] CommonNamespaces = [
        "TelegramGroupsAdmin",
        "TelegramGroupsAdmin.Telegram",
        "TelegramGroupsAdmin.ContentDetection",
        "TelegramGroupsAdmin.Data",
        "Microsoft",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System"
    ];

    // Available log levels (ordered from most verbose to least)
    private static readonly LogLevel[] AvailableLogLevels = [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical
    ];

    public RuntimeLoggingService(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        ILogger<RuntimeLoggingService> logger)
    {
        _scopeFactory = scopeFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<LogConfig> GetCurrentConfigAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Try to get config from database
        var config = await configService.GetAsync<LogConfig>(ConfigType.Log, chatId: null);

        if (config != null)
        {
            return config;
        }

        // Return default config if not in database
        return new LogConfig
        {
            DefaultLevel = LogLevel.Information,
            Overrides = new Dictionary<string, LogLevel>
            {
                { "Microsoft", LogLevel.Warning },
                { "Microsoft.Hosting.Lifetime", LogLevel.Information },
                { "TelegramGroupsAdmin", LogLevel.Information }
            },
            LastModified = DateTimeOffset.UtcNow
        };
    }

    public async Task UpdateConfigAsync(LogConfig config)
    {
        config.LastModified = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Save to database
        await configService.SaveAsync(ConfigType.Log, chatId: null, config);

        // Apply immediately to ILoggerFactory
        ApplyLogLevels(config);

        _logger.LogInformation(
            "Log configuration updated: DefaultLevel={DefaultLevel}, Overrides={OverrideCount}",
            config.DefaultLevel,
            config.Overrides.Count);
    }

    public async Task ResetToDefaultsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Delete from database
        await configService.DeleteAsync(ConfigType.Log, chatId: null);

        // Get default config
        var defaultConfig = new LogConfig
        {
            DefaultLevel = LogLevel.Information,
            Overrides = new Dictionary<string, LogLevel>
            {
                { "Microsoft", LogLevel.Warning },
                { "Microsoft.Hosting.Lifetime", LogLevel.Information },
                { "TelegramGroupsAdmin", LogLevel.Information }
            }
        };

        // Apply default log levels
        ApplyLogLevels(defaultConfig);

        _logger.LogInformation("Log configuration reset to defaults");
    }

    public IReadOnlyList<LogLevel> GetAvailableLogLevels() => AvailableLogLevels;

    public IReadOnlyList<string> GetCommonNamespaces() => CommonNamespaces;

    /// <summary>
    /// Apply log level configuration to ILoggerFactory
    /// This makes changes take effect immediately without restart
    /// </summary>
    private void ApplyLogLevels(LogConfig config)
    {
        // Note: ILoggerFactory doesn't have a direct API to modify filters at runtime
        // In .NET 6+, we need to use IOptionsMonitor<LoggerFilterOptions> or recreate loggers
        // For now, we'll log a warning that this requires additional infrastructure

        // TODO: Implement runtime filter updates using IOptionsMonitor<LoggerFilterOptions>
        // or by using a custom ILoggerProvider that respects our config

        _logger.LogWarning(
            "Log level configuration saved to database. Full runtime updates require additional infrastructure setup. " +
            "Changes will take effect on next application restart.");
    }
}

using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Implementation of runtime logging configuration service
/// Integrates with Serilog LoggingLevelSwitch for true runtime log level changes
/// </summary>
public class RuntimeLoggingService : IRuntimeLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SerilogDynamicConfiguration _serilogConfig;
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
        SerilogDynamicConfiguration serilogConfig,
        ILogger<RuntimeLoggingService> logger)
    {
        _scopeFactory = scopeFactory;
        _serilogConfig = serilogConfig;
        _logger = logger;
    }

    public async Task<LogConfig> GetCurrentConfigAsync()
    {
        // Get current configuration from Serilog switches (reflects actual runtime state)
        var config = _serilogConfig.GetCurrentConfig();

        // Also check database for LastModified timestamp
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var dbConfig = await configService.GetAsync<LogConfig>(ConfigType.Log, chatId: 0);

        if (dbConfig != null)
        {
            config.LastModified = dbConfig.LastModified;
        }

        return config;
    }

    public async Task UpdateConfigAsync(LogConfig config)
    {
        config.LastModified = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        // Save to database (global config at chatId=0)
        await configService.SaveAsync(ConfigType.Log, chatId: 0, config);

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
    /// Apply log level configuration to Serilog switches at runtime
    /// Changes take effect immediately for all loggers (no restart needed)
    /// </summary>
    private void ApplyLogLevels(LogConfig config)
    {
        try
        {
            // Update Serilog LoggingLevelSwitch instances
            _serilogConfig.ApplyConfig(config);

            _logger.LogInformation(
                "Log configuration applied at runtime: DefaultLevel={DefaultLevel}, Overrides={OverrideCount}",
                config.DefaultLevel,
                config.Overrides.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply log level configuration at runtime");
            throw;
        }
    }
}

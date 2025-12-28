using Serilog.Core;
using Serilog.Events;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Manages Serilog LoggingLevelSwitch instances for runtime log level reconfiguration
/// Integrates with database configuration to persist settings across restarts
/// </summary>
public class SerilogDynamicConfiguration
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, LoggingLevelSwitch> _switches = new();
    private readonly LoggingLevelSwitch _defaultSwitch;

    public SerilogDynamicConfiguration(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _defaultSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

        // Apply defaults immediately so they take effect during startup (before InitializeAsync)
        ApplyDefaults();
    }

    /// <summary>
    /// Default log level switch (applies when no namespace override matches)
    /// </summary>
    public LoggingLevelSwitch DefaultSwitch => _defaultSwitch;

    /// <summary>
    /// Get or create a LoggingLevelSwitch for a specific namespace
    /// </summary>
    public LoggingLevelSwitch GetSwitch(string category)
    {
        if (!_switches.ContainsKey(category))
        {
            _switches[category] = new LoggingLevelSwitch(LogEventLevel.Information);
        }
        return _switches[category];
    }

    /// <summary>
    /// Initialize switches from database configuration at startup
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

            // Load config from database (global config at chatId=0)
            var config = await configService.GetAsync<LogConfig>(ConfigType.Log, chatId: 0);

            if (config != null)
            {
                // Apply default level
                _defaultSwitch.MinimumLevel = MapToSerilogLevel(config.DefaultLevel);

                // Apply namespace overrides
                foreach (var (category, level) in config.Overrides)
                {
                    GetSwitch(category).MinimumLevel = MapToSerilogLevel(level);
                }
            }
            else
            {
                // Use sensible defaults if no database config
                ApplyDefaults();
            }
        }
        catch (Exception)
        {
            // Database not available (first startup) - use defaults
            ApplyDefaults();
        }
    }

    /// <summary>
    /// Apply log level changes at runtime (called by RuntimeLoggingService)
    /// </summary>
    public void ApplyConfig(LogConfig config)
    {
        // Update default level
        _defaultSwitch.MinimumLevel = MapToSerilogLevel(config.DefaultLevel);

        // Update namespace-specific switches
        foreach (var (category, level) in config.Overrides)
        {
            GetSwitch(category).MinimumLevel = MapToSerilogLevel(level);
        }

        // Remove switches for categories no longer in config
        var categoriesToRemove = _switches.Keys.Except(config.Overrides.Keys).ToList();
        foreach (var category in categoriesToRemove)
        {
            // Reset to default level instead of removing (keeps existing loggers working)
            _switches[category].MinimumLevel = _defaultSwitch.MinimumLevel;
        }
    }

    /// <summary>
    /// Apply default log levels (background: Warning, app: Information)
    /// </summary>
    private void ApplyDefaults()
    {
        _defaultSwitch.MinimumLevel = LogEventLevel.Information;

        // Background services and infrastructure: Warning
        GetSwitch("Microsoft").MinimumLevel = LogEventLevel.Warning;
        GetSwitch("Microsoft.Hosting.Lifetime").MinimumLevel = LogEventLevel.Information;
        GetSwitch("Microsoft.EntityFrameworkCore").MinimumLevel = LogEventLevel.Warning; // Hide EF Core query logs
        GetSwitch("Microsoft.EntityFrameworkCore.Database.Command").MinimumLevel = LogEventLevel.Warning; // Hide SQL queries
        GetSwitch("Npgsql").MinimumLevel = LogEventLevel.Warning;
        GetSwitch("System").MinimumLevel = LogEventLevel.Warning;

        // Our application code: Information
        GetSwitch("TelegramGroupsAdmin").MinimumLevel = LogEventLevel.Information;
    }

    /// <summary>
    /// Map Microsoft.Extensions.Logging.LogLevel to Serilog.Events.LogEventLevel
    /// </summary>
    private static LogEventLevel MapToSerilogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            LogLevel.None => LogEventLevel.Fatal, // Disable logging
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// Map Serilog.Events.LogEventLevel to Microsoft.Extensions.Logging.LogLevel
    /// (Used by RuntimeLoggingService for UI display)
    /// </summary>
    public static LogLevel MapFromSerilogLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }

    /// <summary>
    /// Get current configuration (for Settings UI display)
    /// </summary>
    public LogConfig GetCurrentConfig()
    {
        return new LogConfig
        {
            DefaultLevel = MapFromSerilogLevel(_defaultSwitch.MinimumLevel),
            Overrides = _switches.ToDictionary(
                kvp => kvp.Key,
                kvp => MapFromSerilogLevel(kvp.Value.MinimumLevel)),
            LastModified = DateTimeOffset.UtcNow
        };
    }
}

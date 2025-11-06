using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.Configuration;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Configures all application options from environment variables
    /// Pattern: SectionName__PropertyName (e.g., OPENAI__APIKEY maps to OpenAI:ApiKey)
    /// </summary>
    public static IServiceCollection AddApplicationConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection("App"));
        services.Configure<SpamDetectionOptions>(configuration.GetSection("SpamDetection"));

        // NOTE: OpenAIOptions and SendGridOptions removed - now using database config
        // See OpenAIConfig and SendGridConfig in database (configs table)

        // MessageHistoryOptions: Set ImageStoragePath from App:DataPath if not explicitly configured
        services.Configure<MessageHistoryOptions>(options =>
        {
            configuration.GetSection("MessageHistory").Bind(options);

            // If ImageStoragePath is still the default "/data", use App:DataPath as base
            var dataPath = configuration["App:DataPath"] ?? "/data";
            if (options.ImageStoragePath == "/data")
            {
                options.ImageStoragePath = dataPath;
            }
        });

        // Unified configuration service (database-driven config with global/chat-specific merging)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IConfigService, ConfigService>();

        // File scanning configuration repository (Phase 4.17)
        services.AddScoped<IFileScanningConfigRepository, FileScanningConfigRepository>();

        // Telegram configuration migration service (migrates env vars to database on first startup)
        services.AddHostedService<TelegramConfigMigrationService>();

        return services;
    }
}

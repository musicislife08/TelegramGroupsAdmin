using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration;

public static class ConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Configures all application options from environment variables
        /// Pattern: SectionName__PropertyName (e.g., OPENAI__APIKEY maps to OpenAI:ApiKey)
        /// </summary>
        public IServiceCollection AddApplicationConfiguration(IConfiguration configuration)
        {
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.Configure<ContentDetectionOptions>(configuration.GetSection("SpamDetection"));

            // NOTE: OpenAIOptions and SendGridOptions removed - now using database config
            // See AIProviderConfig and SendGridConfig in database (configs table)

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

            // Configuration repositories (database-driven config storage)
            services.AddScoped<IConfigRepository, ConfigRepository>();

            // System configuration repository (API keys, service settings, per-chat config overrides)
            services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();

            return services;
        }
    }
}

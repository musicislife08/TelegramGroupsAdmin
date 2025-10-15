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
        services.Configure<OpenAIOptions>(configuration.GetSection("OpenAI"));
        services.Configure<TelegramOptions>(configuration.GetSection("Telegram"));
        services.Configure<SpamDetectionOptions>(configuration.GetSection("SpamDetection"));
        services.Configure<MessageHistoryOptions>(configuration.GetSection("MessageHistory"));
        services.Configure<TelegramGroupsAdmin.Services.Email.SendGridOptions>(configuration.GetSection("SendGrid"));

        // Unified configuration service (database-driven config with global/chat-specific merging)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IConfigService, ConfigService>();

        return services;
    }
}

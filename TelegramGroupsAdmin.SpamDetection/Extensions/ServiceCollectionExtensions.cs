using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Services;
using TelegramGroupsAdmin.SpamDetection.Repositories;

namespace TelegramGroupsAdmin.SpamDetection.Extensions;

/// <summary>
/// Extension methods for registering spam detection services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add spam detection services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddSpamDetection(this IServiceCollection services)
    {
        // Register main spam detection engine (loads config from repository dynamically)
        services.AddScoped<ISpamDetectionEngine, SpamDetectionEngine>();

        // Register core services
        services.AddScoped<ITokenizerService, TokenizerService>();
        services.AddScoped<IOpenAITranslationService, OpenAITranslationService>();
        // NOTE: IMessageHistoryService is registered by the main app (TelegramAdminBotService implements it)

        // Register repositories (needed by engine for config)
        services.AddScoped<ISpamDetectionConfigRepository, SpamDetectionConfigRepository>();

        // Register individual spam checks
        // NOTE: Translation happens in SpamDetectionEngine preprocessing, not as a spam check
        services.AddScoped<ISpamCheck, Checks.InvisibleCharsSpamCheck>();  // Runs FIRST on original message
        services.AddScoped<ISpamCheck, Checks.StopWordsSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.CasSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.SimilaritySpamCheck>();
        services.AddScoped<ISpamCheck, Checks.BayesSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.SpacingSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.OpenAISpamCheck>();
        services.AddScoped<ISpamCheck, Checks.UrlBlocklistSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.ThreatIntelSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.SeoScrapingSpamCheck>();
        services.AddScoped<ISpamCheck, Checks.ImageSpamCheck>();

        // Register HTTP client for external API calls
        services.AddHttpClient();

        // Register memory cache for caching API responses
        services.AddMemoryCache();

        return services;
    }
}
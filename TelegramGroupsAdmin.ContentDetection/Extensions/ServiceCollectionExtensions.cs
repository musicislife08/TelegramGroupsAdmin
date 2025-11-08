using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Extensions;

/// <summary>
/// Extension methods for registering spam detection services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add content detection services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddContentDetection(this IServiceCollection services)
    {
        // Register main spam detection engine (loads config from repository dynamically)
        services.AddScoped<IContentDetectionEngine, ContentDetectionEngine>();

        // Register core services
        services.AddScoped<ITokenizerService, TokenizerService>();
        services.AddScoped<IOpenAITranslationService, OpenAITranslationService>();
        services.AddScoped<IUrlContentScrapingService, UrlContentScrapingService>();
        services.AddSingleton<IImageTextExtractionService, ImageTextExtractionService>(); // ML-5: OCR service (Singleton: binary path lookup happens once)
        services.AddSingleton<IVideoFrameExtractionService, VideoFrameExtractionService>(); // ML-6: FFmpeg frame extraction (Singleton: binary path lookup happens once)
        // NOTE: IMessageHistoryService is registered by the main app (TelegramAdminBotService implements it)

        // Register repositories (needed by engine for config)
        services.AddScoped<ISpamDetectionConfigRepository, SpamDetectionConfigRepository>();
        services.AddScoped<IContentCheckConfigRepository, ContentCheckConfigRepository>(); // Phase 4.14: Critical checks

        // Register URL filtering repositories (Phase 4.13)
        services.AddScoped<IBlocklistSubscriptionsRepository, BlocklistSubscriptionsRepository>();
        services.AddScoped<IDomainFiltersRepository, DomainFiltersRepository>();
        services.AddScoped<ICachedBlockedDomainsRepository, CachedBlockedDomainsRepository>();

        // Register file scanning repositories (Phase 4.17)
        services.AddScoped<IFileScanResultRepository, FileScanResultRepository>();
        services.AddScoped<IFileScanQuotaRepository, FileScanQuotaRepository>();  // Phase 2: Cloud quota tracking

        // Register image training samples repository (ML-5: Layer 2 hash similarity)
        services.AddScoped<IImageTrainingSamplesRepository, ImageTrainingSamplesRepository>();

        // Register video training samples repository (ML-6: Layer 2 hash similarity)
        services.AddScoped<IVideoTrainingSamplesRepository, VideoTrainingSamplesRepository>();

        // Register URL filtering services (Phase 4.13)
        services.AddScoped<IBlocklistSyncService, BlocklistSyncService>();
        services.AddScoped<IUrlPreFilterService, UrlPreFilterService>();

        // Register file scanning services (Phase 4.17 - Tier 1: Local scanners)
        // Note: YARA was removed due to ARM compatibility issues - ClamAV provides superior coverage
        services.AddScoped<ClamAVScannerService>();
        services.AddScoped<Tier1VotingCoordinator>();

        // Register file scanning services (Phase 4.17 - Phase 2: Tier 2 cloud scanners)
        services.AddScoped<VirusTotalScannerService>();
        services.AddScoped<Tier2QueueCoordinator>();

        // Register file scanning utilities
        services.AddScoped<IFileScanningTestService, FileScanningTestService>();  // UI testing service

        // Register ML.NET threshold optimization services
        services.AddScoped<FeatureExtractionService>();
        services.AddScoped<IThresholdRecommendationService, ThresholdRecommendationService>();
        services.AddScoped<IStopWordRecommendationService, StopWordRecommendationService>(); // ML-6: Stop word recommendations

        // Register stop words repository
        services.AddScoped<IStopWordsRepository, StopWordsRepository>();

        // Register individual content checks
        // NOTE: Translation happens in ContentDetectionEngine preprocessing, not as a content check
        services.AddScoped<IContentCheck, Checks.InvisibleCharsSpamCheck>();  // Runs FIRST on original message
        services.AddScoped<IContentCheck, Checks.StopWordsSpamCheck>();
        services.AddScoped<IContentCheck, Checks.CasSpamCheck>();
        services.AddScoped<IContentCheck, Checks.SimilaritySpamCheck>();
        services.AddScoped<IContentCheck, Checks.BayesSpamCheck>();
        services.AddScoped<IContentCheck, Checks.SpacingSpamCheck>();
        services.AddScoped<IContentCheck, Checks.OpenAIContentCheck>();
        services.AddScoped<IContentCheck, Checks.UrlBlocklistSpamCheck>();
        services.AddScoped<IContentCheck, Checks.ThreatIntelSpamCheck>();
        services.AddScoped<IContentCheck, Checks.ImageSpamCheck>();
        services.AddScoped<IContentCheck, Checks.VideoSpamCheck>();  // ML-6: Video spam detection
        services.AddScoped<IContentCheck, Checks.FileScanningCheck>();  // Phase 4.17: File scanning (always_run=true)

        // Register HTTP client for external API calls
        services.AddHttpClient();

        // Register memory cache for caching API responses
        services.AddMemoryCache();

        return services;
    }
}
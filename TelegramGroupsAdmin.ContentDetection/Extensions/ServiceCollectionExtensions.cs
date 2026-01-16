using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;

namespace TelegramGroupsAdmin.ContentDetection.Extensions;

/// <summary>
/// Extension methods for registering spam detection services with dependency injection
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Add content detection services to the service collection
        /// </summary>
        /// <returns>Service collection for chaining</returns>
        public IServiceCollection AddContentDetection()
        {
            // Register V2 spam detection engine (SpamAssassin-style additive scoring)
            // Rollback: Change ContentDetectionEngineV2 → ContentDetectionEngine
            services.AddScoped<IContentDetectionEngine, ContentDetectionEngineV2>();

            // Register core services
            services.AddSingleton<ITokenizerService, TokenizerService>();
            // Note: IAITranslationService is registered in Core (depends on IChatService)
            services.AddSingleton<ILanguageDetectionService, FastTextLanguageDetectionService>(); // FastText language detection (Singleton: model loaded once, thread-safe)
            services.AddScoped<IUrlContentScrapingService, UrlContentScrapingService>();
            services.AddSingleton<IImageTextExtractionService, ImageTextExtractionService>(); // ML-5: OCR service (Singleton: binary path lookup happens once)
            services.AddSingleton<IVideoFrameExtractionService, VideoFrameExtractionService>(); // ML-6: FFmpeg frame extraction (Singleton: binary path lookup happens once)
                                                                                                // NOTE: IMessageContextProvider is registered by the main app (MessageContextAdapter)

            // Register repositories (needed by engine for config)
            services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
            // NOTE: ContentCheckConfigRepository removed - critical checks now extracted via GetCriticalCheckNamesAsync() from ContentDetectionConfig

            // Register detection results repository
            services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();

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

            // Register prompt version repository (Phase 4.X: AI-powered prompt builder)
            services.AddScoped<IPromptVersionRepository, PromptVersionRepository>();

            // Register threshold recommendations repository (ML.NET threshold optimization)
            services.AddScoped<IThresholdRecommendationsRepository, ThresholdRecommendationsRepository>();

            // Register unified reviews repository (handles Reports, ImpersonationAlerts, ExamFailures)
            services.AddScoped<IReviewsRepository, ReviewsRepository>();

            // Register reports repository (backward compatibility - uses same underlying Reviews table)
            services.AddScoped<IReportsRepository, ReportsRepository>();

            // Register URL filtering services (Phase 4.13)
            services.AddScoped<IBlocklistSyncService, BlocklistSyncService>();
            services.AddScoped<IUrlPreFilterService, UrlPreFilterService>();

            // Register file scanning services (Phase 4.17 - Tier 1: Local scanners)
            // Note: YARA was removed due to ARM compatibility issues - ClamAV provides superior coverage
            services.AddScoped<IFileScannerService, ClamAVScannerService>();
            services.AddScoped<Tier1VotingCoordinator>();

            // Register file scanning services (Phase 4.17 - Phase 2: Tier 2 cloud scanners)
            services.AddScoped<VirusTotalScannerService>();
            services.AddScoped<Tier2QueueCoordinator>();

            // Register file scanning utilities
            services.AddScoped<IFileScanningTestService, FileScanningTestService>();  // UI testing service

            // Register ML.NET threshold optimization services
            services.AddSingleton<FeatureExtractionService>();
            services.AddScoped<IThresholdRecommendationService, ThresholdRecommendationService>();
            services.AddScoped<IStopWordRecommendationService, StopWordRecommendationService>(); // ML-6: Stop word recommendations

            // Register ML.NET text classifier (Singleton: thread-safe model loading/retraining)
            services.AddSingleton<IMLTextClassifierService, MLTextClassifierService>();

            // Register stop words repository
            services.AddScoped<IStopWordsRepository, StopWordsRepository>();

            // Register training labels repository (Phase 1: ML.NET training labels)
            services.AddScoped<ITrainingLabelsRepository, TrainingLabelsRepository>();

            // Register ML training data repository (aggregates training data from multiple tables)
            // Scoped: Matches standard pattern used by all other repositories
            services.AddScoped<IMLTrainingDataRepository, MLTrainingDataRepository>();

            // Note: ContentDetectionEngineV2 registered above (line 25) with IContentDetectionEngine interface

            // Register V2 content checks (proper abstention support)
            // Key fix: Return Score=0 when finding nothing (not "Clean 20%")
            // Note: CAS check moved to user join flow (WelcomeService) - checks USER not MESSAGE
            services.AddScoped<IContentCheckV2, Checks.InvisibleCharsContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.StopWordsContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.SimilarityContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.BayesContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.SpacingContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.AIContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.UrlBlocklistContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.ThreatIntelContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.ImageContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.VideoContentCheckV2>();
            services.AddScoped<IContentCheckV2, Checks.FileScanningCheckV2>();  // Phase 4.17: File scanning (always_run=true)

            // Register HTTP client for external API calls
            services.AddHttpClient();

            // Note: HybridCache is registered in main project and provides L1 in-memory caching

            return services;
        }
    }
}
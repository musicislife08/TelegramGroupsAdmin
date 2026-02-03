using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;
using TelegramGroupsAdmin.Telegram.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Services.Telegram;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for registering Telegram services
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers all Telegram services including bot commands, background services, repositories, and moderation
        /// </summary>
        public IServiceCollection AddTelegramServices()
        {
            // Telegram repositories
            services.AddScoped<IUserActionsRepository, UserActionsRepository>();
            services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
            services.AddScoped<ITelegramUserMappingRepository, TelegramUserMappingRepository>();
            services.AddScoped<ITelegramLinkTokenRepository, TelegramLinkTokenRepository>();
            services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
            services.AddScoped<IWelcomeResponsesRepository, WelcomeResponsesRepository>();
            services.AddScoped<IAdminNotesRepository, AdminNotesRepository>(); // Phase 4.12
            services.AddScoped<IUserTagsRepository, UserTagsRepository>(); // Phase 4.12
            services.AddScoped<ITagDefinitionsRepository, TagDefinitionsRepository>(); // Phase 4.12
            services.AddScoped<IPendingNotificationsRepository, PendingNotificationsRepository>(); // DM notification system
            services.AddScoped<IReportCallbackContextRepository, ReportCallbackContextRepository>(); // DM action button contexts
            services.AddScoped<ILinkedChannelsRepository, LinkedChannelsRepository>(); // Linked channel impersonation detection
            // Note: IAuditLogRepository is registered in AddCoreServices() - it's a cross-cutting concern
            services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
            services.AddScoped<IExamSessionRepository, ExamSessionRepository>(); // Phase 2: Entrance exam state tracking
            services.AddScoped<IBanCelebrationGifRepository, BanCelebrationGifRepository>(); // Ban celebration GIF library
            services.AddScoped<IBanCelebrationCaptionRepository, BanCelebrationCaptionRepository>(); // Ban celebration caption library
            // REFACTOR-3: Extracted services from MessageHistoryRepository
            // NOTE: IMessageStatsService moved to main app (analytics consolidation)
            services.AddScoped<IMessageQueryService, MessageQueryService>();
            services.AddScoped<IMessageTranslationService, MessageTranslationService>();
            services.AddScoped<IMessageEditService, MessageEditService>();
            services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

            // Telegram infrastructure
            services.AddSingleton<ITelegramBotClientFactory, TelegramBotClientFactory>();
            services.AddSingleton<ITelegramConfigLoader, TelegramConfigLoader>(); // Database-backed config loader (replaces IOptions<TelegramOptions>)
            services.AddScoped<ITelegramImageService, TelegramImageService>();
            services.AddScoped<TelegramPhotoService>();
            services.AddScoped<TelegramMediaService>();

            // Message processing handlers (REFACTOR-1: extracted from MessageProcessingService)
            services.AddScoped<Handlers.MediaProcessingHandler>();
            services.AddScoped<Handlers.FileScanningHandler>();
            services.AddScoped<Handlers.ITranslationHandler, Handlers.TranslationHandler>();

            // REFACTOR-2: Additional handlers for image processing and job scheduling
            services.AddScoped<Handlers.ImageProcessingHandler>();
            services.AddScoped<Handlers.BackgroundJobScheduler>();
            services.AddScoped<Handlers.ContentDetectionOrchestrator>();
            services.AddScoped<Handlers.LanguageWarningHandler>();
            services.AddScoped<Handlers.MessageEditProcessor>();

            // Content check coordination (Phase 4.14: filters trusted/admin users, runs critical checks for all)
            services.AddScoped<IContentCheckCoordinator, ContentCheckCoordinator>();

            // DM delivery infrastructure (shared by notification system, welcome system, etc.)
            services.AddScoped<IBotDmService, BotDmService>();

            // Notification system (DM delivery with retry queue)
            services.AddScoped<INotificationChannel, TelegramDmChannel>();
            services.AddScoped<INotificationOrchestrator, NotificationOrchestrator>();

            // REFACTOR-5: Manager/Worker moderation architecture
            // Infrastructure
            services.AddScoped<IMessageBackfillService, MessageBackfillService>();

            // Domain handlers (workers) - Bot handlers (thin API wrappers)
            services.AddScoped<IBotMessageHandler, BotMessageHandler>();
            services.AddScoped<IBotChatHandler, BotChatHandler>();
            services.AddScoped<IBotUserHandler, BotUserHandler>();
            services.AddScoped<IBotMediaHandler, BotMediaHandler>();
            services.AddScoped<IBotBanHandler, BotBanHandler>();
            services.AddScoped<IBotRestrictHandler, BotRestrictHandler>();
            services.AddScoped<IBotModerationMessageHandler, BotModerationMessageHandler>();

            // Moderation domain handlers (non-bot operations)
            services.AddScoped<ITrustHandler, TrustHandler>();
            services.AddScoped<IWarnHandler, WarnHandler>();

            // Support handlers
            services.AddScoped<IAuditHandler, AuditHandler>();
            services.AddScoped<INotificationHandler, NotificationHandler>();
            services.AddScoped<ITrainingHandler, TrainingHandler>();

            // Bot services (orchestrate handlers with business logic)
            services.AddScoped<IBotChatService, BotChatService>();
            services.AddScoped<IBotUserService, BotUserService>();
            services.AddScoped<IBotMediaService, BotMediaService>();
            services.AddScoped<IBotModerationService, BotModerationService>();

            // Report service
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<UserAutoTrustService>();
            services.AddScoped<AdminMentionHandler>();
            services.AddScoped<ITelegramUserManagementService, TelegramUserManagementService>(); // Orchestrates Telegram user operations
            services.AddScoped<IUserMessagingService, UserMessagingService>(); // DM with fallback to chat mentions
            services.AddScoped<IWelcomeService, WelcomeService>();
            services.AddSingleton<IBanCallbackService, BanCallbackService>(); // Ban user selection callbacks
            services.AddSingleton<IReportCallbackService, ReportCallbackService>(); // Report moderation action callbacks
            services.AddScoped<IBotProtectionService, BotProtectionService>(); // Phase 6.1: Bot Auto-Ban
            services.AddScoped<IBotMessageService, BotMessageService>(); // Phase 1: Bot message storage and deletion tracking
            services.AddScoped<IWebBotMessagingService, WebBotMessagingService>(); // Phase 1: Web UI bot messaging with signature
            services.AddSingleton<IBanCelebrationCache, BanCelebrationCache>(); // Singleton: shuffle-bag state for ban celebrations
            services.AddScoped<IBanCelebrationService, BanCelebrationService>(); // Scoped: uses IBanCelebrationCache for state
            services.AddScoped<IThumbnailService, ThumbnailService>(); // Thumbnail generation for images/GIFs

            // Training data quality services
            services.AddSingleton<TextSimilarityService>();
            // Note: SimHashService is registered in Core's AddCoreServices()
            services.AddScoped<TrainingDataDeduplicationService>();

            // Phase 4.10: Anti-Impersonation Detection
            services.AddSingleton<IPhotoHashService, PhotoHashService>();
            services.AddScoped<IImpersonationDetectionService, ImpersonationDetectionService>();

            // Entrance exam evaluation (uses content moderation AI connection)
            services.AddScoped<IExamEvaluationService, ExamEvaluationService>();
            services.AddScoped<IExamFlowService, ExamFlowService>(); // Phase 2: Exam flow orchestration

            // CAS (Combot Anti-Spam) check on user join
            services.AddSingleton<ICasCheckService, CasCheckService>();

            // Bot command system (Keyed Services pattern)
            // Commands are Scoped (to allow injecting Scoped services like BotModerationService)
            // CommandRouter is Singleton (creates scopes internally when executing commands)
            // Keyed services allow direct resolution by command name without type dictionary
            services.AddKeyedScoped<IBotCommand, StartCommand>(CommandNames.Start);
            services.AddKeyedScoped<IBotCommand, HelpCommand>(CommandNames.Help);
            services.AddKeyedScoped<IBotCommand, LinkCommand>(CommandNames.Link);
            services.AddKeyedScoped<IBotCommand, SpamCommand>(CommandNames.Spam);
            services.AddKeyedScoped<IBotCommand, BanCommand>(CommandNames.Ban);
            services.AddKeyedScoped<IBotCommand, TrustCommand>(CommandNames.Trust);
            services.AddKeyedScoped<IBotCommand, UnbanCommand>(CommandNames.Unban);
            services.AddKeyedScoped<IBotCommand, WarnCommand>(CommandNames.Warn);
            services.AddKeyedScoped<IBotCommand, TempBanCommand>(CommandNames.TempBan);
            services.AddKeyedScoped<IBotCommand, MuteCommand>(CommandNames.Mute);
            services.AddKeyedScoped<IBotCommand, ReportCommand>(CommandNames.Report);
            services.AddKeyedScoped<IBotCommand, InviteCommand>(CommandNames.Invite);
            services.AddKeyedScoped<IBotCommand, DeleteCommand>(CommandNames.Delete);
            services.AddKeyedScoped<IBotCommand, MyStatusCommand>(CommandNames.MyStatus);
            services.AddSingleton<CommandRouter>();

            // Caching services (singletons for cross-request state)
            services.AddSingleton<IChatCache, ChatCache>();
            services.AddSingleton<IChatHealthCache, ChatHealthCache>();
            services.AddSingleton<IBotIdentityCache, BotIdentityCache>();

            // Background services (refactored into smaller services)
            services.AddSingleton<DetectionActionService>();
            services.AddScoped<IChatHealthRefreshOrchestrator, ChatHealthRefreshOrchestrator>();
            services.AddSingleton<IMessageProcessingService, MessageProcessingService>();

            // Telegram bot services (clean separation: capabilities vs lifecycle vs routing)
            services.AddSingleton<IUpdateRouter, UpdateRouter>();
            services.AddSingleton<ITelegramBotService, TelegramBotService>();
            services.AddHostedService<TelegramBotPollingHost>();

            return services;
        }
    }
}

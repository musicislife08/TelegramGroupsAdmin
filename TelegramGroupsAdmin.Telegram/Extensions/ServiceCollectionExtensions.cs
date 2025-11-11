using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
using TelegramGroupsAdmin.Telegram.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Services.Telegram;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// Extension methods for registering Telegram services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Telegram services including bot commands, background services, repositories, and moderation
    /// </summary>
    public static IServiceCollection AddTelegramServices(this IServiceCollection services)
    {
        // Telegram repositories
        services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<ITelegramUserMappingRepository, TelegramUserMappingRepository>();
        services.AddScoped<ITelegramLinkTokenRepository, TelegramLinkTokenRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();
        services.AddScoped<IReportsRepository, ReportsRepository>();
        services.AddScoped<IWelcomeResponsesRepository, WelcomeResponsesRepository>();
        services.AddScoped<IAdminNotesRepository, AdminNotesRepository>(); // Phase 4.12
        services.AddScoped<IUserTagsRepository, UserTagsRepository>(); // Phase 4.12
        services.AddScoped<ITagDefinitionsRepository, TagDefinitionsRepository>(); // Phase 4.12
        services.AddScoped<IImpersonationAlertsRepository, ImpersonationAlertsRepository>(); // Phase 4.10
        services.AddScoped<IPendingNotificationsRepository, PendingNotificationsRepository>(); // DM notification system
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        // REFACTOR-3: Extracted services from MessageHistoryRepository
        services.AddScoped<IMessageQueryService, MessageQueryService>();
        services.AddScoped<IMessageStatsService, MessageStatsService>();
        services.AddScoped<IMessageTranslationService, MessageTranslationService>();
        services.AddScoped<IMessageEditService, MessageEditService>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IPromptVersionRepository, PromptVersionRepository>(); // Phase 4.X: AI-powered prompt builder
        services.AddScoped<IThresholdRecommendationsRepository, ThresholdRecommendationsRepository>(); // ML.NET threshold optimization
        services.AddScoped<IImageTrainingSamplesRepository, ImageTrainingSamplesRepository>(); // ML-5: Image spam training samples
        services.AddScoped<IVideoTrainingSamplesRepository, VideoTrainingSamplesRepository>(); // ML-6: Video spam training samples

        // Telegram infrastructure
        services.AddSingleton<TelegramBotClientFactory>();
        services.AddSingleton<TelegramConfigLoader>(); // Database-backed config loader (replaces IOptions<TelegramOptions>)
        services.AddScoped<ITelegramImageService, TelegramImageService>();
        services.AddSingleton<TelegramPhotoService>();
        services.AddSingleton<TelegramMediaService>();

        // Message processing handlers (REFACTOR-1: extracted from MessageProcessingService)
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.MediaProcessingHandler>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.FileScanningHandler>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.TranslationHandler>();

        // REFACTOR-2: Additional handlers for image processing and job scheduling
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.ImageProcessingHandler>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.BackgroundJobScheduler>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.ContentDetectionOrchestrator>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.LanguageWarningHandler>();
        services.AddScoped<TelegramGroupsAdmin.Telegram.Handlers.MessageEditProcessor>();

        // Content check coordination (Phase 4.14: filters trusted/admin users, runs critical checks for all)
        services.AddScoped<IContentCheckCoordinator, ContentCheckCoordinator>();

        // DM delivery infrastructure (shared by notification system, welcome system, etc.)
        // Singleton: Creates scopes internally for repository access
        services.AddSingleton<IDmDeliveryService, DmDeliveryService>();

        // Notification system (DM delivery with retry queue)
        services.AddScoped<INotificationChannel, TelegramDmChannel>();
        services.AddScoped<INotificationOrchestrator, NotificationOrchestrator>();

        // Moderation and user management services
        services.AddScoped<ModerationActionService>();
        services.AddScoped<UserAutoTrustService>();
        services.AddScoped<AdminMentionHandler>();
        services.AddScoped<TelegramUserManagementService>(); // Orchestrates Telegram user operations
        services.AddScoped<IUserMessagingService, UserMessagingService>(); // DM with fallback to chat mentions
        services.AddSingleton<IChatInviteLinkService, ChatInviteLinkService>(); // Phase 4.6: Invite link retrieval
        services.AddSingleton<IWelcomeService, WelcomeService>();
        services.AddSingleton<IBotProtectionService, BotProtectionService>(); // Phase 6.1: Bot Auto-Ban
        services.AddScoped<BotMessageService>(); // Phase 1: Bot message storage and deletion tracking
        services.AddScoped<IWebBotMessagingService, WebBotMessagingService>(); // Phase 1: Web UI bot messaging with signature

        // Phase 4.10: Anti-Impersonation Detection
        services.AddSingleton<IPhotoHashService, PhotoHashService>();
        services.AddScoped<IImpersonationDetectionService, ImpersonationDetectionService>();

        // Bot command system
        // Commands are Scoped (to allow injecting Scoped services like ModerationActionService)
        // CommandRouter is Singleton (creates scopes internally when executing commands)
        // Register both as interface and concrete type for CommandRouter resolution
        services.AddScoped<StartCommand>();
        services.AddScoped<IBotCommand, StartCommand>(sp => sp.GetRequiredService<StartCommand>());
        services.AddScoped<HelpCommand>();
        services.AddScoped<IBotCommand, HelpCommand>(sp => sp.GetRequiredService<HelpCommand>());
        services.AddScoped<LinkCommand>();
        services.AddScoped<IBotCommand, LinkCommand>(sp => sp.GetRequiredService<LinkCommand>());
        services.AddScoped<SpamCommand>();
        services.AddScoped<IBotCommand, SpamCommand>(sp => sp.GetRequiredService<SpamCommand>());
        services.AddScoped<BanCommand>();
        services.AddScoped<IBotCommand, BanCommand>(sp => sp.GetRequiredService<BanCommand>());
        services.AddScoped<TrustCommand>();
        services.AddScoped<IBotCommand, TrustCommand>(sp => sp.GetRequiredService<TrustCommand>());
        services.AddScoped<UnbanCommand>();
        services.AddScoped<IBotCommand, UnbanCommand>(sp => sp.GetRequiredService<UnbanCommand>());
        services.AddScoped<WarnCommand>();
        services.AddScoped<IBotCommand, WarnCommand>(sp => sp.GetRequiredService<WarnCommand>());
        services.AddScoped<TempBanCommand>();
        services.AddScoped<IBotCommand, TempBanCommand>(sp => sp.GetRequiredService<TempBanCommand>());
        services.AddScoped<MuteCommand>();
        services.AddScoped<IBotCommand, MuteCommand>(sp => sp.GetRequiredService<MuteCommand>());
        services.AddScoped<ReportCommand>();
        services.AddScoped<IBotCommand, ReportCommand>(sp => sp.GetRequiredService<ReportCommand>());
        services.AddScoped<InviteCommand>();
        services.AddScoped<IBotCommand, InviteCommand>(sp => sp.GetRequiredService<InviteCommand>());
        services.AddScoped<DeleteCommand>();
        services.AddScoped<IBotCommand, DeleteCommand>(sp => sp.GetRequiredService<DeleteCommand>());
        services.AddScoped<MyStatusCommand>();
        services.AddScoped<IBotCommand, MyStatusCommand>(sp => sp.GetRequiredService<MyStatusCommand>());
        services.AddSingleton<CommandRouter>();

        // Background services (refactored into smaller services)
        services.AddSingleton<SpamActionService>();
        services.AddSingleton<ChatManagementService>();
        services.AddSingleton<MessageProcessingService>();
        services.AddSingleton<TelegramAdminBotService>();
        services.AddSingleton<IMessageHistoryService>(sp => sp.GetRequiredService<TelegramAdminBotService>());
        services.AddHostedService(sp => sp.GetRequiredService<TelegramAdminBotService>());

        return services;
    }
}

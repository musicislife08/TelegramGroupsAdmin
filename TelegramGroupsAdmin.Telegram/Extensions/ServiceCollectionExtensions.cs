using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;
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
        services.AddScoped<AuditLogRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped<MessageHistoryRepository>();

        // Telegram infrastructure
        services.AddSingleton<TelegramBotClientFactory>();
        services.AddScoped<ITelegramImageService, TelegramImageService>();

        // Spam check coordination (filters trusted/admin users before spam detection)
        services.AddScoped<ISpamCheckCoordinator, SpamCheckCoordinator>();

        // Moderation and user management services
        services.AddScoped<ModerationActionService>();
        services.AddScoped<UserAutoTrustService>();
        services.AddScoped<AdminMentionHandler>();
        services.AddSingleton<IWelcomeService, WelcomeService>();

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
        services.AddScoped<ReportCommand>();
        services.AddScoped<IBotCommand, ReportCommand>(sp => sp.GetRequiredService<ReportCommand>());
        services.AddScoped<DeleteCommand>();
        services.AddScoped<IBotCommand, DeleteCommand>(sp => sp.GetRequiredService<DeleteCommand>());
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

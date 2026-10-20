using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Shared DI graph for the <see cref="MessageProcessingService.HandleNewMessageAsync"/> integration
/// fixtures: real repositories, config, profile-scan gate, identity service and the concrete
/// pre-detection handlers against the cloned database; the profile scan and session manager are the
/// fixture's substitutes; every other interface-based dependency is substituted. The graph keeps
/// <c>ContentDetectionOrchestrator</c> out, so a fixture must end the pipeline before detection
/// (an already-banned sender, a service message, or an inactive chat).
/// </summary>
internal static class MessageProcessingTestHarness
{
    public static ServiceProvider Build(
        MigrationTestHelper testHelper,
        string dataPath,
        IProfileScanService profileScanService,
        ITelegramSessionManager sessionManager)
    {
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.Configure<AppOptions>(options => options.DataPath = dataPath);

        services.AddCoreServices();
        services.AddHybridCache();
        services.AddDataProtection();

        // ── Real: repositories the profile-scan gate and the message path read/write ──
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();

        // ── Real: IConfigService, so the ScanOnFirstMessage flag round-trips through
        //    the same welcome_config JSONB path production reads. ──
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddScoped<IConfigService, ConfigService>();

        // ── Real: the gate under test ──
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton<ChatMetrics>();
        services.AddScoped<IProfileScanGate, ProfileScanGate>();

        // ── Real: the identity service the pipeline observes the sender through ──
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        // ── Real: concrete handlers on the pre-detection path. All take interface-only
        //    dependencies and short-circuit for a plain-text, non-command message. ──
        services.AddSingleton<CommandRouter>();
        services.AddScoped<AdminMentionHandler>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddScoped<ImageProcessingHandler>();
        services.AddScoped<TelegramMediaService>();
        services.AddScoped<MediaProcessingHandler>();
        services.AddScoped<FileScanningHandler>();
        services.AddScoped<BackgroundJobScheduler>();

        // ── Substituted: the scan itself (needs a live WTelegram User API session) ──
        services.AddSingleton(profileScanService);
        services.AddSingleton(sessionManager);

        // ── Substituted: everything else the path can touch ──
        services.AddSingleton(Substitute.For<IChatCache>());
        services.AddSingleton(Substitute.For<IJobScheduler>());
        services.AddSingleton(Substitute.For<ITranslationHandler>());
        services.AddSingleton(Substitute.For<IBotMediaService>());
        services.AddSingleton(Substitute.For<IBotMessageService>());
        services.AddSingleton(Substitute.For<IBotChatService>());
        services.AddSingleton(Substitute.For<IBotUserService>());
        services.AddSingleton(Substitute.For<IBotModerationService>());
        services.AddSingleton(Substitute.For<IMessageTranslationService>());
        services.AddSingleton(Substitute.For<IUrlContentScrapingService>());
        services.AddSingleton(Substitute.For<IExamFlowService>());
        services.AddSingleton(Substitute.For<IWelcomeResponsesRepository>());

        services.AddSingleton<IMessageProcessingService, MessageProcessingService>();

        var serviceProvider = services.BuildServiceProvider();

        // The translation handler is on the non-optional path: it must hand back the
        // original text so detection input is unchanged.
        serviceProvider.GetRequiredService<ITranslationHandler>()
            .GetTextForDetectionAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(
                new TranslationForDetectionResult(callInfo.ArgAt<string?>(0) ?? "", null, null)));

        return serviceProvider;
    }
}

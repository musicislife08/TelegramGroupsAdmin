using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// Ban celebration caption name masking against canonical data: the real identity service
/// resolves the banned user's names and latest verdict, and the real config service supplies the
/// chat's effective "Mask flagged names" setting.
///
/// Canonical anchors (no rows edited):
/// - User: <see cref="GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId"/>
///   (@bagging_armado, "Comdl Xbnbsprtni"); latest scan has ai_explicit_display_text = true.
/// - Chat: <see cref="GoldenDatasetConstants.DmCelebrations.WorkshopAlumniChatId"/>; ban celebration
///   enabled for auto bans, no chat welcome_config, so the global row's masking setting applies.
///
/// GIF and caption repositories are substituted with one fixed item: the canonical rotation
/// picks any of 74 captions (two lack {username}), and rotation is not this test's subject.
/// </summary>
[TestFixture]
public class BanCelebrationNameMaskingTests
{
    private const long ChatId = GoldenDatasetConstants.DmCelebrations.WorkshopAlumniChatId;
    private const long BannedUserId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IBotMessageService _messages = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _messages = Substitute.For<IBotMessageService>();
        _messages.SendAndSaveAnimationAsync(Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci => TelegramTestFactory.CreateMessage(messageId: 999, chatId: ci.ArgAt<long>(0)));

        var gifs = Substitute.For<IBanCelebrationGifRepository>();
        gifs.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "cached" });
        var captions = Substitute.For<IBanCelebrationCaptionRepository>();
        captions.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "{username} got banned!", DmText = "You got banned!" });

        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider);
        services.AddSingleton(new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build());
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCoreServices();

        // Real config service (effective welcome config → name masking)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigService, ConfigService>();

        // Real identity service; no rescans
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddSingleton(Substitute.For<IProfileScanGate>());
        services.AddScoped<IUserIdentityService, UserIdentityService>();

        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton(gifs);
        services.AddSingleton(captions);
        services.AddSingleton(_messages);
        services.AddSingleton(Substitute.For<IUserNotificationService>());
        services.AddScoped<IBanCelebrationService, BanCelebrationService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task SendBanCelebration_CanonicalExplicitUser_GlobalMaskingOn_CaptionShowsLabel()
    {
        // Guard the canonical preconditions so a later canonical edit fails loudly.
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var globalWelcome = await ctx.Configs.AsNoTracking()
                .Where(c => c.ChatId == 0).Select(c => c.WelcomeConfig).SingleAsync();
            using var json = JsonDocument.Parse(globalWelcome!);
            var profileScan = json.RootElement.GetProperty("joinSecurity").GetProperty("profileScan");
            Assert.That(profileScan.GetProperty("enabled").GetBoolean(), Is.True,
                "canonical global welcome_config has profile scanning enabled");
            // Absent → the ProfileScanConfig default (true). Masking does not depend on 'enabled'.
            var maskFlaggedNames = !profileScan.TryGetProperty("maskFlaggedNames", out var mask) || mask.GetBoolean();
            Assert.That(maskFlaggedNames, Is.True, "canonical global welcome_config masks flagged names");

            var chatWelcome = await ctx.Configs.AsNoTracking()
                .Where(c => c.ChatId == ChatId).Select(c => c.WelcomeConfig).SingleOrDefaultAsync();
            Assert.That(chatWelcome, Is.Null, "the chat has no own welcome_config, so the global row applies");

            var latestScan = await ctx.ProfileScanResults.AsNoTracking()
                .Where(r => r.UserId == BannedUserId).OrderByDescending(r => r.ScannedAt).FirstAsync();
            Assert.That(latestScan.AiExplicitDisplayText, Is.True, "the anchor's latest scan flags the name as explicit");
        }

        using var scope = _serviceProvider!.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IBanCelebrationService>();

        // The caller's copy is id-only; names and verdict come from the identity service.
        var sent = await sut.SendBanCelebrationAsync(
            ChatIdentity.FromId(ChatId), UserIdentity.FromId(BannedUserId), isAutoBan: true);

        Assert.That(sent, Is.True);
        await _messages.Received(1).SendAndSaveAnimationAsync(
            ChatId,
            Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text == NameRedaction.Explicit + " got banned!"),
            Arg.Any<CancellationToken>());
    }
}

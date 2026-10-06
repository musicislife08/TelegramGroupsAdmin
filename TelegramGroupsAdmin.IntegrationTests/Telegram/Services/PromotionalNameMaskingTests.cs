using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
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
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// A promotional name is masked in what the bot posts in the group only while the user is banned,
/// and never in an admin DM. Real identity service (user_identities view + mapper), real config
/// service (the chat's effective masking), real ban celebration and admin notification services;
/// the Telegram transports and the notification audience lookups are faked.
///
/// Canonical anchors (flags read back first in every test):
/// - <see cref="GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId"/> (@Adexfunnel): banned after
///   profile-scan alert #178; only scan row is the imported prod scan, promotional (canonical edit 2026-10-05).
/// - <see cref="GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalUserId"/> (@splendorfraying):
///   not banned, only scan row 526 promotional (canonical edit 2026-10-05).
/// - <see cref="GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId"/> (@bagging_armado):
///   banned, latest row 534 explicit (read-only).
/// - Chat: Workshop Alumni (no welcome_config, so the global row applies; deliverable DM subscriber).
/// </summary>
[TestFixture]
public class PromotionalNameMaskingTests
{
    private const long ChatId = GoldenDatasetConstants.DmCelebrations.WorkshopAlumniChatId;
    private const long BannedPromotionalUserId = GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId;
    private const long UnbannedPromotionalUserId = GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalUserId;
    private const long BannedExplicitUserId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
    private const long AdminRecipientId = GoldenDatasetConstants.IdentityService.TrustedUserId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IBotMessageService _messages = null!;
    private IUserNotificationService _userNotifications = null!;
    private IBotDmService _dms = null!;

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
        _userNotifications = Substitute.For<IUserNotificationService>();

        // Admin DM path: one unlinked chat admin with DMs enabled, no web users.
        _dms = Substitute.For<IBotDmService>();
        var chatAdmins = Substitute.For<IChatAdminsRepository>();
        chatAdmins.GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>()).Returns([
            new ChatAdmin { Id = 1, ChatId = ChatId, User = UserIdentity.ForTest(AdminRecipientId), IsActive = true, BotDmEnabled = true }
        ]);
        var webUsers = Substitute.For<IUserRepository>();
        webUsers.GetWebUsersWithChatAccessAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns([]);
        var mappings = Substitute.For<ITelegramUserMappingRepository>();
        mappings.GetTelegramIdsByUserIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns([]);

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

        // Real ban celebration service
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton(gifs);
        services.AddSingleton(captions);
        services.AddSingleton(_messages);
        services.AddSingleton(_userNotifications);
        services.AddScoped<IBanCelebrationService, BanCelebrationService>();

        // Real admin notification service and DM dispatcher
        services.AddSingleton(_dms);
        services.AddSingleton(chatAdmins);
        services.AddSingleton(webUsers);
        services.AddSingleton(mappings);
        services.AddSingleton(Substitute.For<INotificationPreferencesRepository>());
        services.AddSingleton(Substitute.For<IEmailService>());
        services.AddSingleton(Substitute.For<IWebPushNotificationService>());
        services.AddSingleton(Substitute.For<IReportCallbackContextRepository>());
        services.AddScoped<NotificationDmDispatcher>();
        services.AddScoped<IAdminNotificationService, AdminNotificationService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task GuardAnchorAsync(long userId, long? expectedScanId, bool isExplicit, bool isPromotional, bool isBanned,
        decimal? expectedScore = null, ProfileScanOutcome? expectedOutcome = null)
    {
        await using var ctx = _testHelper!.GetDbContext();
        var latest = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt).ThenByDescending(r => r.Id)
            .FirstAsync();
        var user = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.Multiple(() =>
        {
            if (expectedScanId is { } scanId)
                Assert.That(latest.Id, Is.EqualTo(scanId), "anchor's latest scan row");
            if (expectedScore is { } score)
                Assert.That(latest.Score, Is.EqualTo(score), "anchor's scan score");
            if (expectedOutcome is { } outcome)
                Assert.That(latest.Outcome, Is.EqualTo((int)outcome), "anchor's scan outcome");
            Assert.That(latest.AiExplicitDisplayText, Is.EqualTo(isExplicit), "explicit flag");
            Assert.That(latest.AiPromotionalDisplayText, Is.EqualTo(isPromotional), "promotional flag");
            Assert.That(user.IsBanned, Is.EqualTo(isBanned), "ban state");
            Assert.That(user.IsTrusted, Is.False, "not trusted");
        });
    }

    private async Task<(UserIdentity Identity, NameMasking Masking, string ChatName, WelcomeConfig Config)> ResolveForChatAsync(long userId)
    {
        using var scope = _serviceProvider!.CreateScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ResolveAsync(userId, CancellationToken.None);
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var masking = await configService.GetNameMaskingAsync(ChatId);
        var config = await configService.GetEffectiveWelcomeAsync(ChatId);
        Assert.That(config, Is.Not.Null, "the global welcome_config applies to Workshop Alumni");
        var template = config!.Mode is WelcomeMode.DmWelcome or WelcomeMode.EntranceExam
            ? config.DmChatTeaserMessage
            : config.MainWelcomeMessage;
        Assert.That(template, Does.Contain("{username}"), "the chat's welcome template mentions the user");
        await using var ctx = _testHelper!.GetDbContext();
        var chatName = await ctx.ManagedChats.AsNoTracking()
            .Where(c => c.ChatId == ChatId).Select(c => c.ChatName).SingleAsync();
        return (identity, masking, chatName ?? ChatId.ToString(), config);
    }

    [Test]
    public async Task BannedPromotionalUser_WelcomeMessageInGroup_ShowsSpamLabel()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, expectedScanId: null,
            isExplicit: false, isPromotional: true, isBanned: true,
            expectedScore: 2.8m, expectedOutcome: ProfileScanOutcome.HeldForReview);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(BannedPromotionalUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(masking, Is.EqualTo(NameMasking.On));
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Promotional));
            Assert.That(message.Text, Does.Contain(NameRedaction.Spam));
            Assert.That(message.Text, Does.Not.Contain(identity.DisplayName));
        });
    }

    [Test]
    public async Task BannedPromotionalUser_BanCelebrationCaptionAndSubscriberCopy_ShowSpamLabel()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, expectedScanId: null,
            isExplicit: false, isPromotional: true, isBanned: true,
            expectedScore: 2.8m, expectedOutcome: ProfileScanOutcome.HeldForReview);
        using var scope = _serviceProvider!.CreateScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<IBanCelebrationSubscriberRepository>()
            .HasDeliverableSubscribersAsync(ChatId), Is.True, "Workshop Alumni has a deliverable DM subscriber");
        var sut = scope.ServiceProvider.GetRequiredService<IBanCelebrationService>();

        // The caller's copy is id-only; names and verdict come from the identity service.
        var sent = await sut.SendBanCelebrationAsync(
            ChatIdentity.FromId(ChatId), UserIdentity.FromId(BannedPromotionalUserId), isAutoBan: true);

        const string expected = NameRedaction.Spam + " got banned!";
        Assert.That(sent, Is.True);
        await _messages.Received(1).SendAndSaveAnimationAsync(
            ChatId, Arg.Any<InputFile>(), Arg.Is<TelegramMessage>(m => m!.Text == expected), Arg.Any<CancellationToken>());
        await _userNotifications.Received(1).EnqueueBanCelebrationAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), expected, 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BannedPromotionalUser_AdminNotificationDm_ShowsRealName()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, expectedScanId: null,
            isExplicit: false, isPromotional: true, isBanned: true,
            expectedScore: 2.8m, expectedOutcome: ProfileScanOutcome.HeldForReview);
        using var scope = _serviceProvider!.CreateScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>()
            .ResolveAsync(BannedPromotionalUserId, CancellationToken.None);
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Promotional));

        await scope.ServiceProvider.GetRequiredService<IAdminNotificationService>().SendProfileScanAlertAsync(
            ChatIdentity.FromId(ChatId), identity, score: 3.0m, signals: "name_signal", aiReason: null, reportId: 1);

        var dmTexts = _dms.ReceivedCalls().SelectMany(c => c.GetArguments().OfType<string>()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(dmTexts, Has.Some.Contains(identity.DisplayName));
            Assert.That(dmTexts, Has.None.Contains(NameRedaction.Spam));
        });
    }

    [Test]
    public async Task UnbannedPromotionalUser_WelcomeMessageInGroup_ShowsRealName()
    {
        await GuardAnchorAsync(UnbannedPromotionalUserId, GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalScanId,
            isExplicit: false, isPromotional: true, isBanned: false);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(UnbannedPromotionalUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(masking, Is.EqualTo(NameMasking.On));
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Clean));
            Assert.That(message.Text, Does.Contain(identity.DisplayName));
            Assert.That(message.Text, Does.Not.Contain("[name removed"));
        });
    }

    [Test]
    public async Task BannedExplicitUser_WelcomeMessageInGroup_ShowsExplicitLabel()
    {
        // Row 534 is read-only, so its promotional flag stays false; precedence with both flags set
        // is pinned by UserIdentityMappingTests.
        await GuardAnchorAsync(BannedExplicitUserId, expectedScanId: 534,
            isExplicit: true, isPromotional: false, isBanned: true);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(BannedExplicitUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
            Assert.That(message.Text, Does.Contain(NameRedaction.Explicit));
            Assert.That(message.Text, Does.Not.Contain(NameRedaction.Spam));
        });
    }
}

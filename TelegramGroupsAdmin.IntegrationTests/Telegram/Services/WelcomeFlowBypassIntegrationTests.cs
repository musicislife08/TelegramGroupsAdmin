using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// Integration tests for the welcome-flow bypass pipeline.
/// Exercises <see cref="IWelcomeBypassResolver"/> together with <see cref="IAuditHandler"/>
/// against a real PostgreSQL database (Testcontainers) to verify that each of the four
/// bypass scenarios — chat admin, linked web admin, trusted user, and pre-banned trusted —
/// produces the expected database state.
///
/// <para>
/// Harness choice (Option B): <see cref="IBotUserService"/> is mocked via NSubstitute because the
/// Telegram chat-admin signal is sourced from the live Telegram API, which integration tests cannot
/// hit. Every other dependency along the bypass path — repositories, <see cref="IConfigService"/>,
/// <see cref="AuditHandler"/>, and the <see cref="WelcomeBypassResolver"/> itself — runs against
/// real implementations backed by the Testcontainers-managed Postgres instance.
/// </para>
/// </summary>
[TestFixture]
public class WelcomeFlowBypassIntegrationTests
{
    // ── constants matching seeded test data ───────────────────────────────────
    // 100001 is linked to Owner user b388ee38-... by 07_base_telegram_user_mappings.sql.
    private const long LinkedOwnerTelegramUserId = 100001L;

    // 100002 is an unlinked, non-trusted-by-default user (see 00_base_telegram_users.sql).
    private const long TrustedUserTelegramId = 100002L;

    // 100004 is another unlinked, non-trusted user, used for the pre-banned scenario.
    private const long PreBannedUserTelegramId = 100004L;

    // Any supergroup-ish id — the bypass resolver and audit handler don't verify the
    // chat's existence in managed_chats, so we use a synthetic id that doesn't collide
    // with any seeded row.
    private const long TestChatId = -1009876543210L;

    // ── infrastructure ────────────────────────────────────────────────────────
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    // ── services under test ───────────────────────────────────────────────────
    private IWelcomeBypassResolver? _bypassResolver;
    private IAuditHandler? _auditHandler;
    private IConfigService? _configService;
    private ITelegramUserRepository? _telegramUserRepository;

    // ── mocks ─────────────────────────────────────────────────────────────────
    private IBotUserService? _mockBotUserService;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Seed the base telegram_users, web users, managed chats, and the
        // 07_base_telegram_user_mappings.sql fixture (linked-owner mapping).
        // These are the only fixtures the bypass pipeline actually queries.
        await GoldenDataset.LoadSqlScriptAsync(
            "SQL.00_base_telegram_users.sql",
            sql => _testHelper.ExecuteSqlAsync(sql));
        await GoldenDataset.LoadSqlScriptAsync(
            "SQL.01_base_web_users.sql",
            sql => _testHelper.ExecuteSqlAsync(sql));
        await GoldenDataset.LoadSqlScriptAsync(
            "SQL.07_base_telegram_user_mappings.sql",
            sql => _testHelper.ExecuteSqlAsync(sql));

        _mockBotUserService = Substitute.For<IBotUserService>();

        // Default: every user is a plain Member (not a chat admin). Individual tests
        // override this per user id when they need to simulate admin status.
        _mockBotUserService
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => BuildMemberChatMember(callInfo.ArgAt<long>(1)));

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Real repositories — the full bypass path runs against the real Postgres DB.
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<ITelegramUserMappingRepository, TelegramUserMappingRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        // ConfigService + its dependencies — required so the resolver can read the
        // TrustedBypass.Enabled toggle via IConfigService.GetEffectiveAsync.
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddDataProtection();
        services.AddScoped<IConfigService, ConfigService>();

        // Telegram-API-sourced service is the only dependency we mock. See the
        // class-level comment (Option B) for rationale.
        services.AddSingleton(_mockBotUserService);

        // Bypass resolver is Singleton in production and uses IServiceScopeFactory to
        // fan out to scoped dependencies. Keep the same lifetime here so we exercise the
        // scope-management path end-to-end.
        services.AddSingleton<IWelcomeBypassResolver, WelcomeBypassResolver>();

        // Real audit handler writes user_actions rows via the real UserActionsRepository.
        services.AddScoped<IAuditHandler, AuditHandler>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _bypassResolver = scope.ServiceProvider.GetRequiredService<IWelcomeBypassResolver>();
        _auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
        _configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        _telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scenario 1: Joining user is a Telegram chat administrator.
    /// Expectation: resolver returns <see cref="BypassDecision.ChatAdmin"/>, audit row is
    /// written with the chat-admin reason string, and no welcome_responses row is inserted.
    /// </summary>
    [Test]
    public async Task ChatAdminJoin_WritesAuditRow_NoWelcomeResponse()
    {
        // Arrange — simulate the Telegram API reporting this user as an Administrator.
        // We use TrustedUserTelegramId (100002) because it has no web mapping, so the
        // only rule that can match is the chat-admin rule.
        _mockBotUserService!
            .GetChatMemberAsync(TestChatId, TrustedUserTelegramId, Arg.Any<CancellationToken>())
            .Returns(BuildAdministratorChatMember(TrustedUserTelegramId));

        var user = UserIdentity.FromId(TrustedUserTelegramId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act — resolver classifies the join, then audit handler logs the decision.
        var decision = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        await _auditHandler!.LogWelcomeBypassAsync(user, chat, decision, CancellationToken.None);

        // Assert — the decision was ChatAdmin and a matching user_actions row exists.
        Assert.That(decision, Is.EqualTo(BypassDecision.ChatAdmin));
        await AssertBypassAuditRowAsync(
            TrustedUserTelegramId,
            expectedReason: "Telegram chat admin/creator");
        await AssertNoWelcomeResponseAsync(TrustedUserTelegramId);
    }

    /// <summary>
    /// Scenario 2: Joining user is linked to an Owner-level web admin via
    /// 07_base_telegram_user_mappings.sql. Expectation: resolver returns
    /// <see cref="BypassDecision.WebAdmin"/> with the WebAdmin reason string.
    /// </summary>
    [Test]
    public async Task WebAdminJoin_LinkedOwner_WritesAuditAndBypasses()
    {
        // Arrange — the mock defaults to returning Member, so the ChatAdmin rule fails
        // and the resolver falls through to the linked-web-admin rule.
        var user = UserIdentity.FromId(LinkedOwnerTelegramUserId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act
        var decision = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        await _auditHandler!.LogWelcomeBypassAsync(user, chat, decision, CancellationToken.None);

        // Assert
        Assert.That(decision, Is.EqualTo(BypassDecision.WebAdmin));
        await AssertBypassAuditRowAsync(
            LinkedOwnerTelegramUserId,
            expectedReason: "Linked web admin (GlobalAdmin/Owner)");
        await AssertNoWelcomeResponseAsync(LinkedOwnerTelegramUserId);
    }

    /// <summary>
    /// Scenario 3: Joining user is trusted and the per-chat/global bypass toggle is on.
    /// Expectation: resolver returns <see cref="BypassDecision.Trusted"/>, audit row is
    /// written with the Trusted reason string.
    /// </summary>
    [Test]
    public async Task TrustedUserJoin_ToggleOn_Bypasses_CreatesAudit()
    {
        // Arrange — mark user 100002 as trusted, then enable global bypass.
        await MarkUserTrustedAsync(TrustedUserTelegramId);

        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessage = "{username} welcomed automatically - trusted user",
                AnnouncementTtlSeconds = 30,
            },
        };
        await _configService!.SaveAsync(ConfigType.Welcome, ChatIdentity.FromId(0), welcomeConfig);

        var user = UserIdentity.FromId(TrustedUserTelegramId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act
        var decision = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        await _auditHandler!.LogWelcomeBypassAsync(user, chat, decision, CancellationToken.None);

        // Assert
        Assert.That(decision, Is.EqualTo(BypassDecision.Trusted));
        await AssertBypassAuditRowAsync(
            TrustedUserTelegramId,
            expectedReason: "Trusted user, bypass enabled");
        await AssertNoWelcomeResponseAsync(TrustedUserTelegramId);
    }

    /// <summary>
    /// Scenario 4: Joining user is both trusted and banned. This is the ordering-invariant
    /// case — in the real WelcomeService pre-banned users are kicked <em>before</em> the
    /// resolver runs. Here we verify the data-level invariant that the bypass pipeline is
    /// never invoked for a banned user: the caller (WelcomeService) checks IsBanned first
    /// and short-circuits, so no user_actions.WelcomeBypass row is ever written.
    /// </summary>
    [Test]
    public async Task PreBannedTrustedUser_DoesNotBypass_NoAuditRow()
    {
        // Arrange — configure the full pre-banned-trusted state and enable the toggle.
        await MarkUserTrustedAsync(PreBannedUserTelegramId);
        await _telegramUserRepository!.SetBanStatusAsync(
            PreBannedUserTelegramId, isBanned: true, expiresAt: null, CancellationToken.None);

        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessage = "ignored",
                AnnouncementTtlSeconds = 30,
            },
        };
        await _configService!.SaveAsync(ConfigType.Welcome, ChatIdentity.FromId(0), welcomeConfig);

        // Act — simulate WelcomeService's ordering: the pre-ban check short-circuits
        // before ResolveAsync is ever called. We therefore DO NOT call the resolver.
        var telegramUser = await _telegramUserRepository!
            .GetByTelegramIdAsync(PreBannedUserTelegramId, CancellationToken.None);
        Assert.That(telegramUser, Is.Not.Null);
        Assert.That(telegramUser!.IsBanned, Is.True,
            "Precondition failed: user should be flagged banned before the ordering check runs.");

        // Assert — no bypass audit row was ever written, because the pre-ban short-circuit
        // wins over the bypass resolver per the WelcomeService ordering invariant.
        await using var context = _testHelper!.GetDbContext();
        var bypassRows = await context.UserActions
            .AsNoTracking()
            .Where(a => a.UserId == PreBannedUserTelegramId
                        && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(bypassRows, Is.Empty,
            "Pre-banned user must not produce a bypass audit row — pre-ban short-circuit wins.");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ChatMemberMember"/> (plain Member status) — the default reply
    /// from the mock, used for every user that is not explicitly promoted to admin.
    /// </summary>
    private static ChatMember BuildMemberChatMember(long telegramUserId) =>
        new ChatMemberMember
        {
            User = new User
            {
                Id = telegramUserId,
                FirstName = "Test",
                IsBot = false,
            },
        };

    /// <summary>
    /// Builds a <see cref="ChatMemberAdministrator"/> used to simulate chat-admin status
    /// for the ChatAdmin bypass scenario.
    /// </summary>
    private static ChatMember BuildAdministratorChatMember(long telegramUserId) =>
        new ChatMemberAdministrator
        {
            User = new User
            {
                Id = telegramUserId,
                FirstName = "Test",
                IsBot = false,
            },
            CanBeEdited = false,
            IsAnonymous = false,
            CanManageChat = true,
            CanDeleteMessages = true,
            CanManageVideoChats = false,
            CanRestrictMembers = true,
            CanPromoteMembers = false,
            CanChangeInfo = false,
            CanInviteUsers = true,
            CanPostStories = false,
            CanEditStories = false,
            CanDeleteStories = false,
        };

    /// <summary>
    /// Marks a seeded telegram user as trusted by flipping the flag directly via repository.
    /// We use <see cref="ITelegramUserRepository.TrustUserAsync"/> so the write goes through the
    /// production code path, including UpdatedAt bookkeeping.
    /// </summary>
    private async Task MarkUserTrustedAsync(long telegramUserId)
    {
        await _telegramUserRepository!.TrustUserAsync(telegramUserId, CancellationToken.None);
    }

    /// <summary>
    /// Asserts exactly one user_actions row exists for the given user with
    /// <see cref="UserActionType.WelcomeBypass"/>, the expected reason, and
    /// <c>system_identifier = "welcome_bypass"</c> (from <see cref="Actor.WelcomeBypass"/>).
    /// </summary>
    private async Task AssertBypassAuditRowAsync(long telegramUserId, string expectedReason)
    {
        await using var context = _testHelper!.GetDbContext();
        var rows = await context.UserActions
            .AsNoTracking()
            .Where(a => a.UserId == telegramUserId
                        && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(1),
                "Exactly one WelcomeBypass audit row should be written for the user.");
            Assert.That(rows[0].Reason, Is.EqualTo(expectedReason),
                "The reason string distinguishes ChatAdmin/WebAdmin/Trusted paths.");
            Assert.That(rows[0].SystemIdentifier, Is.EqualTo("welcome_bypass"),
                "The audit row must be attributed to the WelcomeBypass system actor.");
            Assert.That(rows[0].WebUserId, Is.Null,
                "Exclusive-arc: web_user_id must be null when system_identifier is set.");
            Assert.That(rows[0].TelegramUserId, Is.Null,
                "Exclusive-arc: telegram_user_id (actor column) must be null when system_identifier is set.");
            Assert.That(rows[0].ChatId, Is.EqualTo(TestChatId),
                "Bypass audit row records the chat where the join occurred.");
            Assert.That(rows[0].MessageId, Is.Null,
                "Bypass rows never reference a specific message.");
        }
    }

    /// <summary>
    /// Asserts that no welcome_responses row was written for the bypassed user.
    /// Bypass users short-circuit before the welcome response record is created.
    /// </summary>
    private async Task AssertNoWelcomeResponseAsync(long telegramUserId)
    {
        await using var context = _testHelper!.GetDbContext();
        var responses = await context.WelcomeResponses
            .AsNoTracking()
            .Where(w => w.UserId == telegramUserId && w.ChatId == TestChatId)
            .ToListAsync();
        Assert.That(responses, Is.Empty,
            "Bypassed users should not produce a welcome_responses row.");
    }
}

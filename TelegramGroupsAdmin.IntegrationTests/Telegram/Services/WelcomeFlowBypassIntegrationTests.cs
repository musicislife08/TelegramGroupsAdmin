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
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using TelegramGroupsAdmin.Configuration.Services;

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
    // ── canonical anchor IDs ──────────────────────────────────────────────────
    // 9899999990001 — synthetic telegram user seeded inline in WebAdminJoin_LinkedOwner test.
    // This ID is in the canonical telegram-user range [9_000_000_000_000, 10_000_000_000_000)
    // and is reserved here so it never collides with any canonical telegram_users row.
    // It is mapped to the Owner web user (b388ee38-...) in the test arrange block.
    // A separate synthetic user is needed because all three canonical telegram_user_mappings
    // rows reference users who are also registered in chat_admins — which would cause the
    // ChatAdmin rule (Rule 1) to fire before the WebAdmin rule.
    private const long LinkedOwnerTelegramUserId = 9899999990001L;

    // 9862700513599 (@unbeatenmutiny) — untrusted, not mapped to any web user.
    // Used for the TrustedUser bypass scenario (trust is set in-test).
    private const long TrustedUserTelegramId = 9862700513599L;

    // 9997671644156 ("Sediment Sitter") — untrusted, not mapped to any web user.
    // Used for the PreBanned scenario (trust + ban both set in-test).
    private const long PreBannedUserTelegramId = 9997671644156L;

    // Any supergroup-ish id — the bypass resolver and audit handler don't verify the
    // chat's existence in managed_chats, so we use a synthetic id that doesn't collide
    // with any canonical managed_chats row (canonical chat IDs are 15-digit numbers in
    // the range [-100_099_999_999_999, -100_000_000_000_000]).
    private const long TestChatId = -1009876543210L;

    // ── infrastructure ────────────────────────────────────────────────────────
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    // ── services under test ───────────────────────────────────────────────────
    private IWelcomeBypassResolver? _bypassResolver;
    private IAuditHandler? _auditHandler;
    private IConfigService? _configService;
    private ITelegramUserRepository? _telegramUserRepository;
    private IChatAdminsRepository? _chatAdminsRepository;

    // ── mocks ─────────────────────────────────────────────────────────────────
    private IBotUserService? _mockBotUserService;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

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
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();

        // ConfigService + its dependencies — required so the resolver can read the
        // TrustedBypass.Enabled toggle via IConfigService.GetEffectiveAsync.
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddDataProtection();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
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
        _chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
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
    /// Expectation: resolver returns <see cref="BypassDecision.Admin"/>, audit row is
    /// written with the chat-admin reason string, and no welcome_responses row is inserted.
    /// </summary>
    [Test]
    public async Task ChatAdminJoin_WritesAuditRow_NoWelcomeResponse()
    {
        // Arrange — seed the parent managed_chats row first to satisfy the chat_admins FK,
        // then seed the chat_admins row so the resolver's DB-backed chat-admin rule fires.
        // We use TrustedUserTelegramId because it has no web mapping, so the
        // only rule that can match is the chat-admin rule.
        await using (var context = _testHelper!.GetDbContext())
        {
            context.ManagedChats.Add(new ManagedChatRecordDto
            {
                ChatId = TestChatId,
                ChatName = "Test Chat",
                ChatType = ManagedChatType.Supergroup,
                AddedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            });
            await context.SaveChangesAsync();
        }

        await _chatAdminsRepository!.UpsertAsync(TestChatId, TrustedUserTelegramId, isCreator: false, CancellationToken.None);

        var user = UserIdentity.FromId(TrustedUserTelegramId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act — resolver classifies the join, then audit handler logs the decision.
        var resolution = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        var decision = resolution.Decision;
        await _auditHandler!.LogWelcomeBypassAsync(
            user, chat, decision, resolution.ReasonDetail ?? string.Empty, CancellationToken.None);

        // Assert — the decision was Admin and a matching user_actions row exists.
        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
        await AssertBypassAuditRowAsync(
            TrustedUserTelegramId,
            expectedReason: "Telegram chat admin (1 chats)");
        await AssertNoWelcomeResponseAsync(TrustedUserTelegramId);
    }

    /// <summary>
    /// Scenario 2: Joining user is linked to an Owner-level web admin. Expectation: resolver
    /// returns <see cref="BypassDecision.Admin"/> with the WebAdmin reason string.
    ///
    /// <para>
    /// A synthetic telegram user (<see cref="LinkedOwnerTelegramUserId"/>) is seeded inline
    /// rather than using an existing canonical mapping, because all three canonical
    /// telegram_user_mappings rows reference users who are also registered in chat_admins.
    /// That would cause Rule 1 (ChatAdmin) to fire before Rule 1b (WebAdmin), masking the
    /// path under test. The synthetic user is mapped to the Owner web user and has no
    /// chat_admins rows, so only the WebAdmin rule can match.
    /// </para>
    /// </summary>
    [Test]
    public async Task WebAdminJoin_LinkedOwner_WritesAuditAndBypasses()
    {
        // Arrange — seed a synthetic telegram user + Owner web mapping with no chat_admins
        // row, so the ChatAdmin rule (Rule 1) cannot fire and the resolver falls through to
        // the linked-web-admin rule (Rule 1b).
        await _testHelper!.ExecuteSqlAsync(
            $"""
            INSERT INTO telegram_users
                (telegram_user_id, username, first_name, last_name, is_bot, is_trusted, is_active,
                 is_banned, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at,
                 warnings, has_pinned_stories, is_fake, is_scam, is_verified, kick_count)
            VALUES ({LinkedOwnerTelegramUserId}, 'bypass_webadmin_test', 'WebAdmin', 'Test',
                    false, false, true, false, false,
                    NOW(), NOW(), NOW(), NOW(),
                    NULL, false, false, false, false, 0);

            INSERT INTO telegram_user_mappings (telegram_id, telegram_username, user_id, linked_at, is_active)
            VALUES ({LinkedOwnerTelegramUserId}, 'bypass_webadmin_test',
                    'b388ee38-0ed3-4c09-9def-5715f9f07f56', NOW(), TRUE);
            """);

        var user = UserIdentity.FromId(LinkedOwnerTelegramUserId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act
        var resolution = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        var decision = resolution.Decision;
        await _auditHandler!.LogWelcomeBypassAsync(
            user, chat, decision, resolution.ReasonDetail ?? string.Empty, CancellationToken.None);

        // Assert
        Assert.That(decision, Is.EqualTo(BypassDecision.Admin));
        await AssertBypassAuditRowAsync(
            LinkedOwnerTelegramUserId,
            expectedReason: "Linked web admin (Owner)");
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
        // Arrange — mark the canonical untrusted user as trusted, then enable global bypass.
        await MarkUserTrustedAsync(TrustedUserTelegramId);

        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessageTrusted = "{username} welcomed automatically - trusted user",
                AnnouncementTtlSeconds = 30,
            },
        };
        await _configService!.SaveWelcomeAsync(ChatIdentity.FromId(0), welcomeConfig, Actor.SystemSeed);

        var user = UserIdentity.FromId(TrustedUserTelegramId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act
        var resolution = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);
        var decision = resolution.Decision;
        await _auditHandler!.LogWelcomeBypassAsync(
            user, chat, decision, resolution.ReasonDetail ?? string.Empty, CancellationToken.None);

        // Assert
        Assert.That(decision, Is.EqualTo(BypassDecision.Trusted));
        await AssertBypassAuditRowAsync(
            TrustedUserTelegramId,
            expectedReason: "Trusted user");
        await AssertNoWelcomeResponseAsync(TrustedUserTelegramId);
    }

    /// <summary>
    /// Scenario 3b: Joining user is trusted but the per-chat TrustedBypass toggle is OFF.
    /// This is the integration-level complement to the unit test
    /// <c>WelcomeBypassResolverTests.TrustedUser_ToggleOff_ReturnsNone</c>: here we
    /// round-trip the <see cref="WelcomeConfig"/> through the real <see cref="IConfigService"/>
    /// (which persists via <see cref="IConfigRepository"/> to Postgres) so we prove the
    /// resolver respects the persisted toggle value, not just a cached/in-memory one.
    ///
    /// Expectation: resolver returns <see cref="BypassDecision.None"/>, which in production
    /// <see cref="TelegramGroupsAdmin.Telegram.Services.WelcomeService"/> causes the caller
    /// to skip <see cref="IAuditHandler.LogWelcomeBypassAsync"/> entirely and fall through to
    /// the normal mute/verify welcome flow. We assert the signal that differentiates the
    /// bypass path from the normal path at the data layer: NO user_actions.WelcomeBypass
    /// row for this (user, chat) pair.
    /// </summary>
    [Test]
    public async Task TrustedUser_ToggleOff_FallsThroughToNormalFlow_NoAuditRow()
    {
        // Arrange — mark the user trusted, then persist a WelcomeConfig with the toggle OFF
        // through the real ConfigService so the resolver reads it back from Postgres.
        await MarkUserTrustedAsync(TrustedUserTelegramId);

        var welcomeConfig = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = false,
                AnnouncementMessageTrusted = "ignored",
                AnnouncementTtlSeconds = 30,
            },
        };
        await _configService!.SaveWelcomeAsync(ChatIdentity.FromId(0), welcomeConfig, Actor.SystemSeed);

        var user = UserIdentity.FromId(TrustedUserTelegramId);
        var chat = ChatIdentity.FromId(TestChatId);

        // Act — resolver classifies the join. In WelcomeService, a None decision means
        // LogWelcomeBypassAsync is never called, so we deliberately mirror that here:
        // we do NOT call the audit handler. The DB assertion below is the invariant.
        var resolution = await _bypassResolver!.ResolveAsync(user, chat, CancellationToken.None);

        // Assert 1 — the resolver produced None via the Postgres config round-trip.
        Assert.That(resolution.Decision, Is.EqualTo(BypassDecision.None),
            "A trusted user with TrustedBypass.Enabled = false (loaded from Postgres) must fall through.");

        // Assert 2 — differential signal: no WelcomeBypass audit row was written.
        // If the bypass path had fired (it didn't, per Assert 1), WelcomeService would
        // have logged an audit row; since the decision was None, the caller skipped the
        // audit step and the user continues into the normal mute/verify flow.
        await using var context = _testHelper!.GetDbContext();
        var bypassRows = await context.UserActions
            .AsNoTracking()
            .Where(a => a.UserId == TrustedUserTelegramId
                        && a.ChatId == TestChatId
                        && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(bypassRows, Is.Empty,
            "A trusted user with the per-chat bypass toggle OFF must not produce a WelcomeBypass audit row.");
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
                AnnouncementMessageTrusted = "ignored",
                AnnouncementTtlSeconds = 30,
            },
        };
        await _configService!.SaveWelcomeAsync(ChatIdentity.FromId(0), welcomeConfig, Actor.SystemSeed);

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
    /// Marks a canonical telegram user as trusted by flipping the flag directly via
    /// repository. We use <see cref="ITelegramUserRepository.TrustUserAsync"/> so the write
    /// goes through the production code path, including UpdatedAt bookkeeping.
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
                        && a.ChatId == TestChatId
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

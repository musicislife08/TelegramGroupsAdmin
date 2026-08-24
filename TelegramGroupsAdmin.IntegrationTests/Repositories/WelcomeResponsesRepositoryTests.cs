using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for <see cref="WelcomeResponsesRepository.GetByUserAsync"/>.
///
/// The query's <c>GroupBy(ChatId).Select(g => g.OrderByDescending(CreatedAt).First())</c>
/// shape has no other precedent in this codebase (the only other GroupBy uses in
/// TelegramUserRepository are scalar Count() aggregates — a much simpler translation),
/// and WelcomeCleanupHandler runs inside a caller that swallows exceptions so cleanup can
/// never fail a ban that already landed. A future EF/Npgsql bump that breaks this
/// translation would otherwise fail silently in production, so this must be covered by a
/// real Postgres round-trip, not a mocked one.
///
/// Substrate: canonical template-clone, unmodified except for re-timing the 5 synthetic
/// welcome-flow anchor rows (999001..999005, all pinned to <see cref="WelcomeUserId"/> in
/// MainChat — see <c>TelegramGroupsAdmin.IntegrationTests/CLAUDE.md</c>) via
/// <c>GoldenDataset.Mutate(ctx).ShiftWelcomeResponseTimestamps(...)</c>, the same in-place
/// mutator <c>AnalyticsRepositoryTests</c> uses for this exact fixture. No canonical rows
/// are added or removed — <c>LoadCanonicalAsyncTests</c>' exactly-11-row assertion is
/// unaffected.
/// </summary>
[TestFixture]
public class WelcomeResponsesRepositoryTests
{
    // Synthetic welcome-flow target user (see TelegramGroupsAdmin.IntegrationTests/CLAUDE.md,
    // "Synthetic welcome-flow target"). Canonical welcome_responses 999001..999005 are pinned
    // to this user, all in MainChat, one per WelcomeResponseType.
    private const long WelcomeUserId = 9196379650113L;
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;

    // welcome_responses anchors (canonical id → welcome_message_id → response):
    //   999001 → 99001 → Pending
    //   999002 → 99002 → Accepted
    //   999003 → 99003 → Denied
    //   999004 → 99004 → Timeout
    //   999005 → 99005 → Left
    private const long WrId_Pending = 999001;
    private const long WrId_Accepted = 999002;
    private const long WrId_Denied = 999003;
    private const long WrId_Timeout = 999004;
    private const long WrId_Left = 999005;
    private const int PendingWelcomeMsgId = 99001;

    // Single-row-per-chat, single-row-per-user canonical anchor, untouched by this fixture's
    // re-timing (see CLAUDE.md "welcome_responses" table note — id 55 is a "non-MainChat
    // keeper" kept for chat-grouping shape diversity).
    private const long SoloChatId = -100017312732389L;
    private const long SoloUserId = 9991228601256L;
    private const int SoloWelcomeMsgId = 7788;

    // Deliberately not present in canonical.
    private const long UnknownUserId = 1L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IWelcomeResponsesRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        await using (var ctx = _testHelper.GetDbContext())
        {
            // Re-time the 5 synthetic anchors so the row with the LOWEST id (999001,
            // Pending) has the NEWEST created_at, and the row with the HIGHEST id (999005,
            // Left) has the OLDEST. This deliberately decouples "newest by CreatedAt" from
            // "highest by Id" / natural insertion order, so a test asserting the Pending
            // row wins can only pass if the query genuinely orders by CreatedAt DESC.
            await GoldenDataset.Mutate(ctx)
                .ShiftWelcomeResponseTimestamps(
                [
                    new TimestampShift(WrId_Left, TimeSpan.Zero),
                    new TimestampShift(WrId_Accepted, TimeSpan.FromMinutes(10)),
                    new TimestampShift(WrId_Denied, TimeSpan.FromMinutes(20)),
                    new TimestampShift(WrId_Timeout, TimeSpan.FromMinutes(30)),
                    new TimestampShift(WrId_Pending, TimeSpan.FromMinutes(40)),
                ])
                .ApplyAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IWelcomeResponsesRepository, WelcomeResponsesRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task GetByUserAsync_MultipleRowsSameChat_ReturnsOnlyTheNewestByCreatedAt()
    {
        var results = await _repository!.GetByUserAsync(WelcomeUserId);

        // 5 canonical rows all in MainChat collapse to exactly 1 — the GroupBy translated.
        Assert.That(results, Has.Count.EqualTo(1));

        var result = results[0];
        Assert.That(result.ChatId, Is.EqualTo(MainChatId));

        // The Pending row (lowest id, but re-timed to the newest CreatedAt) must win —
        // proves the query orders by CreatedAt, not by id or insertion order.
        Assert.That(result.WelcomeMessageId, Is.EqualTo(PendingWelcomeMsgId));
        Assert.That(result.Response, Is.EqualTo(WelcomeResponseType.Pending));
    }

    [Test]
    public async Task GetByUserAsync_SingleCanonicalRow_ReturnsMappedFieldsUnchanged()
    {
        var results = await _repository!.GetByUserAsync(SoloUserId);

        Assert.That(results, Has.Count.EqualTo(1));

        var result = results[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ChatId, Is.EqualTo(SoloChatId));
            Assert.That(result.UserId, Is.EqualTo(SoloUserId));
            Assert.That(result.Username, Is.EqualTo("Maxellzy"));
            Assert.That(result.WelcomeMessageId, Is.EqualTo(SoloWelcomeMsgId));
            Assert.That(result.Response, Is.EqualTo(WelcomeResponseType.Accepted));
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.DmFallback, Is.False);
        }
    }

    [Test]
    public async Task GetByUserAsync_UnknownUser_ReturnsEmpty()
    {
        var results = await _repository!.GetByUserAsync(UnknownUserId);

        Assert.That(results, Is.Empty);
    }
}

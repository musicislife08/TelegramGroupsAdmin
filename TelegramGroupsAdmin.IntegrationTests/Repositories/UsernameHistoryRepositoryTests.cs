using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests verifying UsernameHistoryRepository against a real PostgreSQL database.
/// Covers retrieval and field mapping, ordering, cascade delete, user isolation, and HasChangeSinceAsync.
///
/// All tests clone the golden_template. Canonical has 4 username_history rows total:
///   id=1  user_id=9726308613009
///   id=2  user_id=9875141377477  (prior name "QQQ", renamed spammer)
///   id=3  user_id=9032620986755
///   id=4  user_id=9095125964119
///
/// Tests read canonical history rows (anchors in GoldenDatasetConstants.UsernameHistory).
/// </summary>
[TestFixture]
[Category("Integration")]
public class UsernameHistoryRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    // Canonical telegram_user_id with NO existing username_history rows (ordering test).
    private const long FreshUser2 = 9960171136314L; // @sillywolf — "Early Spirits"

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        // Production data registration (pooled factory, retry-on-failure), as the app runs it.
        services.AddDataServices(_testHelper.ConnectionString);

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    // ============================================================================
    // Retrieve (canonical history rows, read-only)
    // ============================================================================

    [Test]
    public async Task GetByUserIdAsync_CanonicalRow_MapsEveryFieldIncludingNulls()
    {
        // History row 4: prior names "Tin Tun Min", no prior username.
        const long userId = GoldenDatasetConstants.UsernameHistory.NoPastUsernameUserId;
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        var results = await repo.GetByUserIdAsync(userId);

        Assert.That(results, Has.Count.EqualTo(1));
        var record = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.UserId, Is.EqualTo(userId));
            Assert.That(record.Username, Is.Null);
            Assert.That(record.FirstName, Is.EqualTo("Tin Tun"));
            Assert.That(record.LastName, Is.EqualTo("Min"));
            Assert.That(record.RecordedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        });
    }

    // ============================================================================
    // Ordering
    // ============================================================================

    [Test]
    public async Task GetByUserIdAsync_ReturnsDescendingByRecordedAt()
    {
        // FreshUser2 has no existing username_history rows in canonical.
        const long userId = FreshUser2;

        // Seed rows directly with explicit, deterministic timestamps to avoid any timing dependency.
        var olderTs = "2024-01-01 10:00:00+00";
        var newerTs = "2024-01-02 10:00:00+00";
        await _testHelper!.ExecuteSqlAsync($"""
            INSERT INTO username_history (user_id, username, first_name, last_name, recorded_at)
            VALUES ({userId}, 'first_username', 'First', 'User', '{olderTs}'),
                   ({userId}, 'second_username', 'Second', 'User', '{newerTs}')
            """);

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        var results = await repo.GetByUserIdAsync(userId);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Username, Is.EqualTo("second_username"),
            "Most recently recorded entry must appear first (descending order)");
        Assert.That(results[1].Username, Is.EqualTo("first_username"));
        Assert.That(results[0].RecordedAt, Is.GreaterThan(results[1].RecordedAt));
    }

    // ============================================================================
    // Cascade Delete
    // ============================================================================

    [Test]
    public async Task CascadeDelete_RemovesHistoryWhenUserDeleted()
    {
        // History row 1's owner. Deleting the user (in this test's clone) is the act under test.
        const long userId = GoldenDatasetConstants.UsernameHistory.CascadeDeleteUserId;

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            var before = await repo.GetByUserIdAsync(userId);
            Assert.That(before, Has.Count.EqualTo(1), "History must exist before user deletion");
        }

        await _testHelper!.ExecuteSqlAsync(
            $"DELETE FROM telegram_users WHERE telegram_user_id = {userId}");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            var after = await repo.GetByUserIdAsync(userId);
            Assert.That(after, Is.Empty,
                "Cascade delete must remove username_history rows when parent user is deleted");
        }
    }

    // ============================================================================
    // User Isolation
    // ============================================================================

    [Test]
    public async Task GetByUserIdAsync_DoesNotReturnOtherUsersHistory()
    {
        // Two canonical owners of one history row each (rows 4 and 1).
        const long userAId = GoldenDatasetConstants.UsernameHistory.NoPastUsernameUserId;
        const long userBId = GoldenDatasetConstants.UsernameHistory.CascadeDeleteUserId;
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        var userAHistory = await repo.GetByUserIdAsync(userAId);
        var userBHistory = await repo.GetByUserIdAsync(userBId);

        Assert.Multiple(() =>
        {
            Assert.That(userAHistory, Has.Count.EqualTo(1));
            Assert.That(userAHistory[0].UserId, Is.EqualTo(userAId));
            Assert.That(userAHistory[0].FirstName, Is.EqualTo("Tin Tun"));

            Assert.That(userBHistory, Has.Count.EqualTo(1));
            Assert.That(userBHistory[0].UserId, Is.EqualTo(userBId));
        });
    }

    // ============================================================================
    // HasChangeSinceAsync (canonical history row 2, read-only)
    // ============================================================================

    private async Task<DateTimeOffset> PastFirstNameRecordedAtAsync()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.UsernameHistory.AsNoTracking()
            .SingleAsync(h => h.UserId == GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId);
        Assert.That(row.FirstName, Is.EqualTo(GoldenDatasetConstants.UsernameHistory.PastFirstName));
        return row.RecordedAt;
    }

    [Test]
    public async Task HasChangeSinceAsync_SinceBeforeTheRename_IsTrue()
    {
        var recordedAt = await PastFirstNameRecordedAtAsync();
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        var changed = await repo.HasChangeSinceAsync(
            GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId, recordedAt.AddSeconds(-1));

        Assert.That(changed, Is.True);
    }

    [Test]
    public async Task HasChangeSinceAsync_SinceAtOrAfterTheRename_IsFalse()
    {
        var recordedAt = await PastFirstNameRecordedAtAsync();
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
        const long userId = GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId;

        Assert.That(await repo.HasChangeSinceAsync(userId, recordedAt), Is.False);
        Assert.That(await repo.HasChangeSinceAsync(userId, recordedAt.AddSeconds(1)), Is.False);
    }

    [Test]
    public async Task HasChangeSinceAsync_NeverScanned_CountsAnyRename()
    {
        await PastFirstNameRecordedAtAsync();
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        var changed = await repo.HasChangeSinceAsync(GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId, since: null);

        Assert.That(changed, Is.True);
    }

    [Test]
    public async Task HasChangeSinceAsync_UserWithNoHistory_IsFalse()
    {
        const long userId = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using (var ctx = _testHelper!.GetDbContext())
            Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == userId), Is.Zero);
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        Assert.That(await repo.HasChangeSinceAsync(userId, since: null), Is.False);
    }
}

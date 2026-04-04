using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests verifying UsernameHistoryRepository against a real PostgreSQL database.
/// Covers insert/retrieve round-trips, ordering, cascade delete, user isolation, and null handling.
/// </summary>
[TestFixture]
[Category("Integration")]
public class UsernameHistoryRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
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
    // Insert and Retrieve
    // ============================================================================

    [Test]
    public async Task InsertAsync_And_GetByUserIdAsync_RoundTrips()
    {
        const long userId = 100001L;
        await SeedUserAsync(userId);

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        await repo.InsertAsync(userId, "old_username", "OldFirst", "OldLast");

        var results = await repo.GetByUserIdAsync(userId);

        Assert.That(results, Has.Count.EqualTo(1));
        var record = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.UserId, Is.EqualTo(userId));
            Assert.That(record.Username, Is.EqualTo("old_username"));
            Assert.That(record.FirstName, Is.EqualTo("OldFirst"));
            Assert.That(record.LastName, Is.EqualTo("OldLast"));
            Assert.That(record.RecordedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        });
    }

    // ============================================================================
    // Ordering
    // ============================================================================

    [Test]
    public async Task GetByUserIdAsync_ReturnsDescendingByRecordedAt()
    {
        const long userId = 100002L;
        await SeedUserAsync(userId);

        // Seed rows directly with explicit, deterministic timestamps to avoid any timing dependency
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
        const long userId = 100003L;
        await SeedUserAsync(userId);

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await repo.InsertAsync(userId, "username_to_delete", "ToDelete", "User");
        }

        // Verify history exists before delete
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            var before = await repo.GetByUserIdAsync(userId);
            Assert.That(before, Has.Count.EqualTo(1), "History must exist before user deletion");
        }

        // Delete the parent user directly via SQL
        await _testHelper!.ExecuteSqlAsync(
            $"DELETE FROM telegram_users WHERE telegram_user_id = {userId}");

        // History must be gone due to CASCADE DELETE
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
        const long userAId = 100004L;
        const long userBId = 100005L;
        await SeedUserAsync(userAId, username: "user_a");
        await SeedUserAsync(userBId, username: "user_b");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await repo.InsertAsync(userAId, "user_a_old", "UserA", "Old");
            await repo.InsertAsync(userBId, "user_b_old", "UserB", "Old");
        }

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

            var userAHistory = await repo.GetByUserIdAsync(userAId);
            var userBHistory = await repo.GetByUserIdAsync(userBId);

            Assert.Multiple(() =>
            {
                Assert.That(userAHistory, Has.Count.EqualTo(1));
                Assert.That(userAHistory[0].UserId, Is.EqualTo(userAId));
                Assert.That(userAHistory[0].Username, Is.EqualTo("user_a_old"));

                Assert.That(userBHistory, Has.Count.EqualTo(1));
                Assert.That(userBHistory[0].UserId, Is.EqualTo(userBId));
                Assert.That(userBHistory[0].Username, Is.EqualTo("user_b_old"));
            });
        }
    }

    // ============================================================================
    // Null Fields
    // ============================================================================

    [Test]
    public async Task InsertAsync_HandlesNullFields()
    {
        const long userId = 100006L;
        await SeedUserAsync(userId);

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();

        Assert.DoesNotThrowAsync(async () =>
            await repo.InsertAsync(userId, username: null, firstName: null, lastName: null));

        var results = await repo.GetByUserIdAsync(userId);

        Assert.That(results, Has.Count.EqualTo(1));
        var record = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.UserId, Is.EqualTo(userId));
            Assert.That(record.Username, Is.Null);
            Assert.That(record.FirstName, Is.Null);
            Assert.That(record.LastName, Is.Null);
        });
    }

    // ============================================================================
    // Helper
    // ============================================================================

    private async Task SeedUserAsync(long userId, string? username = "testuser", string? firstName = "Test", string? lastName = "User")
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var now = DateTimeOffset.UtcNow;
        await userRepo.UpsertAsync(new TelegramUser(
            TelegramUserId: userId,
            Username: username,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            IsBanned: false,
            KickCount: 0,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now,
            IsActive: false));
    }
}

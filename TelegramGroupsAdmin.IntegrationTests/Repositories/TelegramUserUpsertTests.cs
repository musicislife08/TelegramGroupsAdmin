using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests validating TelegramUserRepository.UpsertAsync semantics:
/// - New user creation
/// - Existing user field updates
/// - Admin-controlled field preservation (is_trusted, bot_dm_enabled not overwritten)
/// - Concurrent upserts produce exactly one row (DATA-02 verification)
///
/// Uses PostgreSQL ON CONFLICT (telegram_user_id) DO UPDATE SET — these tests prove
/// the atomic upsert prevents duplicate key violations under concurrent load.
/// </summary>
[TestFixture]
[Category("Integration")]
public class TelegramUserUpsertTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    // ============================================================================
    // Basic Upsert Semantics
    // ============================================================================

    [Test]
    public async Task UpsertAsync_NewUser_CreatesRecord()
    {
        // INSERT path — 999999L is outside canonical range [9_000_000_000_000, 10_000_000_000_000)
        const long newUserId = 999_999L;
        var user = BuildUser(
            telegramUserId: newUserId,
            username: "new_upsert_user",
            firstName: "New",
            lastName: "User");

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        await repo.UpsertAsync(user, cancellationToken: CancellationToken.None);

        var fetched = await repo.GetByTelegramIdAsync(newUserId, cancellationToken: CancellationToken.None);

        Assert.That(fetched, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fetched!.TelegramUserId, Is.EqualTo(newUserId));
            Assert.That(fetched.Username, Is.EqualTo("new_upsert_user"));
            Assert.That(fetched.FirstName, Is.EqualTo("New"));
            Assert.That(fetched.LastName, Is.EqualTo("User"));
        });
    }

    [Test]
    public async Task UpsertAsync_ExistingUser_UpdatesFields()
    {
        // UPDATE path — 9921676191756 is the top ham author (@unhelpfulgrab) in canonical
        const long existingUserId = 9_921_676_191_756L;
        var updatedUser = BuildUser(
            telegramUserId: existingUserId,
            username: "updated_username",
            firstName: "UpdatedFirst",
            lastName: "UpdatedLast");

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        await repo.UpsertAsync(updatedUser, cancellationToken: CancellationToken.None);

        var fetched = await repo.GetByTelegramIdAsync(existingUserId, cancellationToken: CancellationToken.None);

        Assert.That(fetched, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fetched!.Username, Is.EqualTo("updated_username"));
            Assert.That(fetched.FirstName, Is.EqualTo("UpdatedFirst"));
            Assert.That(fetched.LastName, Is.EqualTo("UpdatedLast"));
            // ON CONFLICT sets is_active = true
            Assert.That(fetched.IsActive, Is.True);
        });
    }

    [Test]
    public async Task UpsertAsync_ExistingUser_DoesNotOverwriteTrustOrDm()
    {
        // UPDATE path — 9921676191756 is the top ham author (@unhelpfulgrab, "Squeak Degree") in canonical
        const long existingUserId = 9_921_676_191_756L;

        // Directly set is_trusted and bot_dm_enabled via raw SQL to simulate admin-controlled state
        await _testHelper!.ExecuteSqlAsync(
            $"UPDATE telegram_users SET is_trusted = true, bot_dm_enabled = true WHERE telegram_user_id = {existingUserId}");

        // Upsert the same user — the ON CONFLICT clause must NOT overwrite these fields
        var user = BuildUser(
            telegramUserId: existingUserId,
            username: "unhelpfulgrab",
            firstName: "Squeak",
            lastName: "Degree");

        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        await repo.UpsertAsync(user, cancellationToken: CancellationToken.None);

        var fetched = await repo.GetByTelegramIdAsync(existingUserId, cancellationToken: CancellationToken.None);

        Assert.That(fetched, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(fetched!.IsTrusted, Is.True,
                "is_trusted must not be overwritten by UpsertAsync — admin-controlled field");
            Assert.That(fetched.BotDmEnabled, Is.True,
                "bot_dm_enabled must not be overwritten by UpsertAsync — admin-controlled field");
        });
    }

    // ============================================================================
    // Concurrent Upsert Safety (DATA-02 core test)
    // ============================================================================

    [Test]
    public async Task UpsertAsync_ConcurrentSameUser_ProducesOneRow()
    {
        // INSERT path — 888888L is outside canonical range [9_000_000_000_000, 10_000_000_000_000)
        const long concurrentUserId = 888_888L;
        var user = BuildUser(
            telegramUserId: concurrentUserId,
            username: "concurrent_user",
            firstName: "Concurrent",
            lastName: "Test");

        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var loggerFactory = _serviceProvider!.GetRequiredService<ILoggerFactory>();

        // Launch 10 concurrent UpsertAsync calls — each uses a separate DbContext
        // to simulate real-world concurrent requests from different scopes
        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var repo = new TelegramUserRepository(contextFactory, loggerFactory.CreateLogger<TelegramUserRepository>());
            return repo.UpsertAsync(user, cancellationToken: CancellationToken.None);
        }).ToArray();

        // All should complete without exception (no duplicate key violation)
        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));

        // Verify exactly one row was created
        var rowCount = await _testHelper!.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM telegram_users WHERE telegram_user_id = {concurrentUserId}");

        Assert.That(rowCount, Is.EqualTo(1L),
            "Concurrent upserts must produce exactly one row — ON CONFLICT handles races atomically");
    }

    // ============================================================================
    // Helper
    // ============================================================================

    private static UiModels.TelegramUser BuildUser(
        long telegramUserId,
        string? username,
        string? firstName,
        string? lastName)
    {
        var now = DateTimeOffset.UtcNow;
        return new UiModels.TelegramUser(
            TelegramUserId: telegramUserId,
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
            IsActive: false);
    }
}

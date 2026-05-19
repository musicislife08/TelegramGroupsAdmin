using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for TelegramUserRepository.GetOrCreateAsync method.
///
/// Validates the Phase 2 (double-ban race condition fix) where GetOrCreateAsync
/// replaced duplicated get-then-create patterns in WelcomeService and BotChatService.
/// These tests run against real PostgreSQL to verify actual DB behavior including
/// unique constraints and default values.
///
/// Test Infrastructure:
/// - Shared PostgreSQL container (PostgresFixture) — started once per test run
/// - Unique database per test cloned from golden_template — perfect isolation
/// - Canonical dataset available per test — consistent starting state
/// </summary>
[TestFixture]
public class TelegramUserRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITelegramUserRepository? _repository;

    // Canonical anchor IDs
    // Top MainChat ham author (@unhelpfulgrab, "Squeak Degree", is_banned=false)
    private const long TopHamAuthorId = 9921676191756L;
    private const string TopHamAuthorUsername = "unhelpfulgrab";
    private const string TopHamAuthorFirstName = "Squeak";

    // Second active MainChat ham author (@sillywolf, is_banned=false)
    private const long SecondHamAuthorId = 9960171136314L;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    private async Task SeedActiveUserAsync(long userId, string? username = null, string? firstName = null, string? lastName = null)
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
            IsActive: true));
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Search with Username History Tests

    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastUsername()
    {
        // Seed a synthetic user (ID below canonical range) with current username "new_name"
        const long userId = 200001L;
        await SeedActiveUserAsync(userId, username: "new_name", firstName: "Current", lastName: "User");

        // Insert a history entry recording the old username
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "old_name", "Current", "User");
        }

        // Search by old username — should find the user via username_history join
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 10,
            searchText: "old_name", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.That(totalCount, Is.EqualTo(1));
        Assert.That(items[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastFirstName()
    {
        // Seed a synthetic user (ID below canonical range) with current first name "NewFirst"
        const long userId = 200002L;
        await SeedActiveUserAsync(userId, username: "history_user", firstName: "NewFirst", lastName: "User");

        // Insert a history entry recording the old first name
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "history_user", "OldFirst", "User");
        }

        // Search by old first name — should find the user via username_history join
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 10,
            searchText: "OldFirst", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.That(totalCount, Is.EqualTo(1));
        Assert.That(items[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task GetUserTabCountsAsync_IncludesUsersMatchedByPastNames()
    {
        // Seed a synthetic user (ID below canonical range) with a current username that won't match the search
        const long userId = 200003L;
        await SeedActiveUserAsync(userId, username: "current_handle", firstName: "Present", lastName: "User");

        // Insert a history entry with a distinctive old username
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "historic_handle", "Present", "User");
        }

        // Tab counts filtered by the old name must include the user in the active count
        var counts = await _repository!.GetUserTabCountsAsync(
            chatIds: null, searchText: "historic_handle");

        Assert.That(counts.ActiveCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task SearchByNameAsync_MatchesPastUsername()
    {
        const long userId = 200010L;
        await SeedActiveUserAsync(userId, username: "current_handle", firstName: "Search", lastName: "Test");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "legacy_handle", "Search", "Test");
        }

        var results = await _repository!.SearchByNameAsync("legacy_handle", limit: 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task SearchByNameAsync_MatchesPastFirstName()
    {
        const long userId = 200011L;
        await SeedActiveUserAsync(userId, username: "name_search_user", firstName: "CurrentFirst", lastName: "Test");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "name_search_user", "FormerFirst", "Test");
        }

        var results = await _repository!.SearchByNameAsync("FormerFirst", limit: 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchByPastName_DoesNotReturnDifferentUser()
    {
        const long userA = 200020L;
        const long userB = 200021L;
        await SeedActiveUserAsync(userA, username: "user_a", firstName: "Alice", lastName: "Smith");
        await SeedActiveUserAsync(userB, username: "user_b", firstName: "Bob", lastName: "Jones");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userA, "unique_old_handle", "Alice", "Smith");
        }

        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 10,
            searchText: "unique_old_handle", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.Multiple(() =>
        {
            Assert.That(totalCount, Is.EqualTo(1));
            Assert.That(items[0].TelegramUserId, Is.EqualTo(userA));
        });
    }

    [Test]
    public async Task SearchByNameAsync_DoesNotReturnUserWithoutMatchingHistory()
    {
        const long userA = 200030L;
        const long userB = 200031L;
        await SeedActiveUserAsync(userA, username: "iso_user_a", firstName: "Ava", lastName: "Test");
        await SeedActiveUserAsync(userB, username: "iso_user_b", firstName: "Ben", lastName: "Test");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userA, "distinctive_old_name", "Ava", "Test");
        }

        var results = await _repository!.SearchByNameAsync("distinctive_old_name", limit: 10);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].TelegramUserId, Is.EqualTo(userA));
        });
    }

    #endregion

    #region GetOrCreateAsync Tests

    [Test]
    public async Task GetOrCreateAsync_NewUser_CreatesAndReturns()
    {
        // Arrange — use an ID that doesn't exist in canonical (canonical IDs are 13-digit numbers in the 9T range)
        const long newUserId = 999999;

        // Act
        var result = await _repository!.GetOrCreateAsync(
            new UserIdentity(newUserId, FirstName: "New", LastName: "Person", Username: "new_user"), isBot: false);

        // Assert — verify the returned object has correct defaults
        Assert.Multiple(() =>
        {
            Assert.That(result.TelegramUserId, Is.EqualTo(newUserId));
            Assert.That(result.Username, Is.EqualTo("new_user"));
            Assert.That(result.FirstName, Is.EqualTo("New"));
            Assert.That(result.LastName, Is.EqualTo("Person"));
            Assert.That(result.IsBot, Is.False);
            Assert.That(result.IsBanned, Is.False);
            Assert.That(result.IsTrusted, Is.False);
            Assert.That(result.IsActive, Is.False, "New users start inactive until welcome flow completes");
            Assert.That(result.BotDmEnabled, Is.False);
        });

        // Verify persisted to DB
        var fetched = await _repository.GetByTelegramIdAsync(newUserId);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.TelegramUserId, Is.EqualTo(newUserId));
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingUser_ReturnsWithoutCreating()
    {
        // Arrange — canonical anchor exists in golden template (top ham author, @unhelpfulgrab)
        const long existingUserId = TopHamAuthorId;

        // Act
        var result = await _repository!.GetOrCreateAsync(
            new UserIdentity(existingUserId, FirstName: "Different", LastName: "Name", Username: "different_username"), isBot: false);

        // Assert — should return the DB state, not the parameters we passed
        Assert.Multiple(() =>
        {
            Assert.That(result.TelegramUserId, Is.EqualTo(existingUserId));
            Assert.That(result.Username, Is.EqualTo(TopHamAuthorUsername),
                "Should return existing DB username, not the one we passed");
            Assert.That(result.FirstName, Is.EqualTo(TopHamAuthorFirstName));
        });
    }

    [Test]
    public async Task GetOrCreateAsync_ExistingBannedUser_ReturnsBannedState()
    {
        // Arrange — ban a canonical anchor first, then call GetOrCreateAsync
        const long userId = SecondHamAuthorId;
        await _repository!.SetBanStatusAsync(userId, isBanned: true);

        // Act
        var result = await _repository.GetOrCreateAsync(
            new UserIdentity(userId, FirstName: "Alice", LastName: "Anderson", Username: "alice_user"), isBot: false);

        // Assert — critical for the WelcomeService early-out path
        Assert.That(result.IsBanned, Is.True,
            "GetOrCreateAsync must return current DB ban state for WelcomeService to detect and skip banned users");
    }

    [Test]
    public async Task GetOrCreateAsync_SystemUser_AutoTrusted()
    {
        // Arrange — use the Telegram service account ID (777000)
        // 777000 does not exist in canonical, so this will create it
        const long systemUserId = TelegramConstants.ServiceAccountUserId;

        // Act
        var result = await _repository!.GetOrCreateAsync(
            new UserIdentity(systemUserId, FirstName: "Telegram", LastName: null, Username: null), isBot: false);

        // Assert — system users get automatic trust
        Assert.Multiple(() =>
        {
            Assert.That(result.TelegramUserId, Is.EqualTo(systemUserId));
            Assert.That(result.IsTrusted, Is.True,
                "System users (777000, 1087968824, etc.) should be auto-trusted via TelegramConstants.IsSystemUser()");
            Assert.That(result.IsBanned, Is.False);
            Assert.That(result.IsActive, Is.False);
        });
    }

    #endregion
}

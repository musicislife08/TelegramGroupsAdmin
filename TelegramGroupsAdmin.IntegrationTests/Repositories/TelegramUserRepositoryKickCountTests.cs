using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for <see cref="ITelegramUserRepository.GetKickCountAsync"/> and
/// <see cref="ITelegramUserRepository.IncrementKickCountAsync"/>.
///
/// Each test gets a fresh isolated PostgreSQL database cloned from the golden template.
/// The shared container is managed by <see cref="PostgresFixture"/> at assembly level.
/// </summary>
[TestFixture]
public class TelegramUserRepositoryKickCountTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private ITelegramUserRepository? _repository;

    // Canonical anchor IDs — kick_count = 0 in golden dataset.
    // Top MainChat ham author (@unhelpfulgrab, 24 messages, is_banned=false).
    private const long TopHamAuthorId = 9921676191756L;
    // Second active MainChat ham author (@sillywolf, 23 messages, is_banned=false).
    private const long SecondHamAuthorId = 9960171136314L;
    // Heavily-banned spammer (@lazinessunsheathe, 4 Ban actions) — kick_count = 0.
    private const long HeavilyBannedSpammerId = 9971261287520L;

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

        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    // ============================================================================
    // GetKickCountAsync Tests
    // ============================================================================

    [Test]
    public async Task GetKickCountAsync_NewUser_ReturnsZero()
    {
        // Arrange — canonical anchor has kick_count = 0 in the golden dataset
        const long userId = TopHamAuthorId;

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetKickCountAsync_AfterIncrements_MatchesCount()
    {
        // Arrange — canonical anchor has kick_count = 0 in the golden dataset
        const long userId = SecondHamAuthorId;

        await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userId));
        await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userId));

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(2));
    }

    // ============================================================================
    // IncrementKickCountAsync Tests
    // ============================================================================

    [Test]
    public async Task IncrementKickCountAsync_FirstKick_ReturnsRowsAffected()
    {
        // Arrange — canonical anchor exists in the golden dataset
        const long userId = HeavilyBannedSpammerId;

        // Act — returns rows affected (1 = success)
        var result = await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userId));

        // Assert
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public async Task IncrementKickCountAsync_MultipleKicks_EachReturnsRowsAffected()
    {
        // Arrange — canonical anchor exists in the golden dataset; kick_count starts at 0
        const long userId = TopHamAuthorId;

        // Act — each call returns rows affected (always 1 for existing user)
        var identity = UserIdentity.FromId(userId);
        var result1 = await _repository!.IncrementKickCountAsync(identity);
        var result2 = await _repository!.IncrementKickCountAsync(identity);
        var result3 = await _repository!.IncrementKickCountAsync(identity);

        // Assert — rows affected is always 1; actual count verified via GetKickCountAsync
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result1, Is.EqualTo(1));
            Assert.That(result2, Is.EqualTo(1));
            Assert.That(result3, Is.EqualTo(1));
        }

        // Verify count accumulated correctly
        var kickCount = await _repository!.GetKickCountAsync(userId);
        Assert.That(kickCount, Is.EqualTo(3));
    }

    [Test]
    public async Task IncrementKickCountAsync_UnknownUser_ReturnsZero()
    {
        // Arrange — no user with this ID in canonical (canonical IDs are 13-digit numbers in the 9T range)
        const long userId = 999_999L;

        // Act
        var result = await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userId));

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task GetKickCountAsync_UnknownUser_ReturnsZero()
    {
        // Arrange — no user with this ID in canonical (canonical IDs are 13-digit numbers in the 9T range)
        const long userId = 999_998L;

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(0));
    }

    [Test]
    public async Task IncrementKickCountAsync_DifferentUsers_IndependentCounts()
    {
        // Arrange — two canonical anchors, both with kick_count = 0 in the golden dataset
        const long userA = TopHamAuthorId;
        const long userB = SecondHamAuthorId;

        await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userA));
        await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userA));
        await _repository!.IncrementKickCountAsync(UserIdentity.FromId(userB));

        // Act
        var countA = await _repository!.GetKickCountAsync(userA);
        var countB = await _repository!.GetKickCountAsync(userB);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(countA, Is.EqualTo(2), "User A should have 2 kicks");
            Assert.That(countB, Is.EqualTo(1), "User B should have 1 kick");
        }
    }
}

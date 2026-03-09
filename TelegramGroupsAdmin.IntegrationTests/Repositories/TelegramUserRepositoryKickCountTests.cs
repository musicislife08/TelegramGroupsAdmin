using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for <see cref="ITelegramUserRepository.GetKickCountAsync"/> and
/// <see cref="ITelegramUserRepository.IncrementKickCountAsync"/>.
///
/// Each test gets a fresh isolated PostgreSQL database (via MigrationTestHelper).
/// The shared container is managed by <see cref="PostgresFixture"/> at assembly level.
/// </summary>
[TestFixture]
public class TelegramUserRepositoryKickCountTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private ITelegramUserRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

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
    // Helper Methods
    // ============================================================================

    /// <summary>
    /// Inserts a minimal telegram_users row directly via EF Core.
    /// The kick_count column defaults to 0 unless overridden.
    /// </summary>
    private async Task CreateTestUserAsync(long telegramUserId)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var now = DateTimeOffset.UtcNow;
        context.TelegramUsers.Add(new TelegramUserDto
        {
            TelegramUserId = telegramUserId,
            FirstName = "Test",
            LastName = "User",
            IsBot = false,
            FirstSeenAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync();
    }

    // ============================================================================
    // GetKickCountAsync Tests
    // ============================================================================

    [Test]
    public async Task GetKickCountAsync_NewUser_ReturnsZero()
    {
        // Arrange
        const long userId = 100_001L;
        await CreateTestUserAsync(userId);

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetKickCountAsync_AfterIncrements_MatchesCount()
    {
        // Arrange
        const long userId = 100_002L;
        await CreateTestUserAsync(userId);

        await _repository!.IncrementKickCountAsync(userId);
        await _repository!.IncrementKickCountAsync(userId);

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(2));
    }

    // ============================================================================
    // IncrementKickCountAsync Tests
    // ============================================================================

    [Test]
    public async Task IncrementKickCountAsync_FirstKick_ReturnsOne()
    {
        // Arrange
        const long userId = 100_003L;
        await CreateTestUserAsync(userId);

        // Act
        var result = await _repository!.IncrementKickCountAsync(userId);

        // Assert
        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public async Task IncrementKickCountAsync_MultipleKicks_AccumulatesCorrectly()
    {
        // Arrange
        const long userId = 100_004L;
        await CreateTestUserAsync(userId);

        // Act
        await _repository!.IncrementKickCountAsync(userId);
        await _repository!.IncrementKickCountAsync(userId);
        var result = await _repository!.IncrementKickCountAsync(userId);

        // Assert
        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public async Task IncrementKickCountAsync_UnknownUser_ReturnsZero()
    {
        // Arrange — no user created, so the user does not exist in the database
        const long userId = 999_999L;

        // Act
        var result = await _repository!.IncrementKickCountAsync(userId);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task GetKickCountAsync_UnknownUser_ReturnsZero()
    {
        // Arrange — no user created, so user does not exist in the database
        const long userId = 999_998L;

        // Act
        var kickCount = await _repository!.GetKickCountAsync(userId);

        // Assert
        Assert.That(kickCount, Is.EqualTo(0));
    }

    [Test]
    public async Task IncrementKickCountAsync_DifferentUsers_IndependentCounts()
    {
        // Arrange
        const long userA = 100_005L;
        const long userB = 100_006L;
        await CreateTestUserAsync(userA);
        await CreateTestUserAsync(userB);

        await _repository!.IncrementKickCountAsync(userA);
        await _repository!.IncrementKickCountAsync(userA);
        await _repository!.IncrementKickCountAsync(userB);

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

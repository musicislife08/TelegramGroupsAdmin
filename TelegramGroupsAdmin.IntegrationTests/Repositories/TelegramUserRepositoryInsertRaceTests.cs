using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for the concurrent-insert race in TelegramUserRepository.GetOrUpdateAsync
/// (the only path that creates a telegram_users row for a newly seen user). Empty template: the
/// write under test is the assertion subject.
///
/// Verifies that concurrent calls with the same Telegram user ID do not throw
/// DbUpdateException (unique violation on telegram_user_id) and that exactly one
/// row is created in the database.
/// </summary>
[TestFixture]
public class TelegramUserRepositoryInsertRaceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

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
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task GetOrUpdateAsync_ConcurrentFirstObservationsSameId_BothSucceed_OneRowExists()
    {
        // Arrange — use two separate scopes to simulate two independent callers
        await using var scope1 = _serviceProvider!.CreateAsyncScope();
        await using var scope2 = _serviceProvider!.CreateAsyncScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();

        const long id = 9_876_543_210L;
        var observed = new ObservedUser(id, "racer", null, "race", IsBot: false,
            ObservationSource.BotUpdate, DateTimeOffset.UtcNow);
        var context = new ProfileChangeContext(Chat: null, MessageId: null);

        // Act — fire both concurrently
        var task1 = Task.Run(() => repo1.GetOrUpdateAsync(observed, context, CancellationToken.None));
        var task2 = Task.Run(() => repo2.GetOrUpdateAsync(observed, context, CancellationToken.None));

        var results = await Task.WhenAll(task1, task2);

        // Assert — both callers get back the user record
        Assert.That(results[0].User.TelegramUserId, Is.EqualTo(id));
        Assert.That(results[1].User.TelegramUserId, Is.EqualTo(id));

        // Only one row should exist in the database
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var count = await ctx.TelegramUsers.CountAsync(u => u.TelegramUserId == id);
        Assert.That(count, Is.EqualTo(1));
    }
}

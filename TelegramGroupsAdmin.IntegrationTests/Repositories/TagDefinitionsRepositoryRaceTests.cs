using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for TagDefinitionsRepository concurrent race conditions.
///
/// Verifies that concurrent calls to CreateAsync and IncrementUsageAsync do not throw
/// DbUpdateException (unique violation on tag_name) and that the resulting database
/// state is correct (one row, correct count).
///
/// Also verifies the DecrementUsageAsync invariants against canonical golden rows:
/// the no-lost-update guarantee (N concurrent decrements from a seeded high-count tag,
/// 'power-user' at usage_count = 20, land exactly on 0) and clamp-at-zero (decrementing
/// the real zero-count canonical tag, 'tester', never drives the count negative).
/// </summary>
[TestFixture]
public class TagDefinitionsRepositoryRaceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        // Golden template: decrement tests assert against canonical tag_definitions rows
        // ('power-user', 'tester'). The Create/Increment tests use unique Guid tag names,
        // so the presence of canonical rows does not affect them.
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

        services.AddScoped<ITagDefinitionsRepository, TagDefinitionsRepository>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    // ============================================================================
    // CreateAsync Race Tests
    // ============================================================================

    [Test]
    public async Task CreateAsync_ConcurrentCallsSameTag_BothSucceed_OneRowExists()
    {
        // Arrange — two separate scopes to simulate two independent callers
        await using var scope1 = _serviceProvider!.CreateAsyncScope();
        await using var scope2 = _serviceProvider!.CreateAsyncScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var tagName = $"race-tag-{Guid.NewGuid():N}";

        // Act — fire both concurrently
        var task1 = Task.Run(() => repo1.CreateAsync(tagName, TagColor.Primary, CancellationToken.None));
        var task2 = Task.Run(() => repo2.CreateAsync(tagName, TagColor.Primary, CancellationToken.None));

        var results = await Task.WhenAll(task1, task2);

        // Assert — both callers get back the tag definition
        Assert.That(results[0].TagName, Is.EqualTo(tagName));
        Assert.That(results[1].TagName, Is.EqualTo(tagName));

        // Only one row should exist in the database
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var count = await ctx.TagDefinitions.CountAsync(t => t.TagName == tagName);
        Assert.That(count, Is.EqualTo(1));
    }

    // ============================================================================
    // IncrementUsageAsync Race Tests
    // ============================================================================

    [Test]
    public async Task IncrementUsageAsync_ConcurrentCalls_FinalCountEqualsCallCount()
    {
        // Arrange — pre-create with usage_count = 0 via CreateAsync
        await using var setupScope = _serviceProvider!.CreateAsyncScope();
        var setupRepo = setupScope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var tagName = $"inc-race-{Guid.NewGuid():N}";
        await setupRepo.CreateAsync(tagName, TagColor.Primary, CancellationToken.None);

        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();

        const int concurrentCalls = 20;
        var tasks = Enumerable.Range(0, concurrentCalls)
            .Select(_ => Task.Run(async () =>
            {
                await using var scope = _serviceProvider!.CreateAsyncScope();
                var scopedRepo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
                await scopedRepo.IncrementUsageAsync(tagName, CancellationToken.None);
            }))
            .ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert — final usage_count must equal total number of concurrent increments
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(concurrentCalls));
    }

    [Test]
    public async Task IncrementUsageAsync_ConcurrentCallsOnNewTag_BothSucceed_OneRowFinalCountIsCallCount()
    {
        // Arrange — tag does NOT pre-exist; both concurrent calls must auto-create + increment
        await using var scope1 = _serviceProvider!.CreateAsyncScope();
        await using var scope2 = _serviceProvider!.CreateAsyncScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var tagName = $"new-inc-race-{Guid.NewGuid():N}";

        // Act
        var task1 = Task.Run(() => repo1.IncrementUsageAsync(tagName, CancellationToken.None));
        var task2 = Task.Run(() => repo2.IncrementUsageAsync(tagName, CancellationToken.None));
        await Task.WhenAll(task1, task2);

        // Assert
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(2));

        var count = await ctx.TagDefinitions.CountAsync(t => t.TagName == tagName);
        Assert.That(count, Is.EqualTo(1));
    }

    // ============================================================================
    // DecrementUsageAsync Race Tests
    // ============================================================================

    [Test]
    public async Task DecrementUsageAsync_ConcurrentCalls_NoLostUpdates()
    {
        // Asserts the no-lost-update invariant against a known canonical row.
        // 'power-user' is seeded in canonical with usage_count = 20 specifically so the
        // race window is meaningful; only the count matters here, not row content.
        const string tagName = "power-user";
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();

        int startCount;
        await using (var readCtx = await contextFactory.CreateDbContextAsync())
        {
            startCount = (await readCtx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName)).UsageCount;
        }
        Assert.That(startCount, Is.GreaterThan(0), "canonical seed precondition");

        var tasks = Enumerable.Range(0, startCount)
            .Select(_ => Task.Run(async () =>
            {
                await using var scope = _serviceProvider!.CreateAsyncScope();
                var scopedRepo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
                await scopedRepo.DecrementUsageAsync(tagName, cancellationToken: CancellationToken.None);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // startCount concurrent decrements from startCount must land exactly on 0 (no lost updates).
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DecrementUsageAsync_WhenCountIsZero_StaysAtZero()
    {
        // 'tester' is a real canonical tag already at usage_count = 0 — decrementing must clamp, not go negative.
        const string tagName = "tester";

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
            await repo.DecrementUsageAsync(tagName, cancellationToken: CancellationToken.None);
        }

        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await contextFactory.CreateDbContextAsync();
        var def = await ctx.TagDefinitions.AsNoTracking().FirstAsync(t => t.TagName == tagName);
        Assert.That(def.UsageCount, Is.EqualTo(0));
    }
}

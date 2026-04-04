using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Smoke tests proving each of the 7 IDbContextFactory-migrated repositories can perform
/// a basic operation against real PostgreSQL without ObjectDisposedException.
///
/// DATA-01 verification: Each test validates one migrated repository creates its own
/// per-method DbContext and successfully completes an operation.
///
/// Migrated repositories under test:
/// 1. BlocklistSubscriptionsRepository (ContentDetection)
/// 2. DomainFiltersRepository (ContentDetection)
/// 3. CachedBlockedDomainsRepository (ContentDetection)
/// 4. TagDefinitionsRepository (Telegram)
/// 5. PendingNotificationsRepository (Telegram)
/// 6. AdminNotesRepository (Telegram)
/// 7. UserTagsRepository (Telegram)
/// </summary>
[TestFixture]
[Category("Integration")]
public class DbContextFactoryMigrationTests
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

        // Register all 7 migrated repositories
        services.AddScoped<IBlocklistSubscriptionsRepository, BlocklistSubscriptionsRepository>();
        services.AddScoped<IDomainFiltersRepository, DomainFiltersRepository>();
        services.AddScoped<ICachedBlockedDomainsRepository, CachedBlockedDomainsRepository>();
        services.AddScoped<ITagDefinitionsRepository, TagDefinitionsRepository>();
        services.AddScoped<IPendingNotificationsRepository, PendingNotificationsRepository>();
        services.AddScoped<IAdminNotesRepository, AdminNotesRepository>();
        services.AddScoped<IUserTagsRepository, UserTagsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed golden dataset — some repos require telegram_users/web_users for FK constraints
        var dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken: CancellationToken.None);
        await GoldenDataset.SeedAsync(context, dataProtectionProvider);
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    // ============================================================================
    // ContentDetection Repositories
    // ============================================================================

    [Test]
    public async Task BlocklistSubscriptionsRepository_GetAll_ReturnsWithoutException()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();

        // Empty result is fine — the test proves no ObjectDisposedException occurs
        var result = await repo.GetAllAsync(cancellationToken: CancellationToken.None);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task DomainFiltersRepository_GetAll_ReturnsWithoutException()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDomainFiltersRepository>();

        var result = await repo.GetAllAsync(cancellationToken: CancellationToken.None);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task CachedBlockedDomainsRepository_GetAll_ReturnsWithoutException()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();

        var result = await repo.GetAllAsync(cancellationToken: CancellationToken.None);

        Assert.That(result, Is.Not.Null);
    }

    // ============================================================================
    // Telegram Repositories
    // ============================================================================

    [Test]
    public async Task TagDefinitionsRepository_CreateAndGet_RoundTrips()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();

        const string tagName = "integration-test-tag";
        await repo.CreateAsync(tagName, TagColor.Primary, cancellationToken: CancellationToken.None);

        var result = await repo.GetByNameAsync(tagName, cancellationToken: CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TagName, Is.EqualTo(tagName));
    }

    [Test]
    public async Task PendingNotificationsRepository_AddAndGet_RoundTrips()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();

        const long userId = GoldenDataset.TelegramUsers.User1_TelegramUserId;
        await repo.AddPendingNotificationAsync(
            telegramUserId: userId,
            notificationType: "test",
            messageText: "integration test message",
            cancellationToken: CancellationToken.None);

        var notifications = await repo.GetPendingNotificationsForUserAsync(
            telegramUserId: userId,
            cancellationToken: CancellationToken.None);

        Assert.That(notifications.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task AdminNotesRepository_AddAndGet_RoundTrips()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAdminNotesRepository>();

        const long userId = GoldenDataset.TelegramUsers.User1_TelegramUserId;
        var note = new AdminNote
        {
            TelegramUserId = userId,
            NoteText = "Integration test note",
            CreatedBy = Actor.FromSystem("integration-test"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsPinned = false
        };

        await repo.AddNoteAsync(note, cancellationToken: CancellationToken.None);

        var notes = await repo.GetNotesByUserIdAsync(
            telegramUserId: userId,
            cancellationToken: CancellationToken.None);

        Assert.That(notes.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task UserTagsRepository_AddAndGet_RoundTrips()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var tagDefsRepo = scope.ServiceProvider.GetRequiredService<ITagDefinitionsRepository>();
        var userTagsRepo = scope.ServiceProvider.GetRequiredService<IUserTagsRepository>();

        const string tagName = "integration-user-tag";
        const long userId = GoldenDataset.TelegramUsers.User1_TelegramUserId;

        // Create the tag definition first (FK constraint)
        await tagDefsRepo.CreateAsync(tagName, TagColor.Info, cancellationToken: CancellationToken.None);

        var userTag = new UserTag
        {
            TelegramUserId = userId,
            TagName = tagName,
            TagColor = TagColor.Info,
            AddedBy = Actor.FromSystem("integration-test"),
            AddedAt = DateTimeOffset.UtcNow
        };

        await userTagsRepo.AddTagAsync(userTag, cancellationToken: CancellationToken.None);

        var tags = await userTagsRepo.GetTagsByUserIdAsync(
            telegramUserId: userId,
            cancellationToken: CancellationToken.None);

        Assert.That(tags.Count, Is.GreaterThanOrEqualTo(1));
    }
}

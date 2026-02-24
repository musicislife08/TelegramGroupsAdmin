using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;
using PermissionLevel = TelegramGroupsAdmin.Core.Models.PermissionLevel;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for ManagedChatsRepository.GetUserAccessibleChatsAsync().
/// Verifies that GlobalAdmin and Owner permission levels receive a synthetic "Global" entry
/// (ChatId=0) appended to the list of accessible chats.
/// </summary>
[TestFixture]
public class ManagedChatsRepositoryTests
{
    private const string TestUserId = "test-user-id";
    private const long TestChatId = -100123456789L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IManagedChatsRepository? _repository;
    private IDbContextFactory<AppDbContext>? _contextFactory;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        _contextFactory = _scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region GlobalAdmin synthetic Global entry

    [Test]
    public async Task GetUserAccessibleChatsAsync_GlobalAdmin_IncludesSyntheticGlobalEntry()
    {
        // Act
        var result = await _repository!.GetUserAccessibleChatsAsync(
            TestUserId,
            PermissionLevel.GlobalAdmin,
            cancellationToken: CancellationToken.None);

        // Assert
        var globalEntry = result.Find(c => c.Identity.Id == 0);
        Assert.That(globalEntry, Is.Not.Null);
        Assert.That(globalEntry!.Identity.ChatName, Is.EqualTo("Global"));
    }

    [Test]
    public async Task GetUserAccessibleChatsAsync_Owner_IncludesSyntheticGlobalEntry()
    {
        // Act
        var result = await _repository!.GetUserAccessibleChatsAsync(
            TestUserId,
            PermissionLevel.Owner,
            cancellationToken: CancellationToken.None);

        // Assert
        var globalEntry = result.Find(c => c.Identity.Id == 0);
        Assert.That(globalEntry, Is.Not.Null);
        Assert.That(globalEntry!.Identity.ChatName, Is.EqualTo("Global"));
    }

    [Test]
    public async Task GetUserAccessibleChatsAsync_GlobalAdmin_GlobalEntryHasExpectedProperties()
    {
        // Act
        var result = await _repository!.GetUserAccessibleChatsAsync(
            TestUserId,
            PermissionLevel.GlobalAdmin,
            cancellationToken: CancellationToken.None);

        // Assert
        var globalEntry = result.Find(c => c.Identity.Id == 0);
        Assert.That(globalEntry, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(globalEntry!.ChatType, Is.EqualTo(ManagedChatType.Group));
            Assert.That(globalEntry.BotStatus, Is.EqualTo(BotChatStatus.Member));
            Assert.That(globalEntry.IsAdmin, Is.False);
            Assert.That(globalEntry.IsActive, Is.False);
        }
    }

    [Test]
    public async Task GetUserAccessibleChatsAsync_GlobalAdmin_WithRealChats_GlobalEntryAppended()
    {
        // Arrange — seed one real managed chat into the database
        await using var ctx = await _contextFactory!.CreateDbContextAsync(CancellationToken.None);
        ctx.ManagedChats.Add(new DataModels.ManagedChatRecordDto
        {
            ChatId = TestChatId,
            ChatName = "Test Group",
            ChatType = DataModels.ManagedChatType.Supergroup,
            BotStatus = DataModels.BotChatStatus.Administrator,
            IsAdmin = true,
            AddedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            IsDeleted = false
        });
        await ctx.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await _repository!.GetUserAccessibleChatsAsync(
            TestUserId,
            PermissionLevel.GlobalAdmin,
            cancellationToken: CancellationToken.None);

        // Assert — both the real chat and the synthetic Global entry must be present
        var realChat = result.Find(c => c.Identity.Id == TestChatId);
        var globalEntry = result.Find(c => c.Identity.Id == 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(realChat, Is.Not.Null, "Real seeded chat should be returned");
            Assert.That(globalEntry, Is.Not.Null, "Synthetic Global entry should be appended");
            Assert.That(result, Has.Count.EqualTo(2));
        }
    }

    #endregion
}

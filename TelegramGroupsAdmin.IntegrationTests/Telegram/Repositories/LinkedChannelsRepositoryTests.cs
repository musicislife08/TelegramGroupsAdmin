using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for LinkedChannelsRepository.
/// Tests CRUD operations against a real PostgreSQL database using Testcontainers.
/// Uses golden dataset for realistic test data.
/// </summary>
[TestFixture]
public class LinkedChannelsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ILinkedChannelsRepository? _repository;
    private IDataProtectionProvider? _dataProtectionProvider;

    // Test constants for new records (not in golden dataset)
    private const long TestChatId = -1001999888777;
    private const long TestChannelId = -1001777888999;
    private const string TestChannelName = "Test Channel";

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Register LinkedChannelsRepository
        services.AddScoped<ILinkedChannelsRepository, LinkedChannelsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Seed golden dataset
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedAsync(context, _dataProtectionProvider);
        }

        // Create repository instance
        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<ILinkedChannelsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region GetByChatIdAsync Tests

    [Test]
    public async Task GetByChatIdAsync_ExistingChat_ReturnsRecord()
    {
        // Arrange - Use channel from golden dataset
        var chatId = GoldenDataset.LinkedChannels.Channel1_ManagedChatId;

        // Act
        var result = await _repository!.GetByChatIdAsync(chatId);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ManagedChatId, Is.EqualTo(chatId));
            Assert.That(result.ChannelId, Is.EqualTo(GoldenDataset.LinkedChannels.Channel1_ChannelId));
            Assert.That(result.ChannelName, Is.EqualTo(GoldenDataset.LinkedChannels.Channel1_Name));
            Assert.That(result.PhotoHash, Is.Not.Null, "Channel1 has photo hash in golden dataset");
        }
    }

    [Test]
    public async Task GetByChatIdAsync_NonExistentChat_ReturnsNull()
    {
        // Arrange
        const long nonExistentChatId = -9999999999;

        // Act
        var result = await _repository!.GetByChatIdAsync(nonExistentChatId);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetByChannelIdAsync Tests

    [Test]
    public async Task GetByChannelIdAsync_ExistingChannel_ReturnsRecord()
    {
        // Arrange - Use channel from golden dataset
        var channelId = GoldenDataset.LinkedChannels.Channel1_ChannelId;

        // Act
        var result = await _repository!.GetByChannelIdAsync(channelId);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ChannelId, Is.EqualTo(channelId));
            Assert.That(result.ManagedChatId, Is.EqualTo(GoldenDataset.LinkedChannels.Channel1_ManagedChatId));
        }
    }

    [Test]
    public async Task GetByChannelIdAsync_NonExistentChannel_ReturnsNull()
    {
        // Arrange
        const long nonExistentChannelId = -8888888888;

        // Act
        var result = await _repository!.GetByChannelIdAsync(nonExistentChannelId);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region UpsertAsync Tests

    [Test]
    public async Task UpsertAsync_NewRecord_InsertsSuccessfully()
    {
        // Arrange - Create a new managed chat first (FK constraint)
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO managed_chats (chat_id, chat_name, chat_type, bot_status, is_admin, added_at, is_active) VALUES ({TestChatId}, 'Test Chat', 2, 1, true, NOW(), true)");
        }

        var newRecord = new LinkedChannelRecord(
            Id: 0, // Will be assigned by DB
            ManagedChatId: TestChatId,
            ChannelId: TestChannelId,
            ChannelName: TestChannelName,
            ChannelIconPath: null,
            PhotoHash: [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22],
            LastSynced: DateTimeOffset.UtcNow
        );

        // Act
        await _repository!.UpsertAsync(newRecord);

        // Assert - Verify it was inserted
        var retrieved = await _repository.GetByChatIdAsync(TestChatId);
        Assert.That(retrieved, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved!.ChannelId, Is.EqualTo(TestChannelId));
            Assert.That(retrieved.ChannelName, Is.EqualTo(TestChannelName));
            Assert.That(retrieved.PhotoHash, Is.Not.Null);
        }
    }

    [Test]
    public async Task UpsertAsync_ExistingRecord_UpdatesSuccessfully()
    {
        // Arrange - Get existing record from golden dataset
        var existingRecord = await _repository!.GetByChatIdAsync(GoldenDataset.LinkedChannels.Channel1_ManagedChatId);
        Assert.That(existingRecord, Is.Not.Null);

        // Create updated record with new channel name
        var updatedRecord = existingRecord! with
        {
            ChannelName = "Updated Channel Name",
            LastSynced = DateTimeOffset.UtcNow
        };

        // Act
        await _repository.UpsertAsync(updatedRecord);

        // Assert - Verify it was updated
        var retrieved = await _repository.GetByChatIdAsync(GoldenDataset.LinkedChannels.Channel1_ManagedChatId);
        Assert.That(retrieved, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved!.ChannelName, Is.EqualTo("Updated Channel Name"));
            Assert.That(retrieved.ChannelId, Is.EqualTo(existingRecord.ChannelId), "Channel ID should remain unchanged");
        }
    }

    #endregion

    #region DeleteByChatIdAsync Tests

    [Test]
    public async Task DeleteByChatIdAsync_ExistingRecord_DeletesSuccessfully()
    {
        // Arrange - Verify record exists (using golden dataset Channel2)
        var chatId = GoldenDataset.LinkedChannels.Channel2_ManagedChatId;
        var existingRecord = await _repository!.GetByChatIdAsync(chatId);
        Assert.That(existingRecord, Is.Not.Null, "Should have record to delete");

        // Act
        await _repository.DeleteByChatIdAsync(chatId);

        // Assert - Verify it was deleted
        var retrieved = await _repository.GetByChatIdAsync(chatId);
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task DeleteByChatIdAsync_NonExistentRecord_DoesNotThrow()
    {
        // Arrange
        const long nonExistentChatId = -7777777777;

        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () => await _repository!.DeleteByChatIdAsync(nonExistentChatId));
    }

    #endregion

    #region GetAllAsync Tests

    [Test]
    public async Task GetAllAsync_ReturnsAllRecords()
    {
        // Act
        var results = await _repository!.GetAllAsync();

        // Assert - Golden dataset has 2 linked channels
        Assert.That(results, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count, Is.EqualTo(2), "Golden dataset should have 2 linked channels");

            // Verify records are ordered by channel name
            Assert.That(
                string.Compare(results[0].ChannelName, results[1].ChannelName, StringComparison.Ordinal),
                Is.LessThanOrEqualTo(0),
                "Results should be ordered by channel name");
        }
    }

    [Test]
    public async Task GetAllAsync_IncludesPhotoHashWherePresent()
    {
        // Act
        var results = await _repository!.GetAllAsync();

        // Assert - Channel1 has photo hash, Channel2 does not
        var channel1 = results.FirstOrDefault(r => r.ChannelId == GoldenDataset.LinkedChannels.Channel1_ChannelId);
        var channel2 = results.FirstOrDefault(r => r.ChannelId == GoldenDataset.LinkedChannels.Channel2_ChannelId);

        Assert.That(channel1, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel1!.PhotoHash, Is.Not.Null, "Channel1 should have photo hash");

            Assert.That(channel2, Is.Not.Null);
        }
        Assert.That(channel2!.PhotoHash, Is.Null, "Channel2 should not have photo hash");
    }

    #endregion

    #region GetAllManagedChatIdsAsync Tests

    [Test]
    public async Task GetAllManagedChatIdsAsync_ReturnsAllChatIds()
    {
        // Act
        var chatIds = await _repository!.GetAllManagedChatIdsAsync();

        // Assert - Golden dataset has 2 linked channels
        Assert.That(chatIds, Is.Not.Null);
        Assert.That(chatIds.Count, Is.EqualTo(2));
        Assert.That(chatIds, Does.Contain(GoldenDataset.LinkedChannels.Channel1_ManagedChatId));
        Assert.That(chatIds, Does.Contain(GoldenDataset.LinkedChannels.Channel2_ManagedChatId));
    }

    [Test]
    public async Task GetAllManagedChatIdsAsync_ReturnsHashSet()
    {
        // Act
        var chatIds = await _repository!.GetAllManagedChatIdsAsync();

        // Assert - Should be a HashSet (efficient lookup)
        Assert.That(chatIds, Is.TypeOf<HashSet<long>>());
    }

    #endregion
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for LinkedChannelsRepository.
/// Tests CRUD operations against a real PostgreSQL database using Testcontainers.
/// Uses golden template clone for canonical test data.
///
/// Canonical baseline (from 21_linked_channels.sql — 3 rows, sequence at id=3):
///   id=1: managed_chat_id=-100026957614982, channel_id=-100021999196951
///         name='Linked Channel for Main Community', photo_hash=NULL
///   id=2: managed_chat_id=-100054416618415, channel_id=-100024769901572
///         name='Linked Channel for Regional Group', photo_hash=NULL
///   id=3: managed_chat_id=-100050808209814, channel_id=-100080262325997
///         name='Linked Channel for Growth Community', photo_hash=NULL
/// </summary>
[TestFixture]
public class LinkedChannelsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private ILinkedChannelsRepository? _repository;

    // Canonical anchor — MainChat linked channel (id=1)
    private const long MainChatId = -100026957614982L;
    private const long MainChannelId = -100021999196951L;
    private const string MainChannelName = "Linked Channel for Main Community";

    // Canonical anchor — second linked channel (id=2)
    private const long RegionalChatId = -100054416618415L;
    private const long RegionalChannelId = -100024769901572L;

    // Test constants for new records (not in canonical dataset)
    private const long TestChatId = -1001999888777;
    private const long TestChannelId = -1001777888999;
    private const string TestChannelName = "Test Channel";

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<ILinkedChannelsRepository, LinkedChannelsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<ILinkedChannelsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region GetByChatIdAsync Tests

    [Test]
    public async Task GetByChatIdAsync_ExistingChat_ReturnsRecord()
    {
        // Act
        var result = await _repository!.GetByChatIdAsync(MainChatId);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ManagedChatId, Is.EqualTo(MainChatId));
            Assert.That(result.ChannelId, Is.EqualTo(MainChannelId));
            Assert.That(result.ChannelName, Is.EqualTo(MainChannelName));
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
        // Act
        var result = await _repository!.GetByChannelIdAsync(MainChannelId);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ChannelId, Is.EqualTo(MainChannelId));
            Assert.That(result.ManagedChatId, Is.EqualTo(MainChatId));
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
        // Arrange - Get existing canonical record
        var existingRecord = await _repository!.GetByChatIdAsync(MainChatId);
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
        var retrieved = await _repository.GetByChatIdAsync(MainChatId);
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
        // Arrange - Verify canonical record exists before deleting
        var existingRecord = await _repository!.GetByChatIdAsync(RegionalChatId);
        Assert.That(existingRecord, Is.Not.Null, "Should have record to delete");

        // Act
        await _repository.DeleteByChatIdAsync(RegionalChatId);

        // Assert - Verify it was deleted
        var retrieved = await _repository.GetByChatIdAsync(RegionalChatId);
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

        // Assert - canonical dataset has 3 linked channels
        Assert.That(results, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count, Is.EqualTo(3), "Canonical dataset should have 3 linked channels");

            // Verify records are ordered by channel name
            Assert.That(
                string.Compare(results[0].ChannelName, results[1].ChannelName, StringComparison.Ordinal),
                Is.LessThanOrEqualTo(0),
                "Results should be ordered by channel name");
            Assert.That(
                string.Compare(results[1].ChannelName, results[2].ChannelName, StringComparison.Ordinal),
                Is.LessThanOrEqualTo(0),
                "Results should be ordered by channel name");
        }
    }

    [Test]
    public async Task GetAllAsync_PhotoHashIsNullForAllCanonicalRows()
    {
        // All 3 canonical linked_channels rows have photo_hash=NULL.
        // This test verifies that null photo_hash is correctly round-tripped.
        var results = await _repository!.GetAllAsync();

        Assert.That(results, Is.Not.Empty);
        Assert.That(
            results.All(r => r.PhotoHash is null),
            Is.True,
            "All canonical linked channel rows should have null photo_hash");
    }

    #endregion

    #region GetAllManagedChatIdsAsync Tests

    [Test]
    public async Task GetAllManagedChatIdsAsync_ReturnsAllChatIds()
    {
        // Act
        var chatIds = await _repository!.GetAllManagedChatIdsAsync();

        // Assert - canonical dataset has 3 linked channels
        Assert.That(chatIds, Is.Not.Null);
        Assert.That(chatIds.Count, Is.EqualTo(3));
        Assert.That(chatIds, Does.Contain(MainChatId));
        Assert.That(chatIds, Does.Contain(RegionalChatId));
        Assert.That(chatIds, Does.Contain(-100050808209814L));
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

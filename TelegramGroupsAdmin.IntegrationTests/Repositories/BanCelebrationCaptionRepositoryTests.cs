using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for BanCelebrationCaptionRepository - manages caption text and seeding.
///
/// Architecture:
/// - Captions have chat version (Text) and DM version (DmText) for different contexts
/// - Supports placeholders: {username}, {chatname}, {bancount}
/// - SeedDefaultsIfEmptyAsync populates 51 default captions on first run
/// - GetRandomAsync uses PostgreSQL RANDOM() for efficient selection
///
/// Test Strategy:
/// - Real PostgreSQL via Testcontainers (unique database per test)
/// - Tests CRUD operations, seeding logic, and validation
/// </summary>
[TestFixture]
public class BanCelebrationCaptionRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBanCelebrationCaptionRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.AddScoped<IBanCelebrationCaptionRepository, BanCelebrationCaptionRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<IBanCelebrationCaptionRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region GetAllAsync Tests

    [Test]
    public async Task GetAllAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Act
        var result = await _repository!.GetAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetAllAsync_WithCaptions_ReturnsAllOrderedByCreatedAtDescending()
    {
        // Arrange
        var caption1 = await _repository!.AddAsync("Chat 1", "DM 1", "First");
        await Task.Delay(10);
        var caption2 = await _repository!.AddAsync("Chat 2", "DM 2", "Second");
        await Task.Delay(10);
        var caption3 = await _repository!.AddAsync("Chat 3", "DM 3", "Third");

        // Act
        var result = await _repository.GetAllAsync();

        // Assert - Should be ordered by CreatedAt descending (newest first)
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Id, Is.EqualTo(caption3.Id));
        Assert.That(result[1].Id, Is.EqualTo(caption2.Id));
        Assert.That(result[2].Id, Is.EqualTo(caption1.Id));
    }

    #endregion

    #region GetRandomAsync Tests

    [Test]
    public async Task GetRandomAsync_EmptyDatabase_ReturnsNull()
    {
        // Act
        var result = await _repository!.GetRandomAsync();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetRandomAsync_WithCaptions_ReturnsOneCaption()
    {
        // Arrange
        await _repository!.AddAsync("Caption A", "DM A", "A");
        await _repository.AddAsync("Caption B", "DM B", "B");

        // Act
        var result = await _repository.GetRandomAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.AnyOf("A", "B"));
    }

    [Test]
    public async Task GetRandomAsync_SingleCaption_ReturnsThatCaption()
    {
        // Arrange
        var caption = await _repository!.AddAsync("Only one", "DM only", "Only");

        // Act
        var result = await _repository.GetRandomAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(caption.Id));
    }

    #endregion

    #region GetByIdAsync Tests

    [Test]
    public async Task GetByIdAsync_ExistingCaption_ReturnsCaption()
    {
        // Arrange
        var added = await _repository!.AddAsync(
            "🔨 **BAN HAMMER!** {username} has been banned!",
            "You have been banned!",
            "Ban Hammer");

        // Act
        var result = await _repository.GetByIdAsync(added.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo(added.Id));
            Assert.That(result.Text, Is.EqualTo("🔨 **BAN HAMMER!** {username} has been banned!"));
            Assert.That(result.DmText, Is.EqualTo("You have been banned!"));
            Assert.That(result.Name, Is.EqualTo("Ban Hammer"));
        });
    }

    [Test]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Act
        var result = await _repository!.GetByIdAsync(99999);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region AddAsync Tests

    [Test]
    public async Task AddAsync_ValidCaption_CreatesRecord()
    {
        // Act
        var result = await _repository!.AddAsync(
            "💀 **FATALITY!** {username} has been finished!",
            "You have been finished!",
            "Mortal Kombat");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.GreaterThan(0));
            Assert.That(result.Text, Does.Contain("{username}"));
            Assert.That(result.DmText, Does.Contain("You"));
            Assert.That(result.Name, Is.EqualTo("Mortal Kombat"));
            Assert.That(result.CreatedAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
    }

    [Test]
    public async Task AddAsync_NullName_SavesWithNullName()
    {
        // Act
        var result = await _repository!.AddAsync("Chat text", "DM text", null);

        // Assert
        Assert.That(result.Name, Is.Null);
    }

    [Test]
    public async Task AddAsync_WithPlaceholders_PreservesPlaceholders()
    {
        // Act
        var result = await _repository!.AddAsync(
            "Ban #{bancount} in {chatname}! Goodbye {username}!",
            "You are ban #{bancount}!",
            "With Placeholders");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("{bancount}"));
            Assert.That(result.Text, Does.Contain("{chatname}"));
            Assert.That(result.Text, Does.Contain("{username}"));
            Assert.That(result.DmText, Does.Contain("{bancount}"));
        });
    }

    [Test]
    public void AddAsync_EmptyText_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddAsync("", "DM text", "Name"));
    }

    [Test]
    public void AddAsync_WhitespaceText_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddAsync("   ", "DM text", "Name"));
    }

    [Test]
    public void AddAsync_EmptyDmText_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddAsync("Chat text", "", "Name"));
    }

    [Test]
    public void AddAsync_WhitespaceDmText_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddAsync("Chat text", "   ", "Name"));
    }

    #endregion

    #region UpdateAsync Tests

    [Test]
    public async Task UpdateAsync_ExistingCaption_UpdatesAllFields()
    {
        // Arrange
        var original = await _repository!.AddAsync("Original", "Original DM", "Original Name");

        // Act
        var updated = await _repository.UpdateAsync(
            original.Id,
            "Updated chat text",
            "Updated DM text",
            "Updated Name");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(updated.Id, Is.EqualTo(original.Id));
            Assert.That(updated.Text, Is.EqualTo("Updated chat text"));
            Assert.That(updated.DmText, Is.EqualTo("Updated DM text"));
            Assert.That(updated.Name, Is.EqualTo("Updated Name"));
        });

        // Verify persistence
        var fetched = await _repository.GetByIdAsync(original.Id);
        Assert.That(fetched!.Text, Is.EqualTo("Updated chat text"));
    }

    [Test]
    public async Task UpdateAsync_SetNameToNull_ClearsName()
    {
        // Arrange
        var original = await _repository!.AddAsync("Text", "DM", "Has Name");

        // Act
        var updated = await _repository.UpdateAsync(original.Id, "Text", "DM", null);

        // Assert
        Assert.That(updated.Name, Is.Null);
    }

    [Test]
    public void UpdateAsync_NonExistentId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _repository!.UpdateAsync(99999, "Text", "DM", "Name"));
    }

    [Test]
    public async Task UpdateAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var original = await _repository!.AddAsync("Text", "DM", "Name");

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.UpdateAsync(original.Id, "", "DM", "Name"));
    }

    [Test]
    public async Task UpdateAsync_EmptyDmText_ThrowsArgumentException()
    {
        // Arrange
        var original = await _repository!.AddAsync("Text", "DM", "Name");

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.UpdateAsync(original.Id, "Text", "", "Name"));
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_ExistingCaption_RemovesRecord()
    {
        // Arrange
        var caption = await _repository!.AddAsync("To delete", "DM", "Delete Me");

        // Act
        await _repository.DeleteAsync(caption.Id);

        // Assert
        var result = await _repository.GetByIdAsync(caption.Id);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_NonExistentId_DoesNotThrow()
    {
        // Act & Assert - Should not throw (just logs warning)
        Assert.DoesNotThrowAsync(async () =>
            await _repository!.DeleteAsync(99999));
    }

    #endregion

    #region GetCountAsync Tests

    [Test]
    public async Task GetCountAsync_EmptyDatabase_ReturnsZero()
    {
        // Act
        var count = await _repository!.GetCountAsync();

        // Assert
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetCountAsync_WithCaptions_ReturnsCorrectCount()
    {
        // Arrange
        await _repository!.AddAsync("A", "DM A", "A");
        await _repository.AddAsync("B", "DM B", "B");
        await _repository.AddAsync("C", "DM C", "C");

        // Act
        var count = await _repository.GetCountAsync();

        // Assert
        Assert.That(count, Is.EqualTo(3));
    }

    #endregion

    #region SeedDefaultsIfEmptyAsync Tests

    [Test]
    public async Task SeedDefaultsIfEmptyAsync_EmptyDatabase_SeedsDefaultCaptions()
    {
        // Verify empty before seeding
        var countBefore = await _repository!.GetCountAsync();
        Assert.That(countBefore, Is.EqualTo(0));

        // Act
        await _repository.SeedDefaultsIfEmptyAsync();

        // Assert
        var countAfter = await _repository.GetCountAsync();
        Assert.That(countAfter, Is.EqualTo(BanCelebrationDefaults.Captions.Count),
            $"Should seed exactly {BanCelebrationDefaults.Captions.Count} default captions");
    }

    [Test]
    public async Task SeedDefaultsIfEmptyAsync_NonEmptyDatabase_DoesNotSeed()
    {
        // Arrange - Add one caption first
        await _repository!.AddAsync("Existing", "DM Existing", "Existing");

        // Act
        await _repository.SeedDefaultsIfEmptyAsync();

        // Assert - Should still be just 1 (no seeding occurred)
        var count = await _repository.GetCountAsync();
        Assert.That(count, Is.EqualTo(1), "Should not seed when table already has data");
    }

    [Test]
    public async Task SeedDefaultsIfEmptyAsync_SeededCaptions_HaveValidContent()
    {
        // Arrange
        await _repository!.SeedDefaultsIfEmptyAsync();

        // Act
        var captions = await _repository.GetAllAsync();

        // Assert - Verify seeded captions have expected structure
        Assert.That(captions, Is.Not.Empty);

        foreach (var caption in captions)
        {
            Assert.Multiple(() =>
            {
                Assert.That(caption.Text, Is.Not.Empty, $"Caption {caption.Id} should have chat text");
                Assert.That(caption.DmText, Is.Not.Empty, $"Caption {caption.Id} should have DM text");
                Assert.That(caption.Name, Is.Not.Null, $"Caption {caption.Id} should have a name");
            });
        }
    }

    [Test]
    public async Task SeedDefaultsIfEmptyAsync_SeededCaptions_ContainExpectedPlaceholders()
    {
        // Arrange
        await _repository!.SeedDefaultsIfEmptyAsync();

        // Act
        var captions = await _repository.GetAllAsync();

        // Assert - At least some captions should contain placeholders
        var captionsWithUsernamePlaceholder = captions.Count(c => c.Text.Contains("{username}"));
        var captionsWithBancountPlaceholder = captions.Count(c =>
            c.Text.Contains("{bancount}", StringComparison.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(captionsWithUsernamePlaceholder, Is.GreaterThan(0),
                "Some captions should have {username} placeholder");
            Assert.That(captionsWithBancountPlaceholder, Is.GreaterThan(0),
                "Some captions should have {bancount} placeholder");
        });
    }

    [Test]
    public async Task SeedDefaultsIfEmptyAsync_CalledTwice_OnlySeedsOnce()
    {
        // Act - Call twice
        await _repository!.SeedDefaultsIfEmptyAsync();
        await _repository.SeedDefaultsIfEmptyAsync();

        // Assert - Should only have one set of defaults
        var count = await _repository.GetCountAsync();
        Assert.That(count, Is.EqualTo(BanCelebrationDefaults.Captions.Count),
            "Calling seed twice should not duplicate captions");
    }

    #endregion
}

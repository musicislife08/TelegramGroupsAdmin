using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for BanCelebrationGifRepository - manages GIF files and database records.
///
/// Architecture:
/// - GIFs stored locally in /data/media/ban-gifs/{id}.{ext}
/// - Database stores metadata (file path, cached Telegram file_id, thumbnail path)
/// - Repository handles both file I/O and database operations
/// - GetRandomAsync uses PostgreSQL RANDOM() for efficient random selection
///
/// Test Strategy:
/// - Real PostgreSQL via Testcontainers (unique database per test)
/// - Temp directory for file storage (isolated per test)
/// - Tests CRUD operations, file management, and error handling
/// </summary>
[TestFixture]
public class BanCelebrationGifRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IBanCelebrationGifRepository? _repository;
    private string _tempMediaPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Create temp directory for media files
        _tempMediaPath = Path.Combine(Path.GetTempPath(), $"BanCelebrationGifTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempMediaPath);

        // Set up dependency injection
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configure MessageHistoryOptions with temp path
        services.Configure<MessageHistoryOptions>(opt =>
            opt.ImageStoragePath = _tempMediaPath);

        // Add HttpClientFactory for URL downloads
        services.AddHttpClient();

        services.AddScoped<IBanCelebrationGifRepository, BanCelebrationGifRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<IBanCelebrationGifRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();

        // Clean up temp directory
        if (Directory.Exists(_tempMediaPath))
        {
            try
            {
                Directory.Delete(_tempMediaPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
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
    public async Task GetAllAsync_WithGifs_ReturnsAllOrderedByCreatedAtDescending()
    {
        // Arrange - Add multiple GIFs
        using var stream1 = CreateTestGifStream();
        using var stream2 = CreateTestGifStream();
        using var stream3 = CreateTestGifStream();

        var gif1 = await _repository!.AddFromFileAsync(stream1, "first.gif", "First GIF");
        await Task.Delay(10); // Ensure different timestamps
        var gif2 = await _repository!.AddFromFileAsync(stream2, "second.gif", "Second GIF");
        await Task.Delay(10);
        var gif3 = await _repository!.AddFromFileAsync(stream3, "third.gif", "Third GIF");

        // Act
        var result = await _repository.GetAllAsync();

        // Assert - Should be ordered by CreatedAt descending (newest first)
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Id, Is.EqualTo(gif3.Id));
        Assert.That(result[1].Id, Is.EqualTo(gif2.Id));
        Assert.That(result[2].Id, Is.EqualTo(gif1.Id));
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
    public async Task GetRandomAsync_WithGifs_ReturnsOneGif()
    {
        // Arrange
        using var stream1 = CreateTestGifStream();
        using var stream2 = CreateTestGifStream();

        await _repository!.AddFromFileAsync(stream1, "a.gif", "GIF A");
        await _repository.AddFromFileAsync(stream2, "b.gif", "GIF B");

        // Act
        var result = await _repository.GetRandomAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.AnyOf("GIF A", "GIF B"));
    }

    [Test]
    public async Task GetRandomAsync_SingleGif_ReturnsThatGif()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "only.gif", "Only GIF");

        // Act
        var result = await _repository.GetRandomAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(gif.Id));
    }

    #endregion

    #region GetByIdAsync Tests

    [Test]
    public async Task GetByIdAsync_ExistingGif_ReturnsGif()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var added = await _repository!.AddFromFileAsync(stream, "test.gif", "Test GIF");

        // Act
        var result = await _repository.GetByIdAsync(added.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo(added.Id));
            Assert.That(result.Name, Is.EqualTo("Test GIF"));
            Assert.That(result.FilePath, Does.Contain("ban-gifs"));
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

    #region AddFromFileAsync Tests

    [Test]
    public async Task AddFromFileAsync_ValidFile_SavesFileAndCreatesRecord()
    {
        // Arrange
        using var stream = CreateTestGifStream();

        // Act
        var result = await _repository!.AddFromFileAsync(stream, "celebration.gif", "Celebration");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.GreaterThan(0));
            Assert.That(result.Name, Is.EqualTo("Celebration"));
            Assert.That(result.FilePath, Does.EndWith(".gif"));
            Assert.That(result.FileId, Is.Null); // Not cached yet
        });

        // Verify file exists on disk
        var fullPath = _repository.GetFullPath(result.FilePath);
        Assert.That(File.Exists(fullPath), Is.True, "GIF file should exist on disk");
    }

    [Test]
    public async Task AddFromFileAsync_Mp4File_SavesWithCorrectExtension()
    {
        // Arrange
        using var stream = CreateTestGifStream(); // Content doesn't matter for this test

        // Act
        var result = await _repository!.AddFromFileAsync(stream, "video.mp4", "Video GIF");

        // Assert
        Assert.That(result.FilePath, Does.EndWith(".mp4"));
    }

    [Test]
    public async Task AddFromFileAsync_NullName_SavesWithNullName()
    {
        // Arrange
        using var stream = CreateTestGifStream();

        // Act
        var result = await _repository!.AddFromFileAsync(stream, "unnamed.gif", null);

        // Assert
        Assert.That(result.Name, Is.Null);
    }

    [Test]
    public void AddFromFileAsync_EmptyStream_ThrowsArgumentException()
    {
        // Arrange
        using var emptyStream = new MemoryStream();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddFromFileAsync(emptyStream, "empty.gif", "Empty"));
    }

    [Test]
    public void AddFromFileAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _repository!.AddFromFileAsync(null!, "test.gif", "Test"));
    }

    [Test]
    public void AddFromFileAsync_EmptyFileName_ThrowsArgumentException()
    {
        // Arrange
        using var stream = CreateTestGifStream();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.AddFromFileAsync(stream, "", "Test"));
    }

    [Test]
    public async Task AddFromFileAsync_NoExtension_DefaultsToGif()
    {
        // Arrange
        using var stream = CreateTestGifStream();

        // Act
        var result = await _repository!.AddFromFileAsync(stream, "noextension", "No Extension");

        // Assert
        Assert.That(result.FilePath, Does.EndWith(".gif"));
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_ExistingGif_RemovesRecordAndFile()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "todelete.gif", "To Delete");
        var fullPath = _repository.GetFullPath(gif.FilePath);

        // Verify file exists before delete
        Assert.That(File.Exists(fullPath), Is.True);

        // Act
        await _repository.DeleteAsync(gif.Id);

        // Assert
        var result = await _repository.GetByIdAsync(gif.Id);
        Assert.That(result, Is.Null, "Database record should be deleted");
        Assert.That(File.Exists(fullPath), Is.False, "File should be deleted from disk");
    }

    [Test]
    public async Task DeleteAsync_WithThumbnail_RemovesBothFiles()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "withthumb.gif", "With Thumb");

        // Create a thumbnail file
        var thumbPath = $"ban-gifs/thumbnails/{gif.Id}.png";
        var fullThumbPath = _repository.GetFullPath(thumbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullThumbPath)!);
        await File.WriteAllTextAsync(fullThumbPath, "fake thumbnail");

        await _repository.UpdateThumbnailPathAsync(gif.Id, thumbPath);

        // Act
        await _repository.DeleteAsync(gif.Id);

        // Assert
        var gifFullPath = _repository.GetFullPath(gif.FilePath);
        Assert.That(File.Exists(gifFullPath), Is.False, "GIF file should be deleted");
        Assert.That(File.Exists(fullThumbPath), Is.False, "Thumbnail should be deleted");
    }

    [Test]
    public async Task DeleteAsync_NonExistentId_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () =>
            await _repository!.DeleteAsync(99999));
    }

    [Test]
    public async Task DeleteAsync_FileAlreadyDeleted_DoesNotThrow()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "predeleted.gif", "Pre-deleted");

        // Manually delete the file
        var fullPath = _repository.GetFullPath(gif.FilePath);
        File.Delete(fullPath);

        // Act & Assert - Should not throw
        Assert.DoesNotThrowAsync(async () =>
            await _repository.DeleteAsync(gif.Id));

        // Verify database record is still deleted
        var result = await _repository.GetByIdAsync(gif.Id);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region UpdateFileIdAsync Tests

    [Test]
    public async Task UpdateFileIdAsync_ExistingGif_UpdatesCachedFileId()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "tocache.gif", "To Cache");

        // Act
        await _repository.UpdateFileIdAsync(gif.Id, "AgACAgIAAxkBAAI_cached_file_id_123");

        // Assert
        var updated = await _repository.GetByIdAsync(gif.Id);
        Assert.That(updated!.FileId, Is.EqualTo("AgACAgIAAxkBAAI_cached_file_id_123"));
    }

    [Test]
    public async Task UpdateFileIdAsync_NonExistentId_DoesNotThrow()
    {
        // Act & Assert - Should not throw (just logs warning)
        Assert.DoesNotThrowAsync(async () =>
            await _repository!.UpdateFileIdAsync(99999, "some_file_id"));
    }

    [Test]
    public void UpdateFileIdAsync_EmptyFileId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repository!.UpdateFileIdAsync(1, ""));
    }

    #endregion

    #region UpdateThumbnailPathAsync Tests

    [Test]
    public async Task UpdateThumbnailPathAsync_ExistingGif_UpdatesThumbnailPath()
    {
        // Arrange
        using var stream = CreateTestGifStream();
        var gif = await _repository!.AddFromFileAsync(stream, "forthumb.gif", "For Thumb");

        // Act
        await _repository.UpdateThumbnailPathAsync(gif.Id, "ban-gifs/thumbnails/1.png");

        // Assert
        var updated = await _repository.GetByIdAsync(gif.Id);
        Assert.That(updated!.ThumbnailPath, Is.EqualTo("ban-gifs/thumbnails/1.png"));
    }

    [Test]
    public async Task UpdateThumbnailPathAsync_NonExistentId_DoesNotThrow()
    {
        // Act & Assert - Should not throw (just logs warning)
        Assert.DoesNotThrowAsync(async () =>
            await _repository!.UpdateThumbnailPathAsync(99999, "some/path.png"));
    }

    #endregion

    #region GetFullPath Tests

    [Test]
    public void GetFullPath_RelativePath_ResolvesToMediaBasePath()
    {
        // Act
        var fullPath = _repository!.GetFullPath("ban-gifs/123.gif");

        // Assert
        Assert.That(fullPath, Does.Contain(_tempMediaPath));
        Assert.That(fullPath, Does.EndWith(Path.Combine("media", "ban-gifs", "123.gif")));
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
    public async Task GetCountAsync_WithGifs_ReturnsCorrectCount()
    {
        // Arrange
        using var stream1 = CreateTestGifStream();
        using var stream2 = CreateTestGifStream();
        using var stream3 = CreateTestGifStream();

        await _repository!.AddFromFileAsync(stream1, "a.gif", "A");
        await _repository.AddFromFileAsync(stream2, "b.gif", "B");
        await _repository.AddFromFileAsync(stream3, "c.gif", "C");

        // Act
        var count = await _repository.GetCountAsync();

        // Assert
        Assert.That(count, Is.EqualTo(3));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal valid GIF stream for testing.
    /// This is the smallest valid GIF (1x1 transparent pixel).
    /// </summary>
    private static MemoryStream CreateTestGifStream()
    {
        // Minimal 1x1 transparent GIF
        var gifBytes = new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a header
            0x01, 0x00, 0x01, 0x00,             // 1x1 dimensions
            0x00,                               // No global color table
            0x00, 0x00,                         // Background + aspect ratio
            0x2C,                               // Image separator
            0x00, 0x00, 0x00, 0x00,             // Position
            0x01, 0x00, 0x01, 0x00,             // Dimensions
            0x00,                               // No local color table
            0x02, 0x01, 0x01, 0x00, 0x00,       // LZW minimum code size + data
            0x3B                                // Trailer
        };

        return new MemoryStream(gifBytes);
    }

    #endregion
}

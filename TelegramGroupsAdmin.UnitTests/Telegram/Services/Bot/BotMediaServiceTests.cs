using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Testably.Abstractions.Testing;
using TelegramGroupsAdmin.Core.Models;
using TelegramBot = Telegram.Bot;
using TelegramBotTypes = Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

/// <summary>
/// Unit tests for BotMediaService.
/// Tests cache logic, full download+resize flow, and temp file cleanup using MockFileSystem.
///
/// Architecture:
/// - BotMediaService wraps IBotMediaHandler/IBotChatHandler with local file caching
/// - User photo cache invalidation uses file_unique_id comparison
/// - Chat icon caching is simpler (no invalidation, just existence check)
/// - IFileSystem abstraction enables pure in-memory testing with real ImageSharp processing
///
/// Test Strategy:
/// - MockFileSystem for all file I/O (read/write/delete)
/// - Mocked handlers for Telegram API responses
/// - Real ImageSharp processing (mutation only - our code controls I/O)
/// - Programmatic test images via ImageSharp
/// </summary>
[TestFixture]
public class BotMediaServiceTests
{
    private const long TestUserId = 12345L;
    private const long TestChatId = -100123456789L;
    private const string TestFileId = "AgACAgIAAxkBAAI";
    private const string TestFileUniqueId = "AQADAgATunique";
    private const string TestFilePath = "photos/file_123.jpg";

    /// <summary>
    /// Pre-generated test image bytes (200x200 JPEG).
    /// Created once per test class for performance - ImageSharp generation adds ~10-20ms.
    /// </summary>
    private static readonly byte[] TestImageBytes = CreateTestImage();

    private IBotMediaHandler _mockMediaHandler = null!;
    private IBotChatHandler _mockChatHandler = null!;
    private MockFileSystem _mockFileSystem = null!;
    private ILogger<BotMediaService> _mockLogger = null!;
    private BotMediaService _service = null!;

    private string _basePath = null!;
    private string _userPhotosPath = null!;
    private string _chatIconsPath = null!;

    /// <summary>
    /// Creates a valid JPEG image for testing.
    /// Uses ImageSharp to generate a real image that can be processed.
    /// </summary>
    private static byte[] CreateTestImage()
    {
        using var image = new Image<Rgba32>(200, 200);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [SetUp]
    public void SetUp()
    {
        _mockMediaHandler = Substitute.For<IBotMediaHandler>();
        _mockChatHandler = Substitute.For<IBotChatHandler>();
        _mockLogger = Substitute.For<ILogger<BotMediaService>>();

        // Use platform-appropriate paths
        _basePath = OperatingSystem.IsWindows() ? @"C:\data" : "/data";
        _userPhotosPath = Path.Combine(_basePath, "media", "user_photos");
        _chatIconsPath = Path.Combine(_basePath, "media", "chat_icons");

        // Initialize MockFileSystem with required directories
        _mockFileSystem = new MockFileSystem();
        _mockFileSystem.Directory.CreateDirectory(_userPhotosPath);
        _mockFileSystem.Directory.CreateDirectory(_chatIconsPath);
        // Create temp directory (MockFileSystem doesn't have it by default)
        _mockFileSystem.Directory.CreateDirectory(_mockFileSystem.Path.GetTempPath());

        var options = Options.Create(new MessageHistoryOptions
        {
            ImageStoragePath = _basePath
        });

        _service = new BotMediaService(
            _mockMediaHandler,
            _mockChatHandler,
            _mockFileSystem,
            options,
            _mockLogger);
    }

    #region GetUserPhotoAsync - Cache Hit Tests

    [Test]
    public async Task GetUserPhotoAsync_CacheHit_NoKnownPhotoId_ReturnsCachedPath()
    {
        // Arrange - File exists, no known photo ID to compare
        var cachedFilePath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        _mockFileSystem.File.WriteAllText(cachedFilePath, "fake image data");

        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);

        // Act
        var result = await _service.GetUserPhotoAsync(TestUserId, knownPhotoId: null);

        // Assert - Returns cached path without re-downloading
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.RelativePath, Is.EqualTo($"user_photos/{TestUserId}.jpg"));
            Assert.That(result.FileUniqueId, Is.EqualTo(TestFileUniqueId));
        });

        // Verify file was NOT downloaded (cache hit)
        await _mockMediaHandler.DidNotReceive().GetFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetUserPhotoAsync_CacheHit_SamePhotoId_ReturnsCachedPath()
    {
        // Arrange - File exists AND photo ID matches (no change)
        var cachedFilePath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        _mockFileSystem.File.WriteAllText(cachedFilePath, "fake image data");

        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);

        // Act - Pass the same photo ID we know is current
        var result = await _service.GetUserPhotoAsync(TestUserId, knownPhotoId: TestFileUniqueId);

        // Assert - Returns cached path
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.RelativePath, Is.EqualTo($"user_photos/{TestUserId}.jpg"));

        // Verify file was NOT downloaded (cache hit - photo unchanged)
        await _mockMediaHandler.DidNotReceive().GetFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetUserPhotoAsync - Cache Invalidation Tests

    [Test]
    public async Task GetUserPhotoAsync_CacheInvalidation_DifferentPhotoId_ReDownloadsAndReturnsNewPhoto()
    {
        // Arrange - File exists BUT photo ID changed (user updated their photo)
        var cachedFilePath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        _mockFileSystem.File.WriteAllText(cachedFilePath, "old image data");

        const string newFileUniqueId = "AQADAgATnew_unique";
        SetupUserHasProfilePhoto(TestFileId, newFileUniqueId);
        SetupFileDownload(TestFileId, TestFilePath);

        // Act - Pass OLD photo ID (triggers invalidation)
        var result = await _service.GetUserPhotoAsync(TestUserId, knownPhotoId: TestFileUniqueId);

        // Assert - Full flow completed successfully
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.RelativePath, Is.EqualTo($"user_photos/{TestUserId}.jpg"));
            Assert.That(result.FileUniqueId, Is.EqualTo(newFileUniqueId));
        });

        // Verify download occurred
        await _mockMediaHandler.Received(1).GetFileAsync(TestFileId, Arg.Any<CancellationToken>());
        await _mockMediaHandler.Received(1).DownloadFileAsync(TestFilePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // Verify output file was created in MockFileSystem
        Assert.That(_mockFileSystem.File.Exists(cachedFilePath), Is.True);
    }

    #endregion

    #region GetUserPhotoAsync - Cache Miss Tests

    [Test]
    public async Task GetUserPhotoAsync_CacheMiss_DownloadsResizesAndReturnsResult()
    {
        // Arrange - No cached file exists
        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);
        SetupFileDownload(TestFileId, TestFilePath);

        // Act
        var result = await _service.GetUserPhotoAsync(TestUserId);

        // Assert - Full flow completed successfully
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.RelativePath, Is.EqualTo($"user_photos/{TestUserId}.jpg"));
            Assert.That(result.FileUniqueId, Is.EqualTo(TestFileUniqueId));
        });

        // Verify download occurred
        await _mockMediaHandler.Received(1).GetFileAsync(TestFileId, Arg.Any<CancellationToken>());
        await _mockMediaHandler.Received(1).DownloadFileAsync(TestFilePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // Verify output file was created in MockFileSystem
        var expectedPath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        Assert.That(_mockFileSystem.File.Exists(expectedPath), Is.True);
    }

    [Test]
    public async Task GetUserPhotoAsync_NoProfilePhoto_ReturnsNull()
    {
        // Arrange - User has no profile photo
        _mockMediaHandler.GetUserProfilePhotosAsync(TestUserId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TelegramBotTypes.UserProfilePhotos { TotalCount = 0, Photos = [] });

        // Act
        var result = await _service.GetUserPhotoAsync(TestUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetUserPhotoAsync_CleansUpTempFile_AfterSuccessfulProcessing()
    {
        // Arrange
        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);
        SetupFileDownload(TestFileId, TestFilePath);

        // Act
        await _service.GetUserPhotoAsync(TestUserId);

        // Assert - No temp files should remain in temp directory
        var tempPath = _mockFileSystem.Path.GetTempPath();
        var tempFiles = _mockFileSystem.Directory.GetFiles(tempPath);
        Assert.That(tempFiles, Is.Empty, "Temp file should be cleaned up after processing");
    }

    #endregion

    #region GetChatIconAsync - Cache Tests

    [Test]
    public async Task GetChatIconAsync_CacheHit_ReturnsCachedPath()
    {
        // Arrange - File already exists
        var cachedFilePath = Path.Combine(_chatIconsPath, $"{Math.Abs(TestChatId)}.jpg");
        _mockFileSystem.File.WriteAllText(cachedFilePath, "fake icon data");

        // Act
        var result = await _service.GetChatIconAsync(ChatIdentity.FromId(TestChatId));

        // Assert - Returns cached path without fetching from API
        Assert.That(result, Is.EqualTo($"chat_icons/{Math.Abs(TestChatId)}.jpg"));

        // Verify chat info was NOT fetched (cache hit)
        await _mockChatHandler.DidNotReceive().GetChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatIconAsync_CacheMiss_DownloadsResizesAndReturnsResult()
    {
        // Arrange - No cached file, chat has photo
        SetupChatHasPhoto(TestChatId, "Test Chat", TestFileId);
        SetupFileDownload(TestFileId, TestFilePath);

        // Act
        var result = await _service.GetChatIconAsync(ChatIdentity.FromId(TestChatId));

        // Assert - Full flow completed successfully
        Assert.That(result, Is.EqualTo($"chat_icons/{Math.Abs(TestChatId)}.jpg"));

        // Verify API calls occurred
        await _mockChatHandler.Received(1).GetChatAsync(TestChatId, Arg.Any<CancellationToken>());
        await _mockMediaHandler.Received(1).GetFileAsync(TestFileId, Arg.Any<CancellationToken>());
        await _mockMediaHandler.Received(1).DownloadFileAsync(TestFilePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // Verify output file was created in MockFileSystem
        var expectedPath = Path.Combine(_chatIconsPath, $"{Math.Abs(TestChatId)}.jpg");
        Assert.That(_mockFileSystem.File.Exists(expectedPath), Is.True);
    }

    [Test]
    public async Task GetChatIconAsync_NoChatPhoto_ReturnsNull()
    {
        // Arrange - Chat has no photo
        var chatInfo = new TelegramBotTypes.ChatFullInfo
        {
            Id = TestChatId,
            Type = TelegramBot.Types.Enums.ChatType.Supergroup,
            Title = "Test Chat",
            Photo = null
        };
        _mockChatHandler.GetChatAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(chatInfo);

        // Act
        var result = await _service.GetChatIconAsync(ChatIdentity.FromId(TestChatId));

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Passthrough Method Tests

    [Test]
    public async Task GetFileAsync_PassesThroughToHandler()
    {
        // Arrange
        var expectedFile = new TelegramBotTypes.TGFile { FileId = TestFileId, FilePath = TestFilePath };
        _mockMediaHandler.GetFileAsync(TestFileId, Arg.Any<CancellationToken>()).Returns(expectedFile);

        // Act
        var result = await _service.GetFileAsync(TestFileId);

        // Assert
        Assert.That(result.FilePath, Is.EqualTo(TestFilePath));
    }

    [Test]
    public async Task DownloadFileAsBytesAsync_ReturnsFileContents()
    {
        // Arrange
        var expectedFile = new TelegramBotTypes.TGFile { FileId = TestFileId, FilePath = TestFilePath };
        _mockMediaHandler.GetFileAsync(TestFileId, Arg.Any<CancellationToken>()).Returns(expectedFile);

        var expectedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        _mockMediaHandler.DownloadFileAsync(TestFilePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                stream.Write(expectedBytes);
                return Task.CompletedTask;
            });

        // Act
        var result = await _service.DownloadFileAsBytesAsync(TestFileId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBytes));
    }

    [Test]
    public void DownloadFileAsBytesAsync_NoFilePath_ThrowsException()
    {
        // Arrange - File has no path
        var fileWithNoPath = new TelegramBotTypes.TGFile { FileId = TestFileId, FilePath = null };
        _mockMediaHandler.GetFileAsync(TestFileId, Arg.Any<CancellationToken>()).Returns(fileWithNoPath);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.DownloadFileAsBytesAsync(TestFileId));

        Assert.That(ex!.Message, Does.Contain("Unable to get file path"));
    }

    #endregion

    #region Helper Methods

    private void SetupUserHasProfilePhoto(string fileId, string fileUniqueId)
    {
        var photoSize = new TelegramBotTypes.PhotoSize
        {
            FileId = fileId,
            FileUniqueId = fileUniqueId,
            Width = 160,
            Height = 160,
            FileSize = 1024
        };

        var photos = new TelegramBotTypes.UserProfilePhotos
        {
            TotalCount = 1,
            Photos = [[photoSize]]
        };

        _mockMediaHandler.GetUserProfilePhotosAsync(TestUserId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(photos);
    }

    private void SetupChatHasPhoto(long chatId, string title, string smallFileId)
    {
        var chatPhoto = new TelegramBotTypes.ChatPhoto
        {
            SmallFileId = smallFileId,
            SmallFileUniqueId = "small_unique",
            BigFileId = "big_file_id",
            BigFileUniqueId = "big_unique"
        };

        var chatInfo = new TelegramBotTypes.ChatFullInfo
        {
            Id = chatId,
            Type = TelegramBot.Types.Enums.ChatType.Supergroup,
            Title = title,
            Photo = chatPhoto
        };

        _mockChatHandler.GetChatAsync(chatId, Arg.Any<CancellationToken>()).Returns(chatInfo);
    }

    private void SetupFileDownload(string fileId, string filePath)
    {
        var file = new TelegramBotTypes.TGFile
        {
            FileId = fileId,
            FilePath = filePath,
            FileSize = TestImageBytes.Length
        };

        _mockMediaHandler.GetFileAsync(fileId, Arg.Any<CancellationToken>()).Returns(file);

        // Mock the download to write real image bytes to the stream
        // This flows through MockFileSystem and can be processed by ImageSharp
        _mockMediaHandler.DownloadFileAsync(filePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                stream.Write(TestImageBytes);
                return Task.CompletedTask;
            });
    }

    #endregion
}

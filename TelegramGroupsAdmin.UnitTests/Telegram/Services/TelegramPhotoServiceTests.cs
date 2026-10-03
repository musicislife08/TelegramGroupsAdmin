using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SkiaSharp;
using TelegramBot = Telegram.Bot;
using TelegramBotTypes = Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for TelegramPhotoService's undecodable-download handling.
/// Unlike BotMediaService, this service does real System.IO against a temp directory
/// (it does not go through IFileSystem), so these tests use real files.
/// </summary>
[TestFixture]
public class TelegramPhotoServiceTests
{
    private const long TestUserId = 12345L;
    private const long TestChatId = -100123456789L;
    private const string TestFileId = "AgACAgIAAxkBAAI";
    private const string TestFileUniqueId = "AQADAgATunique";
    private const string TestFilePath = "photos/file_123.jpg";

    private static readonly byte[] TestImageBytes = CreateTestImage();

    private IBotMediaService _mockMediaService = null!;
    private IBotChatService _mockChatService = null!;
    private ILogger<TelegramPhotoService> _mockLogger = null!;
    private TelegramPhotoService _service = null!;

    private string _tempDir = null!;
    private string _userPhotosPath = null!;
    private string _chatIconsPath = null!;

    private static byte[] CreateTestImage()
    {
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        using var ms = new MemoryStream();
        bitmap.Encode(ms, SKEncodedImageFormat.Jpeg, 100);
        return ms.ToArray();
    }

    [SetUp]
    public void SetUp()
    {
        _mockMediaService = Substitute.For<IBotMediaService>();
        _mockChatService = Substitute.For<IBotChatService>();
        _mockLogger = Substitute.For<ILogger<TelegramPhotoService>>();

        _tempDir = Path.Combine(Path.GetTempPath(), $"TelegramPhotoServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _userPhotosPath = Path.Combine(_tempDir, "media", "user_photos");
        _chatIconsPath = Path.Combine(_tempDir, "media", "chat_icons");

        var options = Options.Create(new AppOptions { DataPath = _tempDir });

        _service = new TelegramPhotoService(
            _mockLogger,
            _mockMediaService,
            _mockChatService,
            new SkiaImageProcessor(),
            options);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException ex)
            {
                // Best-effort cleanup: a leftover temp directory is harmless and will be
                // picked up by the OS's normal temp-file housekeeping.
                TestContext.Out.WriteLine($"Could not delete temp directory {_tempDir}: {ex.Message}");
            }
        }
    }

    #region Undecodable Download Tests

    [Test]
    public async Task GetUserPhotoWithMetadataAsync_UndecodableDownload_ReturnsNullAndLeavesNoFile()
    {
        // Arrange - Telegram serves a photo, but the download is not a decodable image
        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);
        SetupCorruptFileDownload(TestFileId, TestFilePath);

        // Act
        var result = await _service.GetUserPhotoWithMetadataAsync(TestUserId, knownPhotoId: null);

        // Assert - failure reported, and no broken 0-byte file is left at the target
        Assert.That(result, Is.Null);
        var expectedPath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        Assert.That(File.Exists(expectedPath), Is.False);
    }

    [Test]
    public async Task GetChatIconAsync_UndecodableDownload_ReturnsNullAndLeavesNoFile()
    {
        // Arrange - Telegram serves a chat photo, but the download is not a decodable image
        SetupChatHasPhoto(TestChatId, "Test Chat", TestFileId);
        SetupCorruptFileDownload(TestFileId, TestFilePath);

        // Act
        var result = await _service.GetChatIconAsync(ChatIdentity.FromId(TestChatId));

        // Assert - failure reported, and no broken 0-byte file is left at the target
        Assert.That(result, Is.Null);
        var expectedPath = Path.Combine(_chatIconsPath, $"{Math.Abs(TestChatId)}.jpg");
        Assert.That(File.Exists(expectedPath), Is.False);
    }

    [Test]
    public async Task GetUserPhotoWithMetadataAsync_RegenerationFailsOverExistingCachedPhoto_PreservesOriginal()
    {
        // Arrange - a good cached photo already exists
        SetupUserHasProfilePhoto(TestFileId, TestFileUniqueId);
        SetupFileDownload(TestFileId, TestFilePath);
        var initial = await _service.GetUserPhotoWithMetadataAsync(TestUserId, knownPhotoId: null);
        Assert.That(initial, Is.Not.Null, "Precondition: initial cache must succeed");

        var cachedPath = Path.Combine(_userPhotosPath, $"{TestUserId}.jpg");
        var originalBytes = await File.ReadAllBytesAsync(cachedPath);

        // Act - the user's photo changed on Telegram's side, but the re-download is undecodable
        const string newFileUniqueId = "AQADAgATnew_unique";
        SetupUserHasProfilePhoto(TestFileId, newFileUniqueId);
        SetupCorruptFileDownload(TestFileId, TestFilePath);
        var result = await _service.GetUserPhotoWithMetadataAsync(TestUserId, knownPhotoId: TestFileUniqueId);

        // Assert - failure reported, and the previously-cached photo survives untouched
        Assert.That(result, Is.Null);
        var bytesAfter = await File.ReadAllBytesAsync(cachedPath);
        Assert.That(bytesAfter, Is.EqualTo(originalBytes), "Original cached photo must survive a failed regeneration");
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

        _mockMediaService.GetUserProfilePhotosAsync(TestUserId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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

        _mockChatService.GetChatAsync(chatId, Arg.Any<CancellationToken>()).Returns(chatInfo);
    }

    private void SetupFileDownload(string fileId, string filePath)
    {
        var file = new TelegramBotTypes.TGFile
        {
            FileId = fileId,
            FilePath = filePath,
            FileSize = TestImageBytes.Length
        };

        _mockMediaService.GetFileAsync(fileId, Arg.Any<CancellationToken>()).Returns(file);

        _mockMediaService.DownloadFileAsync(filePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                stream!.Write(TestImageBytes);
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Mocks a download that succeeds at the Telegram-API level but delivers bytes that
    /// are not a decodable image (e.g. a truncated body or an HTML error page).
    /// </summary>
    private void SetupCorruptFileDownload(string fileId, string filePath)
    {
        var file = new TelegramBotTypes.TGFile
        {
            FileId = fileId,
            FilePath = filePath,
            FileSize = 12
        };

        _mockMediaService.GetFileAsync(fileId, Arg.Any<CancellationToken>()).Returns(file);

        _mockMediaService.DownloadFileAsync(filePath, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                stream!.Write("not an image"u8.ToArray());
                return Task.CompletedTask;
            });
    }

    #endregion
}

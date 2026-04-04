using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// Unit tests for MediaUtilities.ValidateMediaPath
/// Tests the static utility method directly without database dependencies
/// </summary>
[TestFixture]
public class MediaUtilitiesTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temp directory for test files
        _tempDir = Path.Combine(Path.GetTempPath(), $"MediaUtilitiesTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "media", "video"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "media", "audio"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "media", "sticker"));
    }

    [TearDown]
    public void TearDown()
    {
        // Cleanup temp directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region ValidateMediaPath - Passthrough Cases

    [Test]
    public void ValidateMediaPath_NullMediaLocalPath_ReturnsNullPassthrough()
    {
        // Arrange
        string? mediaLocalPath = null;
        int? mediaType = 1; // Animation

        // Act
        var result = MediaUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - null input returns null (passthrough, no validation performed)
            Assert.That(result, Is.Null);
            Assert.That(fullPath, Is.Null, "fullPathOut should be null when no validation performed");
        }
    }

    [Test]
    public void ValidateMediaPath_EmptyMediaLocalPath_ReturnsEmptyPassthrough()
    {
        // Arrange
        var mediaLocalPath = "";
        int? mediaType = 1; // Animation

        // Act
        var result = MediaUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - empty input returns empty (passthrough, no validation performed)
            Assert.That(result, Is.EqualTo(""));
            Assert.That(fullPath, Is.Null, "fullPathOut should be null when no validation performed");
        }
    }

    [Test]
    public void ValidateMediaPath_NullMediaType_ReturnsPathPassthrough()
    {
        // Arrange
        var mediaLocalPath = "test_video.mp4";
        int? mediaType = null;

        // Act
        var result = MediaUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - null mediaType means we can't construct the path, passthrough
            Assert.That(result, Is.EqualTo(mediaLocalPath));
            Assert.That(fullPath, Is.Null, "fullPathOut should be null when no validation performed");
        }
    }

    #endregion

    #region ValidateMediaPath - File Exists

    [Test]
    public void ValidateMediaPath_FileExists_ReturnsPath()
    {
        // Arrange - create actual file
        var filename = "test_animation.mp4";
        var fullFilePath = Path.Combine(_tempDir, "media", "video", filename);
        File.WriteAllText(fullFilePath, "test content");
        int mediaType = 1; // Animation -> "video" subdirectory

        // Act
        var result = MediaUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - file exists, return the path
            Assert.That(result, Is.EqualTo(filename));
            Assert.That(fullPath, Is.EqualTo(fullFilePath));
        }
    }

    [Test]
    public void ValidateMediaPath_VideoType_UsesVideoSubdirectory()
    {
        // Arrange
        var filename = "test_video.mp4";
        var fullFilePath = Path.Combine(_tempDir, "media", "video", filename);
        File.WriteAllText(fullFilePath, "test content");
        int mediaType = 2; // Video -> "video" subdirectory

        // Act
        var result = MediaUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.EqualTo(filename));
            Assert.That(fullPath, Does.Contain(Path.Combine("media", "video")));
        }
    }

    [Test]
    public void ValidateMediaPath_AudioType_UsesAudioSubdirectory()
    {
        // Arrange
        var filename = "test_audio.mp3";
        Directory.CreateDirectory(Path.Combine(_tempDir, "media", "audio"));
        var fullFilePath = Path.Combine(_tempDir, "media", "audio", filename);
        File.WriteAllText(fullFilePath, "test content");
        int mediaType = 3; // Audio -> "audio" subdirectory

        // Act
        var result = MediaUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.EqualTo(filename));
            Assert.That(fullPath, Does.Contain(Path.Combine("media", "audio")));
        }
    }

    #endregion

    #region ValidateMediaPath - File Missing

    [Test]
    public void ValidateMediaPath_FileMissing_ReturnsNull()
    {
        // Arrange - file does not exist
        var filename = "nonexistent_video.mp4";
        int mediaType = 1; // Animation

        // Act
        var result = MediaUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - validation failed, return null
            Assert.That(result, Is.Null);
            Assert.That(fullPath, Is.Not.Null, "fullPathOut should contain the path that was checked");
        }
        Assert.That(fullPath, Does.EndWith(filename));
    }

    [Test]
    public void ValidateMediaPath_FileMissing_FullPathOutContainsCheckedPath()
    {
        // Arrange
        var filename = "missing_sticker.webp";
        int mediaType = 5; // Sticker -> "sticker" subdirectory

        // Act
        var result = MediaUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert - fullPath should show what was checked for debugging
            Assert.That(result, Is.Null);
            Assert.That(fullPath, Does.Contain(Path.Combine("media", "sticker", filename)));
        }
    }

    #endregion

    #region GetMediaSubdirectory

    [TestCase(1, "video", Description = "Animation maps to video")]
    [TestCase(2, "video", Description = "Video maps to video")]
    [TestCase(6, "video", Description = "VideoNote maps to video")]
    [TestCase(3, "audio", Description = "Audio maps to audio")]
    [TestCase(4, "audio", Description = "Voice maps to audio")]
    [TestCase(5, "sticker", Description = "Sticker maps to sticker")]
    [TestCase(7, "document", Description = "Document maps to document")]
    [TestCase(0, "other", Description = "None maps to other")]
    [TestCase(99, "other", Description = "Unknown maps to other")]
    public void GetMediaSubdirectory_ReturnsCorrectSubdirectory(int mediaType, string expected)
    {
        var result = MediaUtilities.GetMediaSubdirectory(mediaType);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region IsVideoContent - Magic Byte Detection

    [Test]
    public void IsVideoContent_Mp4FtypSignature_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllBytes(path, CreateMinimalMp4Bytes());

        Assert.That(MediaUtilities.IsVideoContent(path), Is.True);
    }

    [Test]
    public void IsVideoContent_WebMEbmlSignature_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllBytes(path, CreateMinimalWebMBytes());

        Assert.That(MediaUtilities.IsVideoContent(path), Is.True);
    }

    [Test]
    public void IsVideoContent_AviRiffSignature_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllBytes(path, CreateMinimalAviBytes());

        Assert.That(MediaUtilities.IsVideoContent(path), Is.True);
    }

    [Test]
    public void IsVideoContent_RealGifBytes_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "test.gif");
        File.WriteAllBytes(path,
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
            0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00
        ]);

        Assert.That(MediaUtilities.IsVideoContent(path), Is.False);
    }

    [Test]
    public void IsVideoContent_FileShorterThan4Bytes_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "tiny.dat");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

        Assert.That(MediaUtilities.IsVideoContent(path), Is.False);
    }

    [Test]
    public void IsVideoContent_EmptyFile_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "empty.dat");
        File.WriteAllBytes(path, []);

        Assert.That(MediaUtilities.IsVideoContent(path), Is.False);
    }

    [Test]
    public void IsVideoContent_NonexistentFile_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "doesnotexist.dat");

        Assert.That(MediaUtilities.IsVideoContent(path), Is.False);
    }

    [Test]
    public void IsVideoContent_FtypAtWrongOffset_ReturnsFalse()
    {
        // "ftyp" at offset 0 instead of offset 4 — should NOT match
        var path = Path.Combine(_tempDir, "wrong_offset.dat");
        File.WriteAllBytes(path,
        [
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ]);

        Assert.That(MediaUtilities.IsVideoContent(path), Is.False);
    }

    #endregion

    #region VideoExtensions

    [TestCase(".mp4")]
    [TestCase(".webm")]
    [TestCase(".mov")]
    [TestCase(".avi")]
    [TestCase(".mkv")]
    [TestCase(".m4v")]
    public void VideoExtensions_ContainsExpectedExtension(string ext)
    {
        Assert.That(MediaUtilities.VideoExtensions.Contains(ext), Is.True);
    }

    [TestCase(".gif")]
    [TestCase(".png")]
    [TestCase(".jpg")]
    public void VideoExtensions_DoesNotContainImageExtensions(string ext)
    {
        Assert.That(MediaUtilities.VideoExtensions.Contains(ext), Is.False);
    }

    [Test]
    public void VideoExtensions_IsCaseInsensitive()
    {
        Assert.That(MediaUtilities.VideoExtensions.Contains(".MP4"), Is.True);
    }

    #endregion

    #region Magic Byte Helpers

    private static byte[] CreateMinimalMp4Bytes() =>
    [
        0x00, 0x00, 0x00, 0x1C,       // box size
        0x66, 0x74, 0x79, 0x70,       // "ftyp"
        0x69, 0x73, 0x6F, 0x6D,       // brand: "isom"
        0x00, 0x00, 0x02, 0x00,
        0x69, 0x73, 0x6F, 0x6D,
        0x69, 0x73, 0x6F, 0x32,
        0x6D, 0x70, 0x34, 0x31,
    ];

    private static byte[] CreateMinimalWebMBytes() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,       // EBML header
        0x93, 0x42, 0x86, 0x81,
        0x01, 0x42, 0xF7, 0x81,
        0x01, 0x42, 0xF2, 0x81,
    ];

    private static byte[] CreateMinimalAviBytes() =>
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F',  // "RIFF"
        0x00, 0x00, 0x00, 0x00,                       // file size (don't care)
        (byte)'A', (byte)'V', (byte)'I', (byte)' ',   // "AVI "
    ];

    #endregion
}

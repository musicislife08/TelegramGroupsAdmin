using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Utilities;

/// <summary>
/// Unit tests for MediaPathUtilities.ValidateMediaPath
/// Tests the static utility method directly without database dependencies
/// </summary>
[TestFixture]
public class MediaPathUtilitiesTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temp directory for test files
        _tempDir = Path.Combine(Path.GetTempPath(), $"MediaPathUtilitiesTests_{Guid.NewGuid():N}");
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
        var result = MediaPathUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(mediaLocalPath, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.ValidateMediaPath(filename, mediaType, _tempDir, out var fullPath);

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
        var result = MediaPathUtilities.GetMediaSubdirectory(mediaType);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion
}

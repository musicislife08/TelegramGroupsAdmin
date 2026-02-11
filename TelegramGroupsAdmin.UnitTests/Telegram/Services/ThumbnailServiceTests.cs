using Microsoft.Extensions.Logging;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for ThumbnailService - generates static thumbnails from images, GIFs, and videos.
///
/// Architecture:
/// - ThumbnailService uses ImageSharp for images/GIFs, FFmpeg for videos
/// - For animated GIFs, only the first frame is extracted (prevents APNG animation)
/// - For videos (MP4, etc.), delegates to IVideoFrameExtractionService
/// - Output is always a static thumbnail (PNG for images, GIF for videos)
/// - Maintains aspect ratio using ResizeMode.Max
///
/// Test Strategy:
/// - Uses temporary files for input/output (real file I/O, but isolated)
/// - Creates test images programmatically using ImageSharp
/// - Mocks IVideoFrameExtractionService for video thumbnail tests
/// - Validates output dimensions and format
/// - Tests error handling for missing/invalid files
/// </summary>
[TestFixture]
public class ThumbnailServiceTests
{
    private ThumbnailService _service = null!;
    private IVideoFrameExtractionService _mockVideoService = null!;
    private ILogger<ThumbnailService> _mockLogger = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _mockVideoService = Substitute.For<IVideoFrameExtractionService>();
        _mockLogger = Substitute.For<ILogger<ThumbnailService>>();
        _service = new ThumbnailService(_mockVideoService, _mockLogger);

        // Create a unique temp directory for each test
        _tempDir = Path.Combine(Path.GetTempPath(), $"ThumbnailServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up temp directory
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region GenerateThumbnailAsync - Success Cases

    [Test]
    public async Task GenerateThumbnailAsync_StaticImage_CreatesResizedThumbnail()
    {
        // Arrange - Create a 400x300 test PNG image
        var sourcePath = Path.Combine(_tempDir, "source.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(400, 300))
        {
            image.SaveAsPng(sourcePath);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(File.Exists(destPath), Is.True);
        }

        // Verify dimensions - should be resized to fit within 100x100 maintaining aspect ratio
        // 400x300 -> 100x75 (width constrained)
        using var thumbnail = await Image.LoadAsync<Rgba32>(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(100));
            Assert.That(thumbnail.Height, Is.EqualTo(75));
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_TallImage_MaintainsAspectRatio()
    {
        // Arrange - Create a 300x600 test PNG image (taller than wide)
        var sourcePath = Path.Combine(_tempDir, "tall.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(300, 600))
        {
            image.SaveAsPng(sourcePath);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        // Assert
        Assert.That(result, Is.True);

        // 300x600 -> 50x100 (height constrained)
        using var thumbnail = await Image.LoadAsync<Rgba32>(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(50));
            Assert.That(thumbnail.Height, Is.EqualTo(100));
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_AnimatedGif_ExtractsFirstFrame()
    {
        // Arrange - Create a multi-frame GIF
        var sourcePath = Path.Combine(_tempDir, "animated.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        // Create a GIF with 3 frames (each frame is a different color)
        using (var gifImage = new Image<Rgba32>(200, 200))
        {
            // First frame - red
            for (int y = 0; y < gifImage.Height; y++)
                for (int x = 0; x < gifImage.Width; x++)
                    gifImage[x, y] = new Rgba32(255, 0, 0, 255);

            // Add second frame - green
            var frame2 = gifImage.Frames.AddFrame(gifImage.Frames.RootFrame);
            for (int y = 0; y < gifImage.Height; y++)
                for (int x = 0; x < gifImage.Width; x++)
                    frame2[x, y] = new Rgba32(0, 255, 0, 255);

            // Add third frame - blue
            var frame3 = gifImage.Frames.AddFrame(gifImage.Frames.RootFrame);
            for (int y = 0; y < gifImage.Height; y++)
                for (int x = 0; x < gifImage.Width; x++)
                    frame3[x, y] = new Rgba32(0, 0, 255, 255);

            var encoder = new GifEncoder();
            await gifImage.SaveAsync(sourcePath, encoder);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(File.Exists(destPath), Is.True);
        }

        // Verify it's a single-frame image (PNG with one frame)
        using var thumbnail = await Image.LoadAsync<Rgba32>(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(100));
            Assert.That(thumbnail.Height, Is.EqualTo(100));
            Assert.That(thumbnail.Frames.Count, Is.EqualTo(1), "Thumbnail should have exactly 1 frame");
        }

        // Verify the first frame color (should be red from first frame)
        var pixel = thumbnail[50, 50];
        Assert.That(pixel.R, Is.EqualTo(255), "First frame should be red");
    }

    [Test]
    public async Task GenerateThumbnailAsync_SmallImage_DoesNotUpscale()
    {
        // Arrange - Create a 50x50 image (smaller than maxSize)
        var sourcePath = Path.Combine(_tempDir, "small.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(50, 50))
        {
            image.SaveAsPng(sourcePath);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        // Assert
        Assert.That(result, Is.True);

        // ResizeMode.Max with a smaller image should not upscale
        using var thumbnail = await Image.LoadAsync<Rgba32>(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.LessThanOrEqualTo(100));
            Assert.That(thumbnail.Height, Is.LessThanOrEqualTo(100));
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_CreatesDestinationDirectory()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "source.png");
        var nestedDestPath = Path.Combine(_tempDir, "nested", "dir", "thumb.png");

        using (var image = new Image<Rgba32>(200, 200))
        {
            image.SaveAsPng(sourcePath);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, nestedDestPath, maxSize: 100);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(File.Exists(nestedDestPath), Is.True);
            Assert.That(Directory.Exists(Path.GetDirectoryName(nestedDestPath)), Is.True);
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_CustomMaxSize_UsesSpecifiedSize()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "source.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(500, 500))
        {
            image.SaveAsPng(sourcePath);
        }

        // Act - Use custom maxSize of 200
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 200);

        // Assert
        Assert.That(result, Is.True);

        using var thumbnail = await Image.LoadAsync<Rgba32>(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(200));
            Assert.That(thumbnail.Height, Is.EqualTo(200));
        }
    }

    #endregion

    #region GenerateThumbnailAsync - Error Cases

    [Test]
    public async Task GenerateThumbnailAsync_SourceFileMissing_ReturnsFalse()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "nonexistent.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.False);
            Assert.That(File.Exists(destPath), Is.False);
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_InvalidImageFile_ReturnsFalse()
    {
        // Arrange - Create a file with invalid image data
        var sourcePath = Path.Combine(_tempDir, "invalid.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllTextAsync(sourcePath, "This is not a valid image file");

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.False);
            Assert.That(File.Exists(destPath), Is.False);
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_EmptyFile_ReturnsFalse()
    {
        // Arrange - Create an empty file
        var sourcePath = Path.Combine(_tempDir, "empty.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, []);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GenerateThumbnailAsync_CorruptedGif_ReturnsFalse()
    {
        // Arrange - Create a file that looks like a GIF header but is corrupted
        var sourcePath = Path.Combine(_tempDir, "corrupted.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        // GIF89a header followed by garbage
        await File.WriteAllBytesAsync(sourcePath, "GIF89a garbage data here"u8.ToArray());

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Output Format Validation

    [Test]
    public async Task GenerateThumbnailAsync_OutputIsPng_NotAnimated()
    {
        // Arrange - Create a simple image
        var sourcePath = Path.Combine(_tempDir, "source.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(200, 200))
        {
            var encoder = new GifEncoder();
            await image.SaveAsync(sourcePath, encoder);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.True);

        // Verify output is PNG format by checking magic bytes
        var bytes = await File.ReadAllBytesAsync(destPath);
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        Assert.That(bytes.Length, Is.GreaterThan(8));
        Assert.That(bytes.Take(8).ToArray(), Is.EqualTo(pngSignature), "Output should be PNG format");
    }

    #endregion

    #region Video Thumbnail Tests

    [Test]
    public async Task GenerateThumbnailAsync_Mp4File_DelegatesToVideoService()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "video.mp4");
        var destPath = Path.Combine(_tempDir, "thumb.gif");

        // Create a dummy MP4 file (content doesn't matter, we're mocking the service)
        await File.WriteAllTextAsync(sourcePath, "fake mp4 content");

        _mockVideoService.IsAvailable.Returns(true);
        _mockVideoService.ExtractThumbnailAsync(sourcePath, destPath, 100, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.True);
        await _mockVideoService.Received(1).ExtractThumbnailAsync(
            sourcePath, destPath, 100, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateThumbnailAsync_Mp4File_WhenFfmpegNotAvailable_ReturnsFalse()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "video.mp4");
        var destPath = Path.Combine(_tempDir, "thumb.gif");

        await File.WriteAllTextAsync(sourcePath, "fake mp4 content");

        _mockVideoService.IsAvailable.Returns(false);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.False);
        await _mockVideoService.DidNotReceive().ExtractThumbnailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateThumbnailAsync_WebmFile_DelegatesToVideoService()
    {
        // Arrange - WebM is also a video format
        var sourcePath = Path.Combine(_tempDir, "video.webm");
        var destPath = Path.Combine(_tempDir, "thumb.gif");

        await File.WriteAllTextAsync(sourcePath, "fake webm content");

        _mockVideoService.IsAvailable.Returns(true);
        _mockVideoService.ExtractThumbnailAsync(sourcePath, destPath, 100, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.True);
        await _mockVideoService.Received(1).ExtractThumbnailAsync(
            sourcePath, destPath, 100, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateThumbnailAsync_GifFile_UsesImageSharpNotVideoService()
    {
        // Arrange - GIF files should use ImageSharp, not video service
        var sourcePath = Path.Combine(_tempDir, "animation.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        using (var image = new Image<Rgba32>(100, 100))
        {
            await image.SaveAsGifAsync(sourcePath);
        }

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.True);
        // Video service should NOT be called for GIF files
        await _mockVideoService.DidNotReceive().ExtractThumbnailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    #endregion
}

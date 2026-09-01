using Microsoft.Extensions.Logging;
using NSubstitute;
using SkiaSharp;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.UnitTests.TestHelpers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for ThumbnailService - generates static thumbnails from images, GIFs, and videos.
///
/// Architecture:
/// - ThumbnailService uses IImageProcessor for images/GIFs, FFmpeg for videos
/// - For animated GIFs, only the first frame is extracted (Skia's decode collapses to frame 0)
/// - For videos (MP4, etc.), delegates to IVideoFrameExtractionService
/// - Output is always a static thumbnail (PNG for images, GIF for videos)
/// - Maintains aspect ratio using a max-dimension scale-down
///
/// Test Strategy:
/// - Uses temporary files for input/output (real file I/O, but isolated)
/// - Creates test images programmatically using SkiaSharp
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
        _service = new ThumbnailService(_mockVideoService, new SkiaImageProcessor(), _mockLogger);

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

    /// <summary>
    /// Writes a simple test image (a solid background with a circle) to disk using SkiaSharp.
    /// </summary>
    private static void WriteImage(string path, int width, int height, SKEncodedImageFormat format, int quality = 100)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }

        using var fs = File.Create(path);
        bitmap.Encode(fs, format, quality);
    }

    #region GenerateThumbnailAsync - Success Cases

    [Test]
    public async Task GenerateThumbnailAsync_StaticImage_CreatesResizedThumbnail()
    {
        // Arrange - Create a 400x300 test PNG image
        var sourcePath = Path.Combine(_tempDir, "source.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        WriteImage(sourcePath, 400, 300, SKEncodedImageFormat.Png);

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
        using var thumbnail = SKBitmap.Decode(destPath);
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

        WriteImage(sourcePath, 300, 600, SKEncodedImageFormat.Png);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        // Assert
        Assert.That(result, Is.True);

        // 300x600 -> 50x100 (height constrained)
        using var thumbnail = SKBitmap.Decode(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(50));
            Assert.That(thumbnail.Height, Is.EqualTo(100));
        }
    }

    [Test]
    public async Task GenerateThumbnailAsync_AnimatedGif_ExtractsFirstFrame()
    {
        // Arrange - a hand-built 2-frame GIF (frame 0 red, frame 1 blue)
        var sourcePath = Path.Combine(_tempDir, "animated.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, GifTestData.TwoFrameRedThenBlue());

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(File.Exists(destPath), Is.True);
        }

        // Verify it decoded to a single-frame static PNG, upscaled from the 4x4 source
        using var thumbnail = SKBitmap.Decode(destPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thumbnail.Width, Is.EqualTo(100));
            Assert.That(thumbnail.Height, Is.EqualTo(100));
        }

        // Verify the first frame colour (should be red from frame 0)
        var pixel = thumbnail.GetPixel(50, 50);
        Assert.That(pixel.Red, Is.EqualTo(255), "First frame should be red");
    }

    [Test]
    public async Task GenerateThumbnailAsync_SmallImage_DoesNotUpscale()
    {
        // Arrange - Create a 50x50 image (smaller than maxSize)
        var sourcePath = Path.Combine(_tempDir, "small.png");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        WriteImage(sourcePath, 50, 50, SKEncodedImageFormat.Png);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 100);

        // Assert
        Assert.That(result, Is.True);

        // A smaller image should not be upscaled beyond maxSize
        using var thumbnail = SKBitmap.Decode(destPath);
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

        WriteImage(sourcePath, 200, 200, SKEncodedImageFormat.Png);

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

        WriteImage(sourcePath, 500, 500, SKEncodedImageFormat.Png);

        // Act - Use custom maxSize of 200
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath, maxSize: 200);

        // Assert
        Assert.That(result, Is.True);

        using var thumbnail = SKBitmap.Decode(destPath);
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
        // Arrange - Create a simple animated GIF source
        var sourcePath = Path.Combine(_tempDir, "source.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, GifTestData.TwoFrameRedThenBlue());

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
    public async Task GenerateThumbnailAsync_GifFile_UsesImageProcessorNotVideoService()
    {
        // Arrange - GIF files should use the image processor, not video service
        var sourcePath = Path.Combine(_tempDir, "animation.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, GifTestData.TwoFrameRedThenBlue());

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert
        Assert.That(result, Is.True);
        // Video service should NOT be called for GIF files
        await _mockVideoService.DidNotReceive().ExtractThumbnailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Magic Byte Detection - Video Content Disguised as GIF

    [Test]
    public async Task GenerateThumbnailAsync_GifExtensionWithMp4Content_DelegatesToVideoService()
    {
        // Arrange - File named .gif but containing MP4 magic bytes (ftyp at offset 4)
        var sourcePath = Path.Combine(_tempDir, "disguised.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, CreateMinimalMp4Bytes());

        _mockVideoService.IsAvailable.Returns(true);
        _mockVideoService.ExtractThumbnailAsync(sourcePath, destPath, 100, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert - Should route to video service despite .gif extension
        Assert.That(result, Is.True);
        await _mockVideoService.Received(1).ExtractThumbnailAsync(
            sourcePath, destPath, 100, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GenerateThumbnailAsync_GifExtensionWithWebMContent_DelegatesToVideoService()
    {
        // Arrange - File named .gif but containing WebM/EBML magic bytes
        var sourcePath = Path.Combine(_tempDir, "disguised.gif");
        var destPath = Path.Combine(_tempDir, "thumb.png");

        await File.WriteAllBytesAsync(sourcePath, CreateMinimalWebMBytes());

        _mockVideoService.IsAvailable.Returns(true);
        _mockVideoService.ExtractThumbnailAsync(sourcePath, destPath, 100, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.GenerateThumbnailAsync(sourcePath, destPath);

        // Assert - Should route to video service despite .gif extension
        Assert.That(result, Is.True);
        await _mockVideoService.Received(1).ExtractThumbnailAsync(
            sourcePath, destPath, 100, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates bytes with a valid MP4 file signature (ftyp box).
    /// Only the magic bytes matter — the rest is padding since FFmpeg is mocked.
    /// </summary>
    private static byte[] CreateMinimalMp4Bytes() =>
    [
        0x00, 0x00, 0x00, 0x1C,       // box size (28 bytes)
        0x66, 0x74, 0x79, 0x70,       // "ftyp" — MP4/MOV/M4V signature
        0x69, 0x73, 0x6F, 0x6D,       // brand: "isom"
        0x00, 0x00, 0x02, 0x00,       // minor version
        0x69, 0x73, 0x6F, 0x6D,       // compatible brand: "isom"
        0x69, 0x73, 0x6F, 0x32,       // compatible brand: "iso2"
        0x6D, 0x70, 0x34, 0x31,       // compatible brand: "mp41"
    ];

    /// <summary>
    /// Creates bytes with a valid WebM/MKV file signature (EBML header).
    /// Only the magic bytes matter — the rest is padding since FFmpeg is mocked.
    /// </summary>
    private static byte[] CreateMinimalWebMBytes() =>
    [
        0x1A, 0x45, 0xDF, 0xA3,       // EBML header — WebM/MKV signature
        0x93, 0x42, 0x86, 0x81,       // EBML version element
        0x01, 0x42, 0xF7, 0x81,       // EBML read version element
        0x01, 0x42, 0xF2, 0x81,       // padding
    ];

    #endregion
}

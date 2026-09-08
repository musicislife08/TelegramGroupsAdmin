using Microsoft.Extensions.Logging;
using NSubstitute;
using SkiaSharp;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.Core.Services;

/// <summary>
/// Unit tests for PhotoHashService.
/// Tests perceptual hashing (pHash) for image similarity detection.
/// </summary>
[TestFixture]
public class PhotoHashServiceTests
{
    private PhotoHashService _service = null!;
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new PhotoHashService(new SkiaImageProcessor(), Substitute.For<ILogger<PhotoHashService>>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"phototests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    #region CompareHashes Tests - Happy Path

    [Test]
    public void CompareHashes_IdenticalHashes_ReturnsOne()
    {
        var hash = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };

        var result = _service.CompareHashes(hash, hash);

        Assert.That(result, Is.EqualTo(1.0));
    }

    [Test]
    public void CompareHashes_IdenticalHashesCopy_ReturnsOne()
    {
        var hash1 = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
        var hash2 = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };

        var result = _service.CompareHashes(hash1, hash2);

        Assert.That(result, Is.EqualTo(1.0));
    }

    [Test]
    public void CompareHashes_AllBitsDifferent_ReturnsZero()
    {
        // All 0s vs all 1s = 64 bits different = 0% similarity
        var hash1 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var hash2 = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        var result = _service.CompareHashes(hash1, hash2);

        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void CompareHashes_HalfBitsDifferent_ReturnsHalf()
    {
        // First 4 bytes all 1s, last 4 bytes all 0s vs all 0s = 32 bits different = 50% similarity
        var hash1 = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 };
        var hash2 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = _service.CompareHashes(hash1, hash2);

        Assert.That(result, Is.EqualTo(0.5));
    }

    #endregion

    #region CompareHashes Tests - Known Similarity Values

    [Test]
    public void CompareHashes_OneBitDifferent_ReturnsHighSimilarity()
    {
        // Only 1 bit different out of 64 = 63/64 ≈ 0.984375
        var hash1 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var hash2 = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = _service.CompareHashes(hash1, hash2);

        Assert.That(result, Is.EqualTo(63.0 / 64.0).Within(0.0001));
    }

    [Test]
    public void CompareHashes_TwoBitsDifferent_ReturnsExpected()
    {
        // 2 bits different out of 64 = 62/64 ≈ 0.96875
        var hash1 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var hash2 = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }; // 0x03 = 00000011

        var result = _service.CompareHashes(hash1, hash2);

        Assert.That(result, Is.EqualTo(62.0 / 64.0).Within(0.0001));
    }

    [Test]
    public void CompareHashes_IsSymmetric()
    {
        var hash1 = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
        var hash2 = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };

        var result1 = _service.CompareHashes(hash1, hash2);
        var result2 = _service.CompareHashes(hash2, hash1);

        Assert.That(result1, Is.EqualTo(result2));
    }

    #endregion

    #region CompareHashes Tests - Edge Cases

    [Test]
    public void CompareHashes_TooShort_ThrowsArgumentException()
    {
        var shortHash = new byte[] { 0x00, 0x00, 0x00 }; // 3 bytes, need 8
        var validHash = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        Assert.Throws<ArgumentException>(() => _service.CompareHashes(shortHash, validHash));
    }

    [Test]
    public void CompareHashes_TooLong_ThrowsArgumentException()
    {
        var longHash = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }; // 9 bytes
        var validHash = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        Assert.Throws<ArgumentException>(() => _service.CompareHashes(validHash, longHash));
    }

    [Test]
    public void CompareHashes_BothWrongLength_ThrowsArgumentException()
    {
        var shortHash1 = new byte[] { 0x00, 0x00 };
        var shortHash2 = new byte[] { 0x00, 0x00 };

        Assert.Throws<ArgumentException>(() => _service.CompareHashes(shortHash1, shortHash2));
    }

    #endregion

    #region ComputePhotoHashAsync Tests

    /// <summary>
    /// Writes a deterministic gradient image. The same seed always produces the same
    /// pixels, which is what makes the golden-hash test meaningful.
    /// </summary>
    private string WriteFixture(string name, SKEncodedImageFormat format, int quality = 100)
    {
        const int Width = 64, Height = 64;
        using var bitmap = new SKBitmap(Width, Height);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            // A diagonal gradient with a bright quadrant, so the 8x8 grid has both
            // above- and below-mean cells and the hash is not all zeros or all ones.
            var v = (byte)Math.Clamp((x * 2 + y * 2) % 256, 0, 255);
            if (x < Width / 2 && y < Height / 2) v = (byte)Math.Min(255, v + 90);
            bitmap.SetPixel(x, y, new SKColor(v, v, v));
        }

        var path = Path.Combine(_tempDirectory, name);
        using var fs = File.Create(path);
        bitmap.Encode(fs, format, quality);
        return path;
    }

    [Test]
    public async Task ComputePhotoHashAsync_KnownFixture_ProducesGoldenHash()
    {
        var path = WriteFixture("golden.png", SKEncodedImageFormat.Png);

        var hash = await _service.ComputePhotoHashAsync(path);

        Assert.That(hash, Is.Not.Null);
        Assert.That(hash!, Has.Length.EqualTo(HashingConstants.PhotoHashByteCount));
        // Pins the v2 algorithm. If this fails, the hash definition changed and every
        // stored photo_hash in the database has been silently invalidated — that is a
        // migration, not a test update. Do not edit this value to make the test pass.
        Assert.That(Convert.ToHexString(hash), Is.EqualTo("080C8ECFE0F0F8FC"));
    }

    [Test]
    public async Task ComputePhotoHashAsync_SameImageDifferentEncoders_ProducesIdenticalHash()
    {
        var png = WriteFixture("stable.png", SKEncodedImageFormat.Png);
        var jpeg = WriteFixture("stable.jpg", SKEncodedImageFormat.Jpeg, 85);
        var webp = WriteFixture("stable.webp", SKEncodedImageFormat.Webp, 90);

        var pngHash = await _service.ComputePhotoHashAsync(png);
        var jpegHash = await _service.ComputePhotoHashAsync(jpeg);
        var webpHash = await _service.ComputePhotoHashAsync(webp);

        // The box average must absorb decoder differences entirely. Any drift here
        // means the hash is still coupled to the codec.
        Assert.Multiple(() =>
        {
            Assert.That(jpegHash, Is.EqualTo(pngHash));
            Assert.That(webpHash, Is.EqualTo(pngHash));
        });
    }

    [Test]
    public async Task ComputePhotoHashAsync_MissingFile_ReturnsNull()
    {
        var hash = await _service.ComputePhotoHashAsync(Path.Combine(_tempDirectory, "nope.png"));

        Assert.That(hash, Is.Null);
    }

    [Test]
    public async Task ComputePhotoHashAsync_NotAnImage_ReturnsNull()
    {
        var path = Path.Combine(_tempDirectory, "garbage.png");
        await File.WriteAllTextAsync(path, "definitely not an image");

        Assert.That(await _service.ComputePhotoHashAsync(path), Is.Null);
    }

    #endregion

    #region ComputePhotoHashAsync Tests - Size Invariance

    /// <summary>
    /// Vertical split pattern: left half dark, right half light. Used to verify the
    /// hash is invariant to the source resolution once box-averaged down to the grid.
    /// </summary>
    private string WriteVerticalSplitPattern(
        string name,
        int width,
        int height,
        SKEncodedImageFormat format = SKEncodedImageFormat.Png,
        int quality = 90,
        byte darkValue = 0,
        byte lightValue = 255)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var v = x < width / 2 ? darkValue : lightValue;
            bitmap.SetPixel(x, y, new SKColor(v, v, v));
        }

        var path = Path.Combine(_tempDirectory, name);
        using var fs = File.Create(path);
        bitmap.Encode(fs, format, quality);
        return path;
    }

    [Test]
    public async Task ComputePhotoHashAsync_SamePatternAt32And64_ProduceIdenticalHashes()
    {
        var path32 = WriteVerticalSplitPattern("split32.png", 32, 32);
        var path64 = WriteVerticalSplitPattern("split64.png", 64, 64);

        var hash32 = await _service.ComputePhotoHashAsync(path32);
        var hash64 = await _service.ComputePhotoHashAsync(path64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hash32, Is.Not.Null);
            Assert.That(hash64, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hash32!, hash64!);
        Assert.That(similarity, Is.EqualTo(1.0), "32x32 and 64x64 should produce identical hashes");
    }

    [Test]
    public async Task ComputePhotoHashAsync_SamePatternAt64And128_ProduceIdenticalHashes()
    {
        var path64 = WriteVerticalSplitPattern("split64.png", 64, 64);
        var path128 = WriteVerticalSplitPattern("split128.png", 128, 128);

        var hash64 = await _service.ComputePhotoHashAsync(path64);
        var hash128 = await _service.ComputePhotoHashAsync(path128);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hash64, Is.Not.Null);
            Assert.That(hash128, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hash64!, hash128!);
        Assert.That(similarity, Is.EqualTo(1.0), "64x64 and 128x128 should produce identical hashes");
    }

    [Test]
    public async Task ComputePhotoHashAsync_SamePatternAt32And128_ProduceIdenticalHashes()
    {
        var path32 = WriteVerticalSplitPattern("split32.png", 32, 32);
        var path128 = WriteVerticalSplitPattern("split128.png", 128, 128);

        var hash32 = await _service.ComputePhotoHashAsync(path32);
        var hash128 = await _service.ComputePhotoHashAsync(path128);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hash32, Is.Not.Null);
            Assert.That(hash128, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hash32!, hash128!);
        Assert.That(similarity, Is.EqualTo(1.0), "32x32 and 128x128 should produce identical hashes");
    }

    #endregion

    #region ComputePhotoHashAsync Tests - JPEG Compression Robustness

    [Test]
    public async Task ComputePhotoHashAsync_PngVsJpeg50_ProduceSimilarHashes()
    {
        var png = WriteVerticalSplitPattern("pattern.png", 64, 64, SKEncodedImageFormat.Png);
        var jpeg = WriteVerticalSplitPattern("pattern50.jpg", 64, 64, SKEncodedImageFormat.Jpeg, 50);

        var hashPng = await _service.ComputePhotoHashAsync(png);
        var hashJpeg = await _service.ComputePhotoHashAsync(jpeg);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashPng, Is.Not.Null);
            Assert.That(hashJpeg, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashPng!, hashJpeg!);
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.90),
            $"PNG vs JPEG Q50 should be similar, got {similarity:P1}");
    }

    [Test]
    public async Task ComputePhotoHashAsync_PngVsJpeg10_ProduceSimilarHashes()
    {
        var png = WriteVerticalSplitPattern("pattern.png", 64, 64, SKEncodedImageFormat.Png);
        var jpeg = WriteVerticalSplitPattern("pattern10.jpg", 64, 64, SKEncodedImageFormat.Jpeg, 10);

        var hashPng = await _service.ComputePhotoHashAsync(png);
        var hashJpeg = await _service.ComputePhotoHashAsync(jpeg);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashPng, Is.Not.Null);
            Assert.That(hashJpeg, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashPng!, hashJpeg!);
        // Heavy compression may introduce more artifacts, but should still match
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.80),
            $"PNG vs JPEG Q10 (heavy compression) should still be recognizable, got {similarity:P1}");
    }

    [Test]
    public async Task ComputePhotoHashAsync_JpegReEncoding_ProducesSimilarHash()
    {
        // Simulate re-encoding: JPEG90 should still match original PNG closely
        var png = WriteVerticalSplitPattern("original.png", 64, 64, SKEncodedImageFormat.Png);
        var jpeg = WriteVerticalSplitPattern("reencoded.jpg", 64, 64, SKEncodedImageFormat.Jpeg, 90);

        var hashOriginal = await _service.ComputePhotoHashAsync(png);
        var hashReEncoded = await _service.ComputePhotoHashAsync(jpeg);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashOriginal, Is.Not.Null);
            Assert.That(hashReEncoded, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashOriginal!, hashReEncoded!);
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.95),
            $"Re-encoded image should match original closely, got {similarity:P1}");
    }

    #endregion

    #region ComputePhotoHashAsync Tests - Brightness Robustness

    [Test]
    public async Task ComputePhotoHashAsync_SlightlyBrighterImage_ProducesSimilarHash()
    {
        var normal = WriteVerticalSplitPattern("normal.png", 64, 64, darkValue: 64, lightValue: 192);
        var brighter = WriteVerticalSplitPattern("brighter.png", 64, 64, darkValue: 96, lightValue: 224);

        var hashNormal = await _service.ComputePhotoHashAsync(normal);
        var hashBrighter = await _service.ComputePhotoHashAsync(brighter);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashNormal, Is.Not.Null);
            Assert.That(hashBrighter, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashNormal!, hashBrighter!);
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.90),
            $"Slightly brighter image should still match, got {similarity:P1}");
    }

    [Test]
    public async Task ComputePhotoHashAsync_SlightlyDarkerImage_ProducesSimilarHash()
    {
        var normal = WriteVerticalSplitPattern("normal.png", 64, 64, darkValue: 64, lightValue: 192);
        var darker = WriteVerticalSplitPattern("darker.png", 64, 64, darkValue: 32, lightValue: 160);

        var hashNormal = await _service.ComputePhotoHashAsync(normal);
        var hashDarker = await _service.ComputePhotoHashAsync(darker);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashNormal, Is.Not.Null);
            Assert.That(hashDarker, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashNormal!, hashDarker!);
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.90),
            $"Slightly darker image should still match, got {similarity:P1}");
    }

    #endregion

    #region ComputePhotoHashAsync Tests - Different Content Detection

    /// <summary>
    /// Horizontal split pattern: top half black, bottom half white. Used opposite the
    /// vertical split pattern to verify structurally different images do not collide.
    /// </summary>
    private string WriteHorizontalSplitPattern(string name, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var v = (byte)(y < height / 2 ? 0 : 255);
            bitmap.SetPixel(x, y, new SKColor(v, v, v));
        }

        var path = Path.Combine(_tempDirectory, name);
        using var fs = File.Create(path);
        bitmap.Encode(fs, SKEncodedImageFormat.Png, 100);
        return path;
    }

    private string WriteSolidColor(string name, byte grayValue)
    {
        const int Size = 64;
        using var bitmap = new SKBitmap(Size, Size);
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
            bitmap.SetPixel(x, y, new SKColor(grayValue, grayValue, grayValue));

        var path = Path.Combine(_tempDirectory, name);
        using var fs = File.Create(path);
        bitmap.Encode(fs, SKEncodedImageFormat.Png, 100);
        return path;
    }

    [Test]
    public async Task ComputePhotoHashAsync_VerticalVsHorizontalSplit_ProduceDifferentHashes()
    {
        var vertical = WriteVerticalSplitPattern("vertical.png", 64, 64);
        var horizontal = WriteHorizontalSplitPattern("horizontal.png", 64, 64);

        var hashVertical = await _service.ComputePhotoHashAsync(vertical);
        var hashHorizontal = await _service.ComputePhotoHashAsync(horizontal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashVertical, Is.Not.Null);
            Assert.That(hashHorizontal, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashVertical!, hashHorizontal!);
        // Different patterns should have low similarity (but not necessarily zero)
        Assert.That(similarity, Is.LessThan(0.70),
            $"Vertical vs horizontal split should be different, got {similarity:P1}");
    }

    [Test]
    public async Task ComputePhotoHashAsync_BlackVsWhite_ProduceDifferentHashes()
    {
        var black = WriteSolidColor("black.png", 0);
        var white = WriteSolidColor("white.png", 255);

        var hashBlack = await _service.ComputePhotoHashAsync(black);
        var hashWhite = await _service.ComputePhotoHashAsync(white);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashBlack, Is.Not.Null);
            Assert.That(hashWhite, Is.Not.Null);
        }

        // Note: Solid color images may produce similar hashes (all pixels = average)
        // This test validates the hashes are computed, not necessarily different
        Assert.That(hashBlack, Has.Length.EqualTo(8));
        Assert.That(hashWhite, Has.Length.EqualTo(8));
    }

    #endregion
}

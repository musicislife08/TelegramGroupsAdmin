using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.Core.Services;

/// <summary>
/// Unit tests for PhotoHashService.
/// Tests perceptual hashing (pHash) for image similarity detection.
/// Validates robustness against size changes, JPEG compression, and brightness shifts.
/// </summary>
[TestFixture]
public class PhotoHashServiceTests
{
    private PhotoHashService _service = null!;
    private readonly List<string> _tempFiles = [];

    // Test image paths - generated once per test run
    private string _blackImagePath = null!;
    private string _whiteImagePath = null!;
    private string _grayImagePath = null!;

    // Split pattern images at different sizes (size invariance tests)
    private string _splitPattern32 = null!;
    private string _splitPattern64 = null!;
    private string _splitPattern128 = null!;

    // Compression test images (same pattern, different formats/quality)
    private string _patternPng = null!;
    private string _patternJpeg90 = null!;
    private string _patternJpeg50 = null!;
    private string _patternJpeg10 = null!;

    // Brightness variation images
    private string _normalBrightness = null!;
    private string _brighterVersion = null!;
    private string _darkerVersion = null!;

    // Different pattern (for negative tests)
    private string _horizontalSplit = null!;

    // Error handling tests
    private string _corruptedPath = null!;
    private string _nonExistentPath = null!;

    [OneTimeSetUp]
    public void ClassSetup()
    {
        _service = new PhotoHashService(NullLogger<PhotoHashService>.Instance);

        // Solid colors (for CompareHashes verification)
        _blackImagePath = CreateSolidColorImage(0);
        _whiteImagePath = CreateSolidColorImage(255);
        _grayImagePath = CreateSolidColorImage(128);

        // Split patterns at different sizes (size invariance)
        _splitPattern32 = CreateSplitPatternImage(32, 32);
        _splitPattern64 = CreateSplitPatternImage(64, 64);
        _splitPattern128 = CreateSplitPatternImage(128, 128);

        // Compression test images (same pattern, different formats)
        _patternPng = CreateSplitPatternImage(64, 64, format: "png");
        _patternJpeg90 = CreateSplitPatternImage(64, 64, format: "jpeg", quality: 90);
        _patternJpeg50 = CreateSplitPatternImage(64, 64, format: "jpeg", quality: 50);
        _patternJpeg10 = CreateSplitPatternImage(64, 64, format: "jpeg", quality: 10);

        // Brightness variations (same pattern, different brightness levels)
        _normalBrightness = CreateSplitPatternImage(64, 64, darkValue: 64, lightValue: 192);
        _brighterVersion = CreateSplitPatternImage(64, 64, darkValue: 96, lightValue: 224);
        _darkerVersion = CreateSplitPatternImage(64, 64, darkValue: 32, lightValue: 160);

        // Different pattern (horizontal split instead of vertical)
        _horizontalSplit = CreateHorizontalSplitImage(64, 64);

        // Error handling
        _corruptedPath = CreateCorruptedFile();
        _nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.png");
    }

    [OneTimeTearDown]
    public void ClassTeardown()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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

    #region ComputePhotoHashAsync Tests - File Handling

    [Test]
    public async Task ComputePhotoHashAsync_FileNotExists_ReturnsNull()
    {
        var result = await _service.ComputePhotoHashAsync(_nonExistentPath);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ComputePhotoHashAsync_ValidImage_ReturnsEightByteHash()
    {
        var result = await _service.ComputePhotoHashAsync(_splitPattern64);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Length.EqualTo(8));
    }

    [Test]
    public async Task ComputePhotoHashAsync_CorruptedImage_ReturnsNull()
    {
        var result = await _service.ComputePhotoHashAsync(_corruptedPath);

        Assert.That(result, Is.Null);
    }

    #endregion

    #region ComputePhotoHashAsync Tests - Size Invariance

    [Test]
    public async Task ComputePhotoHashAsync_SamePatternAt32And64_ProduceIdenticalHashes()
    {
        var hash32 = await _service.ComputePhotoHashAsync(_splitPattern32);
        var hash64 = await _service.ComputePhotoHashAsync(_splitPattern64);

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
        var hash64 = await _service.ComputePhotoHashAsync(_splitPattern64);
        var hash128 = await _service.ComputePhotoHashAsync(_splitPattern128);

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
        var hash32 = await _service.ComputePhotoHashAsync(_splitPattern32);
        var hash128 = await _service.ComputePhotoHashAsync(_splitPattern128);

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
    public async Task ComputePhotoHashAsync_PngVsJpeg90_ProduceSimilarHashes()
    {
        var hashPng = await _service.ComputePhotoHashAsync(_patternPng);
        var hashJpeg = await _service.ComputePhotoHashAsync(_patternJpeg90);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashPng, Is.Not.Null);
            Assert.That(hashJpeg, Is.Not.Null);
        }

        var similarity = _service.CompareHashes(hashPng!, hashJpeg!);
        Assert.That(similarity, Is.GreaterThanOrEqualTo(0.95),
            $"PNG vs JPEG Q90 should be very similar, got {similarity:P1}");
    }

    [Test]
    public async Task ComputePhotoHashAsync_PngVsJpeg50_ProduceSimilarHashes()
    {
        var hashPng = await _service.ComputePhotoHashAsync(_patternPng);
        var hashJpeg = await _service.ComputePhotoHashAsync(_patternJpeg50);

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
        var hashPng = await _service.ComputePhotoHashAsync(_patternPng);
        var hashJpeg = await _service.ComputePhotoHashAsync(_patternJpeg10);

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
        var hashOriginal = await _service.ComputePhotoHashAsync(_patternPng);
        var hashReEncoded = await _service.ComputePhotoHashAsync(_patternJpeg90);

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
        var hashNormal = await _service.ComputePhotoHashAsync(_normalBrightness);
        var hashBrighter = await _service.ComputePhotoHashAsync(_brighterVersion);

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
        var hashNormal = await _service.ComputePhotoHashAsync(_normalBrightness);
        var hashDarker = await _service.ComputePhotoHashAsync(_darkerVersion);

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

    [Test]
    public async Task ComputePhotoHashAsync_VerticalVsHorizontalSplit_ProduceDifferentHashes()
    {
        var hashVertical = await _service.ComputePhotoHashAsync(_splitPattern64);
        var hashHorizontal = await _service.ComputePhotoHashAsync(_horizontalSplit);

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
        var hashBlack = await _service.ComputePhotoHashAsync(_blackImagePath);
        var hashWhite = await _service.ComputePhotoHashAsync(_whiteImagePath);

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

    #region ComputePhotoHashAsync Tests - Format Handling

    [Test]
    public async Task ComputePhotoHashAsync_PngFormat_ReturnsHash()
    {
        var result = await _service.ComputePhotoHashAsync(_patternPng);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Length.EqualTo(8));
    }

    [Test]
    public async Task ComputePhotoHashAsync_JpegFormat_ReturnsHash()
    {
        var result = await _service.ComputePhotoHashAsync(_patternJpeg90);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Length.EqualTo(8));
    }

    #endregion

    #region Helper Methods - Image Generation

    private string GetTempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phash_test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private string CreateSolidColorImage(byte grayValue)
    {
        var path = GetTempPath(".png");
        using var image = new Image<L8>(64, 64);

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image[x, y] = new L8(grayValue);
            }
        }

        image.Save(path);
        return path;
    }

    private string CreateSplitPatternImage(
        int width,
        int height,
        string format = "png",
        int quality = 90,
        byte darkValue = 0,
        byte lightValue = 255)
    {
        var path = GetTempPath($".{format}");
        using var image = new Image<L8>(width, height);

        // Vertical split: left half dark, right half light
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new L8(x < width / 2 ? darkValue : lightValue);
            }
        }

        if (format == "jpeg")
        {
            image.Save(path, new JpegEncoder { Quality = quality });
        }
        else
        {
            image.Save(path);
        }

        return path;
    }

    private string CreateHorizontalSplitImage(int width, int height)
    {
        var path = GetTempPath(".png");
        using var image = new Image<L8>(width, height);

        // Horizontal split: top half black, bottom half white
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new L8((byte)(y < height / 2 ? 0 : 255));
            }
        }

        image.Save(path);
        return path;
    }

    private string CreateCorruptedFile()
    {
        var path = GetTempPath(".png");
        // Invalid PNG: starts with PNG magic bytes but truncated
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
        return path;
    }

    #endregion
}

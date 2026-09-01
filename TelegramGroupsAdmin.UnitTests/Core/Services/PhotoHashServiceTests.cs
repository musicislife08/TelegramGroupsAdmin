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
}

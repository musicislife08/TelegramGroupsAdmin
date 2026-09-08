using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using SkiaSharp;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Targeted unit tests for ProfileScanService.ResizeForVisionAsync — the vision-image
/// resize helper used before handing profile photos to the AI vision model.
///
/// The method is private, so it's exercised via reflection rather than standing up the
/// full ScanUserProfileAsync orchestration (Telegram API calls, DB access, AI scoring)
/// that isn't relevant to this narrow decode-failure behaviour.
/// </summary>
[TestFixture]
public class ProfileScanServiceResizeForVisionTests
{
    private const int VisionMaxDimension = 512;

    private static ProfileScanService CreateService() => new(
        Substitute.For<ITelegramSessionManager>(),
        Substitute.For<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        new SkiaImageProcessor(),
        NullLogger<ProfileScanService>.Instance);

    private static async Task<byte[]> InvokeResizeForVisionAsync(ProfileScanService service, Stream imageStream)
    {
        var method = typeof(ProfileScanService).GetMethod("ResizeForVisionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<byte[]>)method.Invoke(service, [imageStream, VisionMaxDimension])!;
        return await task;
    }

    /// <summary>
    /// Encodes a solid-colour PNG larger than VisionMaxDimension on both sides, so
    /// ResizeForVisionAsync's "already within bounds" short-circuit does not apply.
    /// </summary>
    private static byte[] CreateLargeImageBytes(int size = 1000)
    {
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        using var ms = new MemoryStream();
        bitmap.Encode(ms, SKEncodedImageFormat.Png, 90);
        return ms.ToArray();
    }

    /// <summary>
    /// A PNG with an intact, parseable IHDR chunk (so SKCodec.Create/ReadDimensions
    /// succeeds and reports dimensions above VisionMaxDimension) but with the tail of
    /// the compressed pixel stream corrupted, so the full pixel decode
    /// (SKBitmap.Decode, via ResizeToFitAsync) fails. This is the "header-valid but
    /// undecodable" case a truncated/corrupted Telegram download produces.
    /// </summary>
    private static byte[] CreateHeaderValidButUndecodableImageBytes()
    {
        var bytes = CreateLargeImageBytes();
        for (var i = bytes.Length - 64; i < bytes.Length; i++)
        {
            bytes[i] = 0xFF;
        }

        return bytes;
    }

    [Test]
    public async Task ResizeForVisionAsync_HeaderValidButUndecodableSource_ReturnsOriginalBytesNotEmpty()
    {
        // Arrange — sanity-check the fixture actually reproduces the target condition:
        // dimensions must be readable (header intact) but the full decode must fail.
        // This guards the test itself against Skia becoming more tolerant of the
        // corruption pattern in a future dependency bump.
        var processor = new SkiaImageProcessor();
        var badBytes = CreateHeaderValidButUndecodableImageBytes();

        using (var probe = new MemoryStream(badBytes))
        {
            var dimensions = processor.ReadDimensions(probe);
            Assert.That(dimensions, Is.Not.Null, "Fixture must have a parseable header");

            probe.Position = 0;
            using var output = new MemoryStream();
            var decoded = await processor.ResizeToFitAsync(probe, output, VisionMaxDimension, ImageEncoding.Jpeg(85));
            Assert.That(decoded, Is.False, "Fixture must fail full decode for this test to be meaningful");
        }

        var service = CreateService();
        using var imageStream = new MemoryStream(badBytes);

        // Act
        var result = await InvokeResizeForVisionAsync(service, imageStream);

        // Assert — falls back to the original bytes rather than an empty image
        Assert.That(result, Is.EqualTo(badBytes));
        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public async Task ResizeForVisionAsync_DecodableOversizedSource_ReturnsResizedBytes()
    {
        // Arrange — control case: a genuinely decodable, oversized source is resized
        // (not passed through) and the resize succeeds.
        var goodBytes = CreateLargeImageBytes();
        var service = CreateService();
        using var imageStream = new MemoryStream(goodBytes);

        // Act
        var result = await InvokeResizeForVisionAsync(service, imageStream);

        // Assert
        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Is.Not.EqualTo(goodBytes), "A decodable oversized image should be re-encoded, not passed through");

        using var resultStream = new MemoryStream(result);
        var dimensions = new SkiaImageProcessor().ReadDimensions(resultStream);
        Assert.That(dimensions, Is.Not.Null);
        Assert.That(dimensions!.Width, Is.LessThanOrEqualTo(VisionMaxDimension));
        Assert.That(dimensions.Height, Is.LessThanOrEqualTo(VisionMaxDimension));
    }
}

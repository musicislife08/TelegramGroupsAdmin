using SkiaSharp;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.UnitTests.TestHelpers;

namespace TelegramGroupsAdmin.UnitTests.Core.Imaging;

[TestFixture]
public class SkiaImageProcessorTests
{
    private SkiaImageProcessor _processor = null!;

    [SetUp]
    public void SetUp() => _processor = new SkiaImageProcessor();

    /// <summary>
    /// Builds a deterministic test image with real structure, so resize and blur
    /// have something to act on and the luminance grid is not uniform.
    /// </summary>
    private static byte[] CreateTestImage(int width, int height, SKEncodedImageFormat format, int quality = 100)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(30, 30, 40));
            using var paint = new SKPaint { IsAntialias = true };
            for (var i = 0; i < 8; i++)
            {
                paint.Color = new SKColor((byte)(i * 31), (byte)(255 - i * 28), (byte)(i * 17 + 60));
                canvas.DrawCircle(20 + i * 28, 40 + (i % 3) * 40, 22, paint);
            }
            paint.Color = SKColors.White;
            canvas.DrawRect(new SKRect(0, 0, width, 12), paint);
        }

        using var ms = new MemoryStream();
        bitmap.Encode(ms, format, quality);
        return ms.ToArray();
    }

    [Test]
    public void ReadDimensions_ValidImage_ReturnsSize()
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, SKEncodedImageFormat.Png));

        var dimensions = _processor.ReadDimensions(source);

        Assert.That(dimensions, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dimensions!.Width, Is.EqualTo(240));
            Assert.That(dimensions.Height, Is.EqualTo(160));
        });
    }

    [Test]
    public void ReadDimensions_NotAnImage_ReturnsNull()
    {
        using var source = new MemoryStream("this is not an image"u8.ToArray());

        Assert.That(_processor.ReadDimensions(source), Is.Null);
    }

    [Test]
    public async Task ResizeToFitAsync_LandscapeImage_PreservesAspectRatioWithinBound()
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, SKEncodedImageFormat.Png));
        using var destination = new MemoryStream();

        var result = await _processor.ResizeToFitAsync(source, destination, 100, ImageEncoding.Png);

        Assert.That(result, Is.True);
        destination.Position = 0;
        var dimensions = _processor.ReadDimensions(destination);
        Assert.Multiple(() =>
        {
            Assert.That(dimensions!.Width, Is.EqualTo(100));
            Assert.That(dimensions.Height, Is.EqualTo(67));
        });
    }

    [Test]
    public async Task ResizeToFillAsync_LandscapeImage_ProducesExactSquare()
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, SKEncodedImageFormat.Png));
        using var destination = new MemoryStream();

        var result = await _processor.ResizeToFillAsync(source, destination, 64, ImageEncoding.Jpeg(85));

        Assert.That(result, Is.True);
        destination.Position = 0;
        var dimensions = _processor.ReadDimensions(destination);
        Assert.Multiple(() =>
        {
            Assert.That(dimensions!.Width, Is.EqualTo(64));
            Assert.That(dimensions.Height, Is.EqualTo(64));
        });
    }

    [TestCase(SKEncodedImageFormat.Png)]
    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public async Task ResizeToFitAsync_SupportedFormats_AllDecode(SKEncodedImageFormat format)
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, format, 90));
        using var destination = new MemoryStream();

        Assert.That(await _processor.ResizeToFitAsync(source, destination, 50, ImageEncoding.Png), Is.True);
        Assert.That(destination.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task ResizeToFitAsync_AnimatedGif_UsesFirstFrame()
    {
        using var source = new MemoryStream(GifTestData.TwoFrameRedThenBlue());
        using var destination = new MemoryStream();

        Assert.That(await _processor.ResizeToFitAsync(source, destination, 4, ImageEncoding.Png), Is.True);

        destination.Position = 0;
        using var decoded = SKBitmap.Decode(destination);
        var pixel = decoded.GetPixel(1, 1);
        // Frame 0 is red, frame 1 is blue. Blue here means we picked the wrong
        // frame, which would silently change every animated thumbnail.
        Assert.Multiple(() =>
        {
            Assert.That(pixel.Red, Is.GreaterThan(200));
            Assert.That(pixel.Blue, Is.LessThan(60));
        });
    }

    [Test]
    public async Task Operations_DoNotCloseTheCallerStream()
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, SKEncodedImageFormat.Png));

        var dimensions = _processor.ReadDimensions(source);
        Assert.That(dimensions, Is.Not.Null);
        Assert.That(source.CanRead, Is.True, "ReadDimensions closed the caller's stream");

        source.Position = 0;
        using var destination = new MemoryStream();
        Assert.That(await _processor.ResizeToFitAsync(source, destination, 64, ImageEncoding.Png), Is.True,
            "a second operation on the same stream failed");
    }

    [Test]
    public async Task BlurAsync_ReducesLocalContrast()
    {
        var original = CreateTestImage(240, 160, SKEncodedImageFormat.Png);
        using var source = new MemoryStream(original);
        using var destination = new MemoryStream();

        Assert.That(await _processor.BlurAsync(source, destination, 20f, ImageEncoding.Jpeg(85)), Is.True);

        destination.Position = 0;
        using var blurred = SKBitmap.Decode(destination);
        using var sharp = SKBitmap.Decode(original);
        Assert.That(LocalContrast(blurred), Is.LessThan(LocalContrast(sharp)));
    }

    [Test]
    public async Task ResizeToFitAsync_NotAnImage_ReturnsFalse()
    {
        using var source = new MemoryStream("this is not an image"u8.ToArray());
        using var destination = new MemoryStream();

        Assert.That(await _processor.ResizeToFitAsync(source, destination, 64, ImageEncoding.Png), Is.False);
    }

    [Test]
    public void ReadLuminanceGrid_ReturnsOneBytePerCell()
    {
        using var source = new MemoryStream(CreateTestImage(240, 160, SKEncodedImageFormat.Png));

        var grid = _processor.ReadLuminanceGrid(source, 8);

        Assert.That(grid, Is.Not.Null);
        Assert.That(grid!, Has.Length.EqualTo(64));
        // A structured image must not average to a flat grid.
        Assert.That(grid.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void ReadLuminanceGrid_IsStableAcrossLossyReEncodes()
    {
        var png = CreateTestImage(240, 160, SKEncodedImageFormat.Png);
        var jpeg = CreateTestImage(240, 160, SKEncodedImageFormat.Jpeg, 85);

        using var pngStream = new MemoryStream(png);
        using var jpegStream = new MemoryStream(jpeg);

        var pngGrid = _processor.ReadLuminanceGrid(pngStream, 8)!;
        var jpegGrid = _processor.ReadLuminanceGrid(jpegStream, 8)!;

        // The box average absorbs decoder rounding; cells must land within a
        // couple of levels of each other or the hash is not decoder-stable.
        for (var i = 0; i < pngGrid.Length; i++)
            Assert.That(Math.Abs(pngGrid[i] - jpegGrid[i]), Is.LessThanOrEqualTo(3), $"cell {i} drifted");
    }

    private static double LocalContrast(SKBitmap bmp)
    {
        double total = 0;
        var n = 0;
        for (var y = 1; y < bmp.Height; y += 3)
        for (var x = 1; x < bmp.Width; x += 3)
        {
            var a = bmp.GetPixel(x, y);
            var b = bmp.GetPixel(x - 1, y - 1);
            total += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            n++;
        }
        return n == 0 ? 0 : total / n;
    }
}

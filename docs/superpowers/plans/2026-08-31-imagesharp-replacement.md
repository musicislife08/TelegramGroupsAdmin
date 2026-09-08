# ImageSharp → SkiaSharp Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `SixLabors.ImageSharp` (paid licence from v4.0.0) from the solution, replacing it with SkiaSharp behind a new `IImageProcessor` seam, and migrate the persisted perceptual hashes it produced.

**Architecture:** A new `IImageProcessor` in `TelegramGroupsAdmin.Core` owns every image operation; `SkiaImageProcessor` is its only implementation. `PhotoHashService` keeps only threshold-and-pack logic, taking a box-averaged luminance grid from the processor, so the stored hash no longer depends on any image library. Because Skia and ImageSharp resample differently, every stored hash is invalidated: an EF migration NULLs all v1 hashes, and a startup pass refills every hash it can recompute from disk.

**Tech Stack:** .NET 10, SkiaSharp 4.151.1, EF Core 10 / PostgreSQL 18, NUnit 4 + NSubstitute 6, Quartz.NET.

**Spec:** `docs/superpowers/specs/2026-08-31-imagesharp-replacement-design.md`

## Global Constraints

- **SkiaSharp version: exactly `4.151.1`** for both `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`. Versions live in `Directory.Packages.props` (Central Package Management) — `PackageReference` entries carry no `Version` attribute.
- **`SKFilterQuality` is `[Obsolete(error: true)]`.** Use `SKSamplingOptions`. Resize quality is `new SKSamplingOptions(SKCubicResampler.Mitchell)`.
- **No `SixLabors.*` package reference or `using` may remain anywhere**, tests included.
- **One type per file.** Every class, record, interface and enum gets its own file named after it.
- **No tuple returns.** Use a named record and pass the record down rather than splitting it into loose arguments.
- **Never commit to `master` or `develop`.** Work happens on `feat/523-replace-imagesharp-skiasharp`; the PR targets `develop`.
- **Conventional commits** (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`).
- **Migrations:** modify the model and `AppDbContext` FIRST, then `dotnet ef migrations add`. Apply with `dotnet run --migrate`.
- **`OpenAI` stays at `2.12.0`.** `Microsoft.Extensions.AI.OpenAI` 10.9.0 constrains it to `[2.12.0, 2.13.0)`; taking 2.13.0 trips NU1608. Do not "fix" this.
- **Solution must build with 0 warnings** and all tests must pass before the PR.

## Behaviour that must be preserved exactly

| Call site | Operation | Encoding |
|---|---|---|
| `TelegramPhotoService.ResizeImageAsync` | crop-to-fill square, 64px | JPEG **85** |
| `BotMediaService.ResizeImageAsync` | crop-to-fill square, 64px, **all I/O through `IFileSystem`** | JPEG **85** |
| `ProfileScanService.ResizeForVisionAsync` | fit within `VisionMaxDimension`, **passthrough unmodified if already within bounds** | JPEG **85** |
| `ProfileScanService.CensorProfilePhotoAsync` | Gaussian blur, `sigma = min(40, min(w,h)/6)`, rewritten in place | JPEG (default) |
| `ImageProcessingHandler` | fit within `ThumbnailSize` | JPEG **75** — ImageSharp's `JpegEncoder` default. Do not "improve" this to 85; it changes every stored thumbnail. |
| `ThumbnailService` | fit within `maxSize`, first frame only for animated | **PNG** |

---

### Task 1: `IImageProcessor` and `SkiaImageProcessor`

Adds SkiaSharp and the new seam. ImageSharp is untouched, so the solution stays green throughout.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`
- Create: `TelegramGroupsAdmin.Core/Imaging/IImageProcessor.cs`
- Create: `TelegramGroupsAdmin.Core/Imaging/SkiaImageProcessor.cs`
- Create: `TelegramGroupsAdmin.Core/Imaging/ImageDimensions.cs`
- Create: `TelegramGroupsAdmin.Core/Imaging/ImageEncoding.cs`
- Create: `TelegramGroupsAdmin.Core/Imaging/ImageFormat.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Imaging/SkiaImageProcessorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IImageProcessor` with `ImageDimensions? ReadDimensions(Stream)`, `byte[]? ReadLuminanceGrid(Stream, int)`, `Task<bool> ResizeToFitAsync(Stream, Stream, int, ImageEncoding, CancellationToken)`, `Task<bool> ResizeToFillAsync(Stream, Stream, int, ImageEncoding, CancellationToken)`, `Task<bool> BlurAsync(Stream, Stream, float, ImageEncoding, CancellationToken)`. Records `ImageDimensions(int Width, int Height)` and `ImageEncoding(ImageFormat Format, int Quality)` with statics `ImageEncoding.Png` and `ImageEncoding.Jpeg(int quality)`. Enum `ImageFormat { Png, Jpeg, Webp }`.

**Note on animated images:** SkiaSharp's `SKBitmap.Decode` returns frame 0 of an animated GIF/APNG/WebP automatically. The spec listed `ExtractFirstFrameAsync` as a separate operation; the container probe proved it is unnecessary, so it is **not** in this interface. `ResizeToFitAsync` on an animated GIF yields a static first frame by construction.

- [ ] **Step 1: Add the packages to Central Package Management**

In `Directory.Packages.props`, add alongside the other third-party entries (alphabetical, near `SendGrid`):

```xml
    <PackageVersion Include="SkiaSharp" Version="4.151.1" />
    <PackageVersion Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="4.151.1" />
```

Leave the existing `SixLabors.ImageSharp` entry and its version-hold comment in place for now; Task 5 removes them.

In `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`, add to the existing `ItemGroup` of `PackageReference` elements (no `Version` attribute — CPM supplies it):

```xml
    <PackageReference Include="SkiaSharp" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
```

Core is the right home: `PhotoHashService` lives here and `TelegramGroupsAdmin.Telegram` already references Core, so it picks SkiaSharp up transitively and needs no direct reference of its own.

- [ ] **Step 2: Write the failing tests**

Create `TelegramGroupsAdmin.UnitTests/Core/Imaging/SkiaImageProcessorTests.cs`:

```csharp
using SkiaSharp;
using TelegramGroupsAdmin.Core.Imaging;

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

    /// <summary>
    /// Minimal 2-frame GIF89a (4x4, frame 0 red, frame 1 blue). Uses the
    /// clear-code-before-every-pixel LZW form so the code width never grows past
    /// 3 bits, which needs no table bookkeeping to be valid.
    /// </summary>
    private static byte[] CreateTwoFrameGif()
    {
        const int W = 4, H = 4;
        var g = new List<byte>();

        g.AddRange("GIF89a"u8.ToArray());
        g.AddRange([W, 0, H, 0]);
        g.Add(0xF0);                        // global colour table, 2 entries
        g.AddRange([0, 0]);
        g.AddRange([0xFF, 0x00, 0x00]);     // index 0: red
        g.AddRange([0x00, 0x00, 0xFF]);     // index 1: blue

        g.AddRange([0x21, 0xFF, 0x0B]);
        g.AddRange("NETSCAPE2.0"u8.ToArray());
        g.AddRange([0x03, 0x01, 0x00, 0x00, 0x00]);

        foreach (var colourIndex in (byte[])[0, 1])
        {
            g.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
            g.AddRange([0x2C, 0, 0, 0, 0, W, 0, H, 0, 0x00]);

            g.Add(0x02);                    // LZW minimum code size
            var codes = new List<int>();
            for (var i = 0; i < W * H; i++)
            {
                codes.Add(4);               // clear
                codes.Add(colourIndex);
            }
            codes.Add(5);                   // end of information

            var bytes = new List<byte>();
            var accumulator = 0;
            var bitsHeld = 0;
            foreach (var code in codes)
            {
                accumulator |= code << bitsHeld;
                bitsHeld += 3;
                while (bitsHeld >= 8)
                {
                    bytes.Add((byte)(accumulator & 0xFF));
                    accumulator >>= 8;
                    bitsHeld -= 8;
                }
            }
            if (bitsHeld > 0) bytes.Add((byte)(accumulator & 0xFF));

            g.Add((byte)bytes.Count);
            g.AddRange(bytes);
            g.Add(0x00);
        }

        g.Add(0x3B);
        return g.ToArray();
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
        using var source = new MemoryStream(CreateTwoFrameGif());
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
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~SkiaImageProcessorTests"
```

Expected: compile failure — `IImageProcessor`, `SkiaImageProcessor`, `ImageEncoding`, `ImageDimensions` do not exist.

- [ ] **Step 4: Create the supporting types**

`TelegramGroupsAdmin.Core/Imaging/ImageFormat.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Output container an <see cref="IImageProcessor"/> operation encodes to.
/// </summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Webp
}
```

`TelegramGroupsAdmin.Core/Imaging/ImageDimensions.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Pixel dimensions of an image, read without fully decoding it.
/// </summary>
public sealed record ImageDimensions(int Width, int Height);
```

`TelegramGroupsAdmin.Core/Imaging/ImageEncoding.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// How an <see cref="IImageProcessor"/> operation should encode its output.
/// Quality is ignored for lossless formats.
/// </summary>
public sealed record ImageEncoding(ImageFormat Format, int Quality)
{
    /// <summary>Lossless PNG. Quality is not meaningful and is fixed at 100.</summary>
    public static ImageEncoding Png { get; } = new(ImageFormat.Png, 100);

    /// <summary>JPEG at the given quality (0-100).</summary>
    public static ImageEncoding Jpeg(int quality) => new(ImageFormat.Jpeg, quality);
}
```

`TelegramGroupsAdmin.Core/Imaging/IImageProcessor.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Every image decode, transform and encode in the application goes through this
/// interface, so the underlying imaging library can be replaced by editing one file.
///
/// All operations read the source stream from its current position and are tolerant
/// of undecodable input, returning null or false rather than throwing.
///
/// STREAM CONTRACT: the source stream is left OPEN but NOT rewound. Skia consumes
/// bytes while decoding, so a caller making a second call on the same stream must
/// reset Position = 0 first.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Reads pixel dimensions without decoding the full image.
    /// Returns null when the stream does not contain a decodable image.
    /// </summary>
    ImageDimensions? ReadDimensions(Stream source);

    /// <summary>
    /// Decodes the image to greyscale and reduces it to a <paramref name="gridSize"/> x
    /// <paramref name="gridSize"/> grid by averaging every source pixel that falls in
    /// each cell (a box average). Returns one byte of Rec.601 luminance per cell in
    /// row-major order, or null when the stream does not contain a decodable image.
    ///
    /// The box average is what makes the perceptual hash independent of the imaging
    /// library: it absorbs per-decoder rounding differences that a resampling kernel
    /// would preserve.
    /// </summary>
    byte[]? ReadLuminanceGrid(Stream source, int gridSize);

    /// <summary>
    /// Scales the image down so neither side exceeds <paramref name="maxDimension"/>,
    /// preserving aspect ratio. Images already within bounds are still re-encoded.
    /// Animated sources collapse to their first frame.
    /// Returns false when the source is not a decodable image.
    /// </summary>
    Task<bool> ResizeToFitAsync(Stream source, Stream destination, int maxDimension, ImageEncoding encoding, CancellationToken ct = default);

    /// <summary>
    /// Centre-crops the image to a square and scales it to <paramref name="size"/> x
    /// <paramref name="size"/>, filling the frame.
    /// Returns false when the source is not a decodable image.
    /// </summary>
    Task<bool> ResizeToFillAsync(Stream source, Stream destination, int size, ImageEncoding encoding, CancellationToken ct = default);

    /// <summary>
    /// Applies a Gaussian blur at the given sigma, preserving dimensions.
    /// Returns false when the source is not a decodable image.
    /// </summary>
    Task<bool> BlurAsync(Stream source, Stream destination, float sigma, ImageEncoding encoding, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement `SkiaImageProcessor`**

`TelegramGroupsAdmin.Core/Imaging/SkiaImageProcessor.cs`:

```csharp
using SkiaSharp;

namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// SkiaSharp-backed <see cref="IImageProcessor"/>. Skia's API is synchronous, so the
/// decode and transform work runs inline; only the write to the destination stream is
/// awaited.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public ImageDimensions? ReadDimensions(Stream source)
    {
        // SKCodec/SKBitmap take ownership of a raw Stream and dispose it. The
        // non-owning wrapper keeps the caller's stream usable for a second call.
        using var managed = new SKManagedStream(source, disposeManagedStream: false);
        using var codec = SKCodec.Create(managed);
        return codec is null ? null : new ImageDimensions(codec.Info.Width, codec.Info.Height);
    }

    public byte[]? ReadLuminanceGrid(Stream source, int gridSize)
    {
        using var bitmap = Decode(source);
        if (bitmap is null) return null;

        var grid = new byte[gridSize * gridSize];

        for (var cellY = 0; cellY < gridSize; cellY++)
        {
            var y0 = (int)((long)cellY * bitmap.Height / gridSize);
            var y1 = Math.Max(y0 + 1, (int)((long)(cellY + 1) * bitmap.Height / gridSize));

            for (var cellX = 0; cellX < gridSize; cellX++)
            {
                var x0 = (int)((long)cellX * bitmap.Width / gridSize);
                var x1 = Math.Max(x0 + 1, (int)((long)(cellX + 1) * bitmap.Width / gridSize));

                double sum = 0;
                var count = 0;
                for (var y = y0; y < y1 && y < bitmap.Height; y++)
                for (var x = x0; x < x1 && x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    // Rec.601 luminance, matching the greyscale conversion the
                    // previous ImageSharp L8 load performed.
                    sum += 0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue;
                    count++;
                }

                grid[cellY * gridSize + cellX] = count == 0 ? (byte)0 : (byte)Math.Clamp(sum / count, 0, 255);
            }
        }

        return grid;
    }

    public Task<bool> ResizeToFitAsync(Stream source, Stream destination, int maxDimension, ImageEncoding encoding, CancellationToken ct = default)
    {
        using var bitmap = Decode(source);
        if (bitmap is null) return Task.FromResult(false);

        var scale = Math.Min((float)maxDimension / bitmap.Width, (float)maxDimension / bitmap.Height);
        var width = Math.Max(1, (int)MathF.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(bitmap.Height * scale));

        using var resized = bitmap.Resize(new SKImageInfo(width, height), Sampling);
        return resized is null ? Task.FromResult(false) : WriteAsync(resized, destination, encoding, ct);
    }

    public Task<bool> ResizeToFillAsync(Stream source, Stream destination, int size, ImageEncoding encoding, CancellationToken ct = default)
    {
        using var bitmap = Decode(source);
        if (bitmap is null) return Task.FromResult(false);

        var side = Math.Min(bitmap.Width, bitmap.Height);
        var cropRect = SKRectI.Create((bitmap.Width - side) / 2, (bitmap.Height - side) / 2, side, side);

        using var cropped = new SKBitmap(side, side);
        if (!bitmap.ExtractSubset(cropped, cropRect)) return Task.FromResult(false);

        using var resized = cropped.Resize(new SKImageInfo(size, size), Sampling);
        return resized is null ? Task.FromResult(false) : WriteAsync(resized, destination, encoding, ct);
    }

    public Task<bool> BlurAsync(Stream source, Stream destination, float sigma, ImageEncoding encoding, CancellationToken ct = default)
    {
        using var bitmap = Decode(source);
        if (bitmap is null) return Task.FromResult(false);

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        if (surface is null) return Task.FromResult(false);

        using var image = SKImage.FromBitmap(bitmap);
        // SKImageFilter is a native ref-counted object owning its own reference;
        // disposing the SKPaint does not release it.
        using var blurFilter = SKImageFilter.CreateBlur(sigma, sigma);
        using var paint = new SKPaint { ImageFilter = blurFilter };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, SKPoint.Empty, SKSamplingOptions.Default, paint);

        using var snapshot = surface.Snapshot();
        using var blurred = SKBitmap.FromImage(snapshot);
        return blurred is null ? Task.FromResult(false) : WriteAsync(blurred, destination, encoding, ct);
    }

    /// <summary>
    /// Decodes to a bitmap. Animated sources (GIF, APNG, animated WebP) yield frame 0,
    /// which is what every thumbnail path wants.
    /// </summary>
    private static SKBitmap? Decode(Stream source)
    {
        try
        {
            using var managed = new SKManagedStream(source, disposeManagedStream: false);
            return SKBitmap.Decode(managed);
        }
        catch (Exception)
        {
            // Skia signals malformed input by returning null, but a truncated or
            // hostile stream can still surface as a native-side exception.
            return null;
        }
    }

    private static async Task<bool> WriteAsync(SKBitmap bitmap, Stream destination, ImageEncoding encoding, CancellationToken ct)
    {
        var format = encoding.Format switch
        {
            ImageFormat.Png => SKEncodedImageFormat.Png,
            ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };

        using var buffer = new MemoryStream();
        if (!bitmap.Encode(buffer, format, encoding.Quality)) return false;

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, ct);
        return true;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~SkiaImageProcessorTests"
```

Expected: PASS, all tests.

- [ ] **Step 7: Register in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, immediately above the existing `services.AddSingleton<IPhotoHashService, PhotoHashService>();` (line ~168):

```csharp
            services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
```

Add `using TelegramGroupsAdmin.Core.Imaging;` to the file's usings. `SkiaImageProcessor` is stateless, so a singleton is correct.

- [ ] **Step 8: Verify the solution builds with no warnings**

```bash
dotnet build TelegramGroupsAdmin.sln --no-incremental 2>&1 | grep -E "warning|error|Build succeeded"
```

Expected: `Build succeeded`, zero warnings.

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props TelegramGroupsAdmin.Core/ TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs TelegramGroupsAdmin.UnitTests/Core/Imaging/
git commit -F- <<'EOF'
feat(imaging): add IImageProcessor seam backed by SkiaSharp

Every image decode, transform and encode will go through one interface so
the imaging library can be swapped by editing a single file — the diffuse
ImageSharp dependency is what makes #523 expensive.

SkiaSharp's decode returns frame 0 of an animated source automatically, so
no explicit first-frame operation is needed.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: `PhotoHashService` on the new processor

Removes ImageSharp from `TelegramGroupsAdmin.Core` and makes the hash library-independent.

**Files:**
- Modify: `TelegramGroupsAdmin.Core/HashingConstants.cs`
- Modify: `TelegramGroupsAdmin.Core/Services/PhotoHashService.cs`
- Modify: `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Services/PhotoHashServiceTests.cs`

**Interfaces:**
- Consumes: `IImageProcessor.ReadLuminanceGrid(Stream, int)` from Task 1.
- Produces: `PhotoHashService(IImageProcessor imageProcessor, ILogger<PhotoHashService> logger)` — **the constructor gains a first parameter**. `IPhotoHashService` itself is unchanged, so no call site outside DI needs editing.

- [ ] **Step 1: Add the missing constant**

In `TelegramGroupsAdmin.Core/HashingConstants.cs`, add after `PhotoHashByteCount`:

```csharp
    /// <summary>
    /// Bits per byte, used when packing the photo hash bit grid into bytes.
    /// Distinct from <see cref="PhotoHashByteCount"/>, which happens to share the
    /// value 8 but means something else entirely.
    /// </summary>
    public const int BitsPerByte = 8;
```

- [ ] **Step 2: Write the failing tests**

Replace the ImageSharp-based fixture helpers in `TelegramGroupsAdmin.UnitTests/Core/Services/PhotoHashServiceTests.cs`. Keep every existing `CompareHashes` test unchanged — that method has no imaging dependency. Replace the fixture generation and add these tests:

```csharp
using NSubstitute;
using SkiaSharp;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Services;

// ... inside the fixture:

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
    Assert.That(Convert.ToHexString(hash), Is.EqualTo("__FILL_FROM_FIRST_GREEN_RUN__"));
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
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PhotoHashServiceTests"
```

Expected: compile failure — `PhotoHashService` has no constructor taking `IImageProcessor`.

- [ ] **Step 4: Rewrite `PhotoHashService`**

Replace the whole of `TelegramGroupsAdmin.Core/Services/PhotoHashService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Computes and compares perceptual hashes (average hash) of images.
///
/// The hash deliberately depends on no imaging library beyond a decode: the
/// downsample is a box average performed here, so the stored hash values survive
/// a change of imaging library or codec. Changing anything in
/// <see cref="ComputePhotoHashAsync"/> invalidates every persisted photo_hash.
/// </summary>
public class PhotoHashService : IPhotoHashService
{
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<PhotoHashService> _logger;
    private const int HashSize = HashingConstants.PhotoHashSize;

    public PhotoHashService(IImageProcessor imageProcessor, ILogger<PhotoHashService> logger)
    {
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Average hash (aHash):
    /// 1. Reduce to an 8x8 grid of Rec.601 luminance by box-averaging the source
    /// 2. Compute the mean of the 64 cells
    /// 3. Emit one bit per cell: 1 if above the mean, 0 otherwise
    /// </summary>
    public async Task<byte[]?> ComputePhotoHashAsync(string photoPath)
    {
        try
        {
            if (!File.Exists(photoPath))
            {
                _logger.LogTrace("Photo file not found: {PhotoPath}", photoPath);
                return null;
            }

            await using var stream = File.OpenRead(photoPath);
            var cells = _imageProcessor.ReadLuminanceGrid(stream, HashSize);
            if (cells is null)
            {
                _logger.LogDebug("Could not decode image for hashing: {PhotoPath}", photoPath);
                return null;
            }

            long sum = 0;
            foreach (var cell in cells) sum += cell;
            var average = sum / (HashSize * HashSize);

            var hash = new byte[HashingConstants.PhotoHashByteCount];
            for (var i = 0; i < HashingConstants.PhotoHashBitCount; i++)
            {
                if (cells[i] > average)
                {
                    hash[i / HashingConstants.BitsPerByte] |= (byte)(1 << (i % HashingConstants.BitsPerByte));
                }
            }

            _logger.LogDebug("Computed photo hash for {PhotoPath}: {Hash}", photoPath, Convert.ToHexString(hash));
            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute photo hash for {PhotoPath}", photoPath);
            return null;
        }
    }

    /// <summary>
    /// Compares two hashes by Hamming distance, as a similarity from 0.0 to 1.0.
    /// 1.0 is identical; 0.0 is maximally different.
    /// </summary>
    public double CompareHashes(byte[] hash1, byte[] hash2)
    {
        if (hash1.Length != HashingConstants.PhotoHashByteCount || hash2.Length != HashingConstants.PhotoHashByteCount)
        {
            throw new ArgumentException($"Photo hashes must be exactly {HashingConstants.PhotoHashByteCount} bytes ({HashingConstants.PhotoHashBitCount} bits)");
        }

        var hammingDistance = BitwiseUtilities.HammingDistance(hash1, hash2);
        return 1.0 - (hammingDistance / (double)HashingConstants.PhotoHashBitCount);
    }
}
```

- [ ] **Step 5: Capture the golden hash**

Run the tests. `ComputePhotoHashAsync_KnownFixture_ProducesGoldenHash` will fail with the actual hex value in its message. Paste that value into the test, replacing `__FILL_FROM_FIRST_GREEN_RUN__`.

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PhotoHashServiceTests"
```

This is the one place a test value is written from observed output. It is legitimate here because the fixture is deterministic and the assertion's purpose is to pin whatever the algorithm produces today — but it is a one-time action. Never do it again to "fix" a later failure.

- [ ] **Step 6: Remove ImageSharp from Core**

In `TelegramGroupsAdmin.Core/TelegramGroupsAdmin.Core.csproj`, delete:

```xml
    <PackageReference Include="SixLabors.ImageSharp" />
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PhotoHashServiceTests"
```

Expected: PASS, all tests including the golden hash.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Core/ TelegramGroupsAdmin.UnitTests/Core/Services/PhotoHashServiceTests.cs
git commit -F- <<'EOF'
refactor(hashing): compute the photo hash without an imaging library

The 8x8 downsample is now a box average performed in managed code, so the
stored hash no longer depends on a resampling kernel. PNG, JPEG and WebP
encodes of the same source now hash identically.

A golden-hash test pins the algorithm so it cannot drift silently the way
the ImageSharp-era values are about to.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3: `ThumbnailService` on the new processor

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ThumbnailService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/ThumbnailServiceTests.cs`

**Interfaces:**
- Consumes: `IImageProcessor.ResizeToFitAsync` from Task 1.
- Produces: `ThumbnailService(IVideoFrameExtractionService videoFrameService, IImageProcessor imageProcessor, ILogger<ThumbnailService> logger)` — **`imageProcessor` is inserted as the second constructor parameter**.

- [ ] **Step 1: Update the test fixture generation**

In `TelegramGroupsAdmin.UnitTests/Telegram/Services/ThumbnailServiceTests.cs`, replace `using SixLabors.*` with `using SkiaSharp;` and `using TelegramGroupsAdmin.Core.Imaging;`, pass a real `SkiaImageProcessor` into the constructor, and replace every ImageSharp fixture helper with:

```csharp
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
```

Where a test previously generated an animated GIF with ImageSharp's `GifEncoder`, use the `CreateTwoFrameGif()` helper from Task 1's test file. Extract it to `TelegramGroupsAdmin.UnitTests/TestHelpers/GifTestData.cs` as `public static class GifTestData { public static byte[] TwoFrameRedThenBlue() { ... } }` and call it from both fixtures rather than duplicating it.

Keep every existing assertion about output dimensions and the video-delegation path unchanged.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ThumbnailServiceTests"
```

Expected: compile failure — the constructor does not take an `IImageProcessor`.

- [ ] **Step 3: Rewrite `GenerateImageThumbnailAsync`**

In `TelegramGroupsAdmin.Telegram/Services/ThumbnailService.cs`: delete the three `using SixLabors.*` lines, add `using TelegramGroupsAdmin.Core.Imaging;`, add the constructor parameter and field, delete the `ExtractFirstFrame` helper entirely (Skia's decode handles it), and replace the method body:

```csharp
    /// <summary>
    /// Generate a thumbnail from an image or GIF. Animated sources collapse to their
    /// first frame during decode, so the output is always a static PNG.
    /// </summary>
    private async Task<bool> GenerateImageThumbnailAsync(string sourcePath, string destinationPath, int maxSize, CancellationToken ct)
    {
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Write to a temp file and move into place only on success. File.Create
        // truncates the moment it opens, so writing straight to destinationPath
        // would destroy an existing good thumbnail whenever regeneration failed.
        var tempPath = destinationPath + ".tmp";
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(tempPath))
            {
                if (!await _imageProcessor.ResizeToFitAsync(source, destination, maxSize, ImageEncoding.Png, ct))
                {
                    _logger.LogWarning("Could not decode image for thumbnail: {Source}", sourcePath);
                    return false;
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            _logger.LogDebug("Generated image thumbnail: {Source} -> {Dest}", sourcePath, destinationPath);
            return true;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { /* best effort; a stray temp file is harmless */ }
            }
        }
    }
```

Also update the class doc comment: "Uses ImageSharp for images/GIFs" becomes "Uses IImageProcessor for images/GIFs".

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ThumbnailServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/ThumbnailService.cs TelegramGroupsAdmin.UnitTests/
git commit -m "refactor(thumbnails): move ThumbnailService onto IImageProcessor"
```

---

### Task 4: The four remaining call sites

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/TelegramPhotoService.cs:245-265`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotMediaService.cs:230-260`
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/ImageProcessingHandler.cs:120-133`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs:655-680, 789-812`
- Modify: `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/BotMediaServiceTests.cs`

**Interfaces:**
- Consumes: `IImageProcessor.ResizeToFitAsync`, `ResizeToFillAsync`, `BlurAsync`, `ReadDimensions` from Task 1.
- Produces: each of `TelegramPhotoService`, `BotMediaService`, `ImageProcessingHandler` and `ProfileScanService` gains an `IImageProcessor imageProcessor` constructor parameter, appended **before** the `ILogger` parameter to match the existing convention in each file.

- [ ] **Step 1: Update `BotMediaServiceTests` fixture generation**

Same substitution as Task 3 Step 1: `using SkiaSharp;`, the `WriteImage` helper, and a real `SkiaImageProcessor` passed to the constructor. `BotMediaService` writes through `IFileSystem`, so keep the existing `MockFileSystem` wiring exactly as it is — only the image *bytes* generation changes.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BotMediaServiceTests"
```

Expected: compile failure — constructor mismatch.

- [ ] **Step 3: `TelegramPhotoService.ResizeImageAsync`**

Delete both `using SixLabors.*` lines, add `using TelegramGroupsAdmin.Core.Imaging;`, add the constructor parameter and field, and replace the method:

```csharp
    /// <summary>
    /// Resize an image to a square icon, cropping to fill.
    /// </summary>
    private async Task ResizeImageAsync(string sourcePath, string targetPath, int size, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(targetPath);
        await _imageProcessor.ResizeToFillAsync(source, target, size, ImageEncoding.Jpeg(85), cancellationToken);
    }
```

- [ ] **Step 4: `BotMediaService.ResizeImageAsync`**

Delete both `using SixLabors.*` lines, add `using TelegramGroupsAdmin.Core.Imaging;`, add the constructor parameter and field, and replace the method. **All I/O must stay on `_fileSystem`** — that is the whole point of the existing abstraction:

```csharp
    /// <summary>
    /// Resize an image to a square icon, cropping to fill.
    /// I/O goes through IFileSystem for testability; IImageProcessor only transforms.
    /// </summary>
    private async Task ResizeImageAsync(
        string sourcePath,
        string targetPath,
        int size,
        CancellationToken ct = default)
    {
        await using var sourceStream = _fileSystem.File.OpenRead(sourcePath);
        await using var targetStream = _fileSystem.File.Create(targetPath);
        await _imageProcessor.ResizeToFillAsync(sourceStream, targetStream, size, ImageEncoding.Jpeg(85), ct);
    }
```

- [ ] **Step 5: `ImageProcessingHandler` thumbnail generation**

Delete both `using SixLabors.*` lines, add `using TelegramGroupsAdmin.Core.Imaging;`, add the constructor parameter and field, and replace the `using (var image = ...)` block at line ~122:

```csharp
                // Generate thumbnail. Quality 75 matches ImageSharp's JpegEncoder
                // default, which this call site previously relied on — raising it
                // would change every newly stored thumbnail.
                await using (var thumbSource = File.OpenRead(tempPath))
                await using (var thumbTarget = File.Create(thumbPath))
                {
                    await _imageProcessor.ResizeToFitAsync(
                        thumbSource, thumbTarget, ThumbnailSize, ImageEncoding.Jpeg(75), cancellationToken);
                }
```

- [ ] **Step 6: `ProfileScanService` — blur and vision resize**

Delete both `using SixLabors.*` lines, add `using TelegramGroupsAdmin.Core.Imaging;`. `ProfileScanService` uses primary-constructor injection, so add `IImageProcessor imageProcessor` to the parameter list. Replace `CensorProfilePhotoAsync`'s body (line ~663):

```csharp
        try
        {
            // Blur to a temporary file first: the source and destination are the
            // same path, so streaming straight back would truncate the input.
            var tempPath = photoPath + ".censoring";
            var dimensions = ReadDimensions(photoPath);
            if (dimensions is null)
            {
                logger.LogWarning("Profile scan: could not decode profile photo for {User}", user.ToLogDebug());
                return;
            }

            // Skia's blur kernel spans roughly 6*sigma; clamp so it fits the image.
            var maxSigma = Math.Min(dimensions.Width, dimensions.Height) / 6f;
            var sigma = Math.Min(40f, maxSigma);

            await using (var source = File.OpenRead(photoPath))
            await using (var target = File.Create(tempPath))
            {
                if (!await imageProcessor.BlurAsync(source, target, sigma, ImageEncoding.Jpeg(85), ct))
                {
                    logger.LogWarning("Profile scan: blur failed for {User}", user.ToLogDebug());
                    return;
                }
            }

            File.Move(tempPath, photoPath, overwrite: true);
            logger.LogInformation("Profile scan: censored profile photo for banned {User}", user.ToLogInfo());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Profile scan: failed to censor profile photo for {User}", user.ToLogDebug());
        }
```

Add this private helper to the same class:

```csharp
    private ImageDimensions? ReadDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        return imageProcessor.ReadDimensions(stream);
    }
```

Then replace `ResizeForVisionAsync` (line ~789). Note it is `static` today and must stop being static, since it now needs the injected processor — update its two call-site groups accordingly (they are already instance methods):

```csharp
    private async Task<byte[]> ResizeForVisionAsync(Stream imageStream, int maxDimension = VisionMaxDimension)
    {
        imageStream.Position = 0;
        var dimensions = imageProcessor.ReadDimensions(imageStream);

        if (dimensions is null || (dimensions.Width <= maxDimension && dimensions.Height <= maxDimension))
        {
            // Already within bounds, or undecodable — hand back the original bytes
            // rather than re-encoding.
            imageStream.Position = 0;
            using var passthrough = new MemoryStream((int)imageStream.Length);
            await imageStream.CopyToAsync(passthrough);
            return passthrough.ToArray();
        }

        imageStream.Position = 0;
        using var output = new MemoryStream();
        await imageProcessor.ResizeToFitAsync(imageStream, output, maxDimension, ImageEncoding.Jpeg(85));
        return output.ToArray();
    }
```

- [ ] **Step 7: Remove ImageSharp from the Telegram project**

In `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj`, delete:

```xml
    <PackageReference Include="SixLabors.ImageSharp" />
```

- [ ] **Step 8: Verify no ImageSharp references remain in production code**

```bash
grep -rn "SixLabors" --include=*.cs TelegramGroupsAdmin.Core/ TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin/
```

Expected: no output.

- [ ] **Step 9: Run the full unit test suite**

```bash
dotnet test TelegramGroupsAdmin.UnitTests
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin.UnitTests/
git commit -F- <<'EOF'
refactor(imaging): move the remaining call sites onto IImageProcessor

Covers profile-photo icons, bot media icons, message thumbnails, the
vision-API downscale and banned-user photo censoring. ImageSharp is now
gone from every production project.

Censoring writes to a temporary file before moving into place; the old
code read and wrote the same path, which only worked because ImageSharp
buffered the whole image.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 5: Purge the package and update licence docs

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `TelegramGroupsAdmin/TelegramGroupsAdmin.csproj:51`
- Modify: `TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj:26`
- Modify: `THIRD-PARTY-LICENSES.md`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Delete the version hold and the package version**

In `Directory.Packages.props`, delete lines 98-103 — the five-line version-hold comment and the `SixLabors.ImageSharp` `PackageVersion` element.

- [ ] **Step 2: Delete the remaining package references**

`TelegramGroupsAdmin/TelegramGroupsAdmin.csproj:51` — this reference was already dead (no `using` in that project). `TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj:26` — no longer needed after Tasks 2-4.

The UnitTests project needs SkiaSharp for its fixture generation. It picks it up transitively through its project reference to Core, so no new `PackageReference` is required. If the build disagrees, add `<PackageReference Include="SkiaSharp" />` rather than re-adding ImageSharp.

- [ ] **Step 3: Update `THIRD-PARTY-LICENSES.md`**

Remove the `SixLabors.ImageSharp` entry. Add, matching the file's existing formatting:

```markdown
### SkiaSharp
- **License:** MIT
- **Source:** https://github.com/mono/SkiaSharp
- **Note:** Wraps Google's Skia graphics library (BSD-3-Clause).
  Native binaries are supplied by SkiaSharp.NativeAssets.Linux.NoDependencies.
```

- [ ] **Step 4: Verify the package is gone from the entire solution**

```bash
grep -rn "SixLabors" --include=*.cs --include=*.csproj --include=*.props --include=*.md . | grep -v "^./docs/superpowers/"
```

Expected: no output. (The spec and this plan mention it by name; that is correct and they are excluded.)

- [ ] **Step 5: Build and test**

```bash
dotnet build TelegramGroupsAdmin.sln --no-incremental 2>&1 | grep -E "warning|error|Build succeeded"
dotnet test TelegramGroupsAdmin.UnitTests
```

Expected: `Build succeeded`, zero warnings, tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props TelegramGroupsAdmin/TelegramGroupsAdmin.csproj TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj THIRD-PARTY-LICENSES.md
git commit -F- <<'EOF'
chore(deps): remove SixLabors.ImageSharp

Closes the licensing exposure from #523: v4.0.0+ requires a paid Six Labors
licence key at build time, which had us pinned to 3.1.12 and blocked from
routine dependency bumps.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 6: Migration — nullable hash column, and clear every v1 hash

Every stored hash was produced by ImageSharp's resampler and is incomparable with a v2 hash. This migration makes them all NULL in one transactional step, so no window exists in which stale and current hashes are compared against each other.

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/ImageTrainingSampleDto.cs:36-41`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs`
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_ClearV1PhotoHashes.cs` (generated)
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/ImageTrainingSamplesRepository.cs:34-50`
- Create: `TelegramGroupsAdmin.ContentDetection/Models/ImageTrainingSample.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/IImageTrainingSamplesRepository.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Checks/ImageContentCheckV2.cs:109`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed record ImageTrainingSample(byte[] PhotoHash, bool IsSpam);` and `Task<List<ImageTrainingSample>> GetRecentSamplesAsync(int limit, CancellationToken ct)` replacing the tuple-returning signature.

- [ ] **Step 1: Make the column nullable on the model**

In `TelegramGroupsAdmin.Data/Models/ImageTrainingSampleDto.cs`, change the `PhotoHash` property — remove `[Required]` and make it nullable:

```csharp
    /// <summary>
    /// Perceptual hash (64-bit aHash) for similarity matching.
    /// NULL means the hash could not be recomputed after the v1 to v2 hash
    /// migration because the source image no longer exists on disk. Rows with a
    /// NULL hash are excluded from similarity queries.
    /// </summary>
    [Column("photo_hash")]
    public byte[]? PhotoHash { get; set; }
```

- [ ] **Step 2: Reflect it in `AppDbContext`**

In the `ImageTrainingSampleDto` configuration block (around `AppDbContext.cs:426`), add:

```csharp
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .Property(its => its.PhotoHash)
            .IsRequired(false);
```

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add ClearV1PhotoHashes --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin
```

- [ ] **Step 4: Add the hash-clearing statements**

The generated migration will contain only the `AlterColumn` for nullability. Add the clearing statements to `Up`, after the `AlterColumn` call:

```csharp
            // Every stored hash was produced by ImageSharp's resampler and cannot be
            // compared against a v2 hash. Clearing them leaves detection degraded to
            // misses (never false positives) until PhotoHashRehashService refills
            // whatever is still recoverable from disk.
            migrationBuilder.Sql("UPDATE telegram_users SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE linked_channels SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE ban_celebration_gifs SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE image_training_samples SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE video_training_samples SET keyframe_hashes = '[]'::jsonb WHERE keyframe_hashes <> '[]'::jsonb;");
```

Leave `Down` as EF generated it. The cleared hashes are not recoverable by a down-migration; that is inherent, and re-running the rehash service restores them.

- [ ] **Step 5: Add the carrying record**

Create `TelegramGroupsAdmin.ContentDetection/Models/ImageTrainingSample.cs`:

```csharp
namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// A labelled training image reduced to what similarity matching needs.
/// PhotoHash is non-null by construction: rows with a NULL hash are filtered out
/// at the query, because a hash that cannot be compared is not a usable sample.
/// </summary>
public sealed record ImageTrainingSample(byte[] PhotoHash, bool IsSpam);
```

- [ ] **Step 6: Filter NULL hashes at the query**

In `TelegramGroupsAdmin.ContentDetection/Repositories/ImageTrainingSamplesRepository.cs`, replace `GetRecentSamplesAsync` (lines 34-50). The old signature returned `List<(byte[] PhotoHash, bool IsSpam)>`, which the house rules forbid:

```csharp
    public async Task<List<ImageTrainingSample>> GetRecentSamplesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var samples = await context.ImageTrainingSamples
            .AsNoTracking()
            // A NULL hash means the source image is gone and the hash could not be
            // recomputed. Comparing against it would throw in CompareHashes.
            .Where(its => its.PhotoHash != null)
            .OrderByDescending(its => its.MarkedAt)
            .Take(limit)
            .Select(its => new { its.PhotoHash, its.IsSpam })
            .ToListAsync(cancellationToken);

        return samples.Select(s => new ImageTrainingSample(s.PhotoHash!, s.IsSpam)).ToList();
    }
```

Update the signature in `IImageTrainingSamplesRepository.cs` to match, and add `using TelegramGroupsAdmin.ContentDetection.Models;` to both files.

- [ ] **Step 7: Update the consumer**

In `TelegramGroupsAdmin.ContentDetection/Checks/ImageContentCheckV2.cs`, the deconstructing loop at line ~109 must become a record access:

```csharp
                        foreach (var sample in trainingSamples)
                        {
                            var similarity = photoHashService.CompareHashes(photoHash, sample.PhotoHash);
                            if (similarity > bestSimilarity)
                            {
                                bestSimilarity = similarity;
                                matchedSpamLabel = sample.IsSpam;
```

- [ ] **Step 8: Apply the migration and verify**

```bash
dotnet run --project TelegramGroupsAdmin --migrate
```

Expected: "Migration complete. Exiting" with exit code 0.

- [ ] **Step 9: Build and test**

```bash
dotnet build TelegramGroupsAdmin.sln --no-incremental 2>&1 | grep -E "warning|error|Build succeeded"
dotnet test TelegramGroupsAdmin.UnitTests
```

Expected: `Build succeeded`, zero warnings, tests PASS.

- [ ] **Step 10: Commit**

```bash
git add TelegramGroupsAdmin.Data/ TelegramGroupsAdmin.ContentDetection/
git commit -F- <<'EOF'
feat(data): clear v1 photo hashes and allow a null training-sample hash

Skia and ImageSharp resample differently, so every stored hash is
incomparable with a freshly computed one. Clearing them transactionally
means no window exists where the two generations are compared and silently
fail to match.

image_training_samples.photo_hash becomes nullable so a sample whose source
image is gone keeps its label and metadata features instead of being
deleted; such rows are filtered out of similarity queries.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 7: The rehash pass

Refills every hash the migration cleared that can still be recomputed from disk. It uses `photo_hash IS NULL` as its own idempotency marker — no separate completion flag is needed, and a re-run is a cheap no-op for rows already filled.

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/Hashing/IPhotoHashRehashService.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Hashing/PhotoHashRehashService.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Hashing/PhotoHashRehashResult.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin/WebApplicationExtensions.cs`
- Modify: `TelegramGroupsAdmin/Program.cs:212` (after the `--migrate-only` early exit)
- Test: `TelegramGroupsAdmin.IntegrationTests/Services/PhotoHashRehashServiceTests.cs`

**Interfaces:**
- Consumes: `IPhotoHashService.ComputePhotoHashAsync` (unchanged), `AppDbContext`.
- Produces: `IPhotoHashRehashService` with `Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default)`; record `PhotoHashRehashResult(int Recomputed, int Unrecoverable, int Skipped)`.

**Scope note:** this task covers the four `photo_hash` byte-array stores. Video keyframe re-extraction is deliberately **not** included — it needs FFmpeg and belongs in a Quartz job, and `video_training_samples` self-heals as new videos are labelled. Task 9 records this as a follow-up issue.

- [ ] **Step 1: Write the failing tests**

Create `TelegramGroupsAdmin.IntegrationTests/Services/PhotoHashRehashServiceTests.cs`. This is an integration test because it needs a real database; follow the existing fixture pattern in `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs` for container setup.

```csharp
[Test]
public async Task RehashAsync_UserPhotoOnDisk_RecomputesHash()
{
    var user = await SeedUserAsync(photoPath: "user_photos/1.jpg", photoHash: null, bannedAt: null);
    WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "1.jpg"));

    var result = await _service.RehashAsync();

    var reloaded = await ReloadUserAsync(user.Id);
    Assert.Multiple(() =>
    {
        Assert.That(reloaded.PhotoHash, Is.Not.Null);
        Assert.That(reloaded.PhotoHash!, Has.Length.EqualTo(8));
        Assert.That(result.Recomputed, Is.EqualTo(1));
    });
}

[Test]
public async Task RehashAsync_UserPhotoMissingFromDisk_LeavesHashNull()
{
    var user = await SeedUserAsync(photoPath: "user_photos/2.jpg", photoHash: null, bannedAt: null);
    // Deliberately do not write the file.

    var result = await _service.RehashAsync();

    var reloaded = await ReloadUserAsync(user.Id);
    Assert.Multiple(() =>
    {
        Assert.That(reloaded.PhotoHash, Is.Null);
        Assert.That(result.Unrecoverable, Is.EqualTo(1));
    });
}

[Test]
public async Task RehashAsync_BannedUser_LeavesHashNullEvenWithPhotoOnDisk()
{
    var user = await SeedUserAsync(photoPath: "user_photos/3.jpg", photoHash: null, bannedAt: DateTimeOffset.UtcNow);
    WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "3.jpg"));

    var result = await _service.RehashAsync();

    var reloaded = await ReloadUserAsync(user.Id);
    // The photo on disk was blurred in place by profile-scan censoring, so hashing
    // it would store a hash of the blur, not of the user's real photo.
    Assert.Multiple(() =>
    {
        Assert.That(reloaded.PhotoHash, Is.Null);
        Assert.That(result.Skipped, Is.EqualTo(1));
    });
}

[Test]
public async Task RehashAsync_RunTwice_IsIdempotent()
{
    await SeedUserAsync(photoPath: "user_photos/4.jpg", photoHash: null, bannedAt: null);
    WriteTestImage(Path.Combine(_dataPath, "media", "user_photos", "4.jpg"));

    var first = await _service.RehashAsync();
    var second = await _service.RehashAsync();

    Assert.Multiple(() =>
    {
        Assert.That(first.Recomputed, Is.EqualTo(1));
        // The row now has a hash, so the IS NULL predicate skips it entirely.
        Assert.That(second.Recomputed, Is.EqualTo(0));
    });
}

[Test]
public async Task RehashAsync_TrainingSampleWithLiveMessageMedia_RecomputesHash()
{
    var sample = await SeedImageTrainingSampleAsync(mediaLocalPath: "full/1/photo.jpg", photoHash: null);
    WriteTestImage(Path.Combine(_dataPath, "media", "full", "1", "photo.jpg"));

    await _service.RehashAsync();

    var reloaded = await ReloadSampleAsync(sample.Id);
    Assert.That(reloaded.PhotoHash, Is.Not.Null);
}
```

Write `WriteTestImage` using `SkiaSharp` exactly as in Task 3 Step 1, and the `Seed*`/`Reload*` helpers against the fixture's `AppDbContext`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PhotoHashRehashServiceTests"
```

Expected: compile failure — the service does not exist.

- [ ] **Step 3: Create the result record**

`TelegramGroupsAdmin.Telegram/Services/Hashing/PhotoHashRehashResult.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <summary>
/// Outcome of a rehash pass.
/// </summary>
/// <param name="Recomputed">Rows whose hash was recomputed from a file on disk.</param>
/// <param name="Unrecoverable">Rows left NULL because the source file no longer exists.</param>
/// <param name="Skipped">Rows deliberately left NULL, such as banned users whose photo was blur-censored in place.</param>
public sealed record PhotoHashRehashResult(int Recomputed, int Unrecoverable, int Skipped)
{
    public static PhotoHashRehashResult Empty { get; } = new(0, 0, 0);

    public PhotoHashRehashResult Add(PhotoHashRehashResult other) =>
        new(Recomputed + other.Recomputed,
            Unrecoverable + other.Unrecoverable,
            Skipped + other.Skipped);
}
```

- [ ] **Step 4: Create the interface**

`TelegramGroupsAdmin.Telegram/Services/Hashing/IPhotoHashRehashService.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <summary>
/// Refills perceptual hashes cleared by the v1-to-v2 hash migration.
///
/// Idempotent by construction: it only visits rows whose hash is NULL, so a row
/// already refilled is skipped by the query itself and no completion marker is
/// needed. Rows whose source image is gone stay NULL permanently and are revisited
/// cheaply on each run in case the file reappears.
/// </summary>
public interface IPhotoHashRehashService
{
    Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement the service**

`TelegramGroupsAdmin.Telegram/Services/Hashing/PhotoHashRehashService.cs`. Follow the existing repository pattern of resolving `IDbContextFactory<AppDbContext>` and resolving media paths with `MediaUtilities.ToAbsolutePath(relativePath, dataPath)`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <inheritdoc />
public sealed class PhotoHashRehashService(
    IDbContextFactory<AppDbContext> contextFactory,
    IPhotoHashService photoHashService,
    IOptions<AppOptions> appOptions,
    ILogger<PhotoHashRehashService> logger) : IPhotoHashRehashService
{
    public async Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default)
    {
        var total = PhotoHashRehashResult.Empty
            .Add(await RehashUsersAsync(ct))
            .Add(await RehashLinkedChannelsAsync(ct))
            .Add(await RehashBanCelebrationGifsAsync(ct))
            .Add(await RehashImageTrainingSamplesAsync(ct));

        if (total != PhotoHashRehashResult.Empty)
        {
            logger.LogInformation(
                "Photo hash rehash: {Recomputed} recomputed, {Unrecoverable} unrecoverable (source file gone), {Skipped} skipped",
                total.Recomputed, total.Unrecoverable, total.Skipped);
        }

        return total;
    }

    private async Task<PhotoHashRehashResult> RehashUsersAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.TelegramUsers
            .Where(u => u.PhotoHash == null && u.UserPhotoPath != null)
            .Select(u => new { u.Id, u.UserPhotoPath, u.BannedAt })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            // A banned user's photo was overwritten in place with a blurred copy by
            // ProfileScanService, so hashing the file would store a hash of the blur.
            if (candidate.BannedAt is not null)
            {
                skipped++;
                continue;
            }

            var hash = await ComputeAsync(candidate.UserPhotoPath!);
            if (hash is null)
            {
                unrecoverable++;
                continue;
            }

            await context.TelegramUsers
                .Where(u => u.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, skipped);
    }

    private async Task<PhotoHashRehashResult> RehashLinkedChannelsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.LinkedChannels
            .Where(c => c.PhotoHash == null && c.ChannelIconPath != null)
            .Select(c => new { c.Id, c.ChannelIconPath })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.ChannelIconPath!);
            if (hash is null) { unrecoverable++; continue; }

            await context.LinkedChannels
                .Where(c => c.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    private async Task<PhotoHashRehashResult> RehashBanCelebrationGifsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var candidates = await context.BanCelebrationGifs
            .Where(g => g.PhotoHash == null)
            .Select(g => new { g.Id, g.FilePath })
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.FilePath);
            if (hash is null) { unrecoverable++; continue; }

            await context.BanCelebrationGifs
                .Where(g => g.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    private async Task<PhotoHashRehashResult> RehashImageTrainingSamplesAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // PhotoPath on the sample row is never populated (it is [Required] but never
        // assigned on insert), so the source image has to come from the joined message.
        var candidates = await context.ImageTrainingSamples
            .Where(its => its.PhotoHash == null)
            .Join(context.Messages,
                its => new { its.MessageId, its.ChatId },
                m => new { m.MessageId, m.ChatId },
                (its, m) => new { its.Id, m.MediaLocalPath })
            .Where(x => x.MediaLocalPath != null)
            .ToListAsync(ct);

        var recomputed = 0;
        var unrecoverable = 0;

        foreach (var candidate in candidates)
        {
            var hash = await ComputeAsync(candidate.MediaLocalPath!);
            if (hash is null) { unrecoverable++; continue; }

            await context.ImageTrainingSamples
                .Where(its => its.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(its => its.PhotoHash, hash), ct);
            recomputed++;
        }

        return new PhotoHashRehashResult(recomputed, unrecoverable, 0);
    }

    private async Task<byte[]?> ComputeAsync(string relativePath)
    {
        var absolutePath = MediaUtilities.ToAbsolutePath(relativePath, appOptions.Value.DataPath);
        return await photoHashService.ComputePhotoHashAsync(absolutePath);
    }
}
```

Verify the `DbSet` names (`TelegramUsers`, `LinkedChannels`, `BanCelebrationGifs`, `ImageTrainingSamples`, `Messages`) and the message key property names against `AppDbContext` before running; correct them to match if they differ.

- [ ] **Step 6: Register in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, beside the other hashing registrations:

```csharp
            services.AddScoped<IPhotoHashRehashService, PhotoHashRehashService>();
```

- [ ] **Step 7: Add the startup hook**

In `TelegramGroupsAdmin/WebApplicationExtensions.cs`, alongside `RunDatabaseMigrationsAsync`:

```csharp
        /// <summary>
        /// Refills perceptual hashes cleared by the v1-to-v2 hash migration. Runs on
        /// every start; it is a cheap no-op once every recoverable hash is filled.
        /// </summary>
        public async Task RunPhotoHashRehashAsync()
        {
            using var scope = app.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPhotoHashRehashService>();

            try
            {
                await service.RehashAsync();
            }
            catch (Exception ex)
            {
                // Detection degrades to misses without this; it must never block startup.
                app.Logger.LogError(ex, "Photo hash rehash failed; hashes remain NULL and will be retried on next start");
            }
        }
```

In `TelegramGroupsAdmin/Program.cs`, immediately after the `--migrate-only` block closes (line ~212) and before the `--backup` block:

```csharp
// Refill perceptual hashes cleared by the v1-to-v2 hash migration (#523).
// Runs before the bot starts so detection is at full strength immediately.
await app.RunPhotoHashRehashAsync();
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PhotoHashRehashServiceTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Hashing/ TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs TelegramGroupsAdmin/WebApplicationExtensions.cs TelegramGroupsAdmin/Program.cs TelegramGroupsAdmin.IntegrationTests/
git commit -F- <<'EOF'
feat(hashing): refill cleared photo hashes on startup

Recomputes every hash the v1-to-v2 migration cleared whose source image is
still on disk. Rows whose image is gone stay NULL, so detection degrades to
misses rather than to false matches.

Banned users are deliberately skipped: profile-scan censoring overwrites
their photo in place with a blurred copy, so hashing it would store a hash
of the blur.

Idempotent via the IS NULL predicate rather than a completion marker, so a
re-run costs one indexed query.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 8: Dependency bumps

**Files:**
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Apply the bumps**

Edit `Directory.Packages.props` only. Do **not** touch `OpenAI` (2.12.0, constrained) or `OpenTelemetry.Exporter.Prometheus.AspNetCore` (1.18.0-beta.1, no stable release).

| Package | From | To |
|---|---|---|
| `AngleSharp` | 1.7.1 | 1.7.2 |
| `MudBlazor` | 9.8.0 | 9.9.0 |
| `Telegram.Bot` | 22.10.2.1 | 22.10.3 |
| `Quartz` | 3.19.1 | 3.20.0 |
| `Quartz.AspNetCore` | 3.19.1 | 3.20.0 |
| `Quartz.Extensions.DependencyInjection` | 3.19.1 | 3.20.0 |
| `Quartz.Extensions.Hosting` | 3.19.1 | 3.20.0 |
| `Quartz.Serialization.SystemTextJson` | 3.19.1 | 3.20.0 |
| `NUnit3TestAdapter` | 6.2.0 | 6.3.0 |
| `Anthropic` | 12.42.0 | 12.44.0 |

- [ ] **Step 2: Restore and check for constraint violations**

```bash
dotnet restore TelegramGroupsAdmin.sln 2>&1 | grep -E "NU1608|NU1605|warning|error"
```

Expected: no output. An `NU1608` means a bump broke a constraint — revert that one package and note it.

- [ ] **Step 3: Build and run the full suite**

MudBlazor 9.8.0 → 9.9.0 is the only bump with UI surface. Check the MudBlazor v9 notes in context-keep before assuming it is mechanical.

```bash
dotnet build TelegramGroupsAdmin.sln --no-incremental 2>&1 | grep -E "warning|error|Build succeeded"
dotnet test TelegramGroupsAdmin.UnitTests
dotnet test TelegramGroupsAdmin.ComponentTests
```

Expected: `Build succeeded`, zero warnings, tests PASS.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props
git commit -F- <<'EOF'
chore(deps): bump to latest stable

AngleSharp 1.7.2, MudBlazor 9.9.0, Telegram.Bot 22.10.3, Quartz 3.20.0,
NUnit3TestAdapter 6.3.0, Anthropic 12.44.0.

OpenAI stays at 2.12.0: Microsoft.Extensions.AI.OpenAI 10.9.0 constrains it
to [2.12.0, 2.13.0) and 2.13.0 trips NU1608.
OpenTelemetry.Exporter.Prometheus.AspNetCore stays on its prerelease pin;
no stable release exists.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 9: Verification and PR

**Files:**
- No source changes expected.

**Interfaces:**
- Consumes: everything above.
- Produces: the PR.

- [ ] **Step 1: Confirm ImageSharp is entirely gone**

```bash
grep -rn "SixLabors" --include=*.cs --include=*.csproj --include=*.props --include=*.md . | grep -v "^./docs/superpowers/"
```

Expected: no output.

- [ ] **Step 2: Clean build, zero warnings**

```bash
dotnet clean TelegramGroupsAdmin.sln && dotnet build TelegramGroupsAdmin.sln --no-incremental 2>&1 | tail -20
```

Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 3: Full test suite**

```bash
dotnet test TelegramGroupsAdmin.UnitTests
dotnet test TelegramGroupsAdmin.ComponentTests
dotnet test TelegramGroupsAdmin.IntegrationTests
```

Expected: all PASS. Record the pass counts; do not claim success without reading the output.

- [ ] **Step 4: Container smoke test**

Build the real image and confirm SkiaSharp loads under the chiseled runtime with the actual application, not just the probe:

```bash
docker build -f TelegramGroupsAdmin/Dockerfile -t tga:523-verify .
docker run --rm --entrypoint /usr/bin/wget tga:523-verify --version
```

Then start the container against a test database and confirm the log line `Photo hash rehash:` appears with no `DllNotFoundException` for `libSkiaSharp`.

The published image is multi-arch (`linux/amd64,linux/arm64`). arm64 was verified
statically only — the native asset ships and declares an identical dependency set to
the x64 build — so after the image publishes, run the container once on real arm64
hardware and confirm the same absence of `DllNotFoundException`. This is a
post-merge confirmation, not a merge blocker.

- [ ] **Step 5: Open the PR**

Target `develop`, never `master`.

```bash
git push -u origin feat/523-replace-imagesharp-skiasharp
gh pr create --base develop --title "feat: replace SixLabors.ImageSharp with SkiaSharp" --body-file - <<'EOF'
Closes #523

## What

Replaces `SixLabors.ImageSharp` (paid licence from v4.0.0) with SkiaSharp 4.151.1
behind a new `IImageProcessor` seam, and migrates the perceptual hashes it produced.

## Why the hashes had to change

Skia and ImageSharp resample differently and their JPEG decoders round differently,
so byte-identical hashes were not achievable. Every stored `photo_hash` is therefore
invalidated. A migration clears them all transactionally — leaving no window where
the two generations are compared and silently fail to match — and a startup pass
refills every hash still recoverable from disk.

Rows whose source image is gone stay NULL, so detection degrades to **misses only,
never false positives**. That is the correct error direction here: confirming spam
deletes messages permanently, so a missed match costs a spam message getting
through, while a false match costs an irreversible deletion.

Banned users are deliberately skipped rather than rehashed: profile-scan censoring
overwrites their photo in place with a blurred copy, so hashing it would store a
hash of the blur.

## The hash no longer depends on an imaging library

The 8x8 downsample is now a box average in managed code, so only the decode goes
through Skia. Measured drift across PNG, JPEG(85) and WebP(90) re-encodes of the
same source is 0 bits. A golden-hash test pins the algorithm so it cannot drift
silently again.

## Deployment

Verified in a container replicating the production `tesseract-env` and `final`
stages: all operations pass on `linux/amd64`, and they also pass on *bare*
`chiseled-extra` with the Tesseract lib copying removed — so no Dockerfile change
is needed and there is no hidden coupling to the OCR stage. The arm64 native asset
declares an identical dependency set to the verified x64 build.

## Also included

Dependency bumps to latest stable: AngleSharp 1.7.2, MudBlazor 9.9.0,
Telegram.Bot 22.10.3, Quartz 3.20.0, NUnit3TestAdapter 6.3.0, Anthropic 12.44.0.
`OpenAI` stays at 2.12.0 (constrained by `Microsoft.Extensions.AI.OpenAI`).

## Follow-ups (not in this PR)

- `ImageTrainingSampleDto.PhotoPath` is `[Required]` but never assigned on insert,
  so every row holds `""`. The rehash works around it by joining to the message.
- `video_training_samples.keyframe_hashes` are cleared but not re-extracted; that
  needs FFmpeg and belongs in a Quartz job. The table self-heals as new videos are
  labelled.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Gy3cGkngt9zCpFXNhX2uUS
EOF
```

- [ ] **Step 6: Open the two follow-up issues**

```bash
gh issue create --title "ImageTrainingSampleDto.PhotoPath is [Required] but never assigned" --label backend,tech-debt --body "Surfaced while implementing #523.

\`ImageTrainingSamplesRepository\` builds an \`ImageTrainingSampleDto\` without ever setting \`PhotoPath\`, so the column holds \`\"\"\` on every row despite being \`[Required]\`. Recovering a sample's source image has to join through \`messages.media_local_path\` instead.

Fixing it does not recover already-deleted pixels, so it was out of scope for #523."

gh issue create --title "Re-extract video keyframe hashes after the v1-to-v2 hash migration" --label backend --body "Follow-up to #523.

The migration cleared \`video_training_samples.keyframe_hashes\` to \`[]\`. Unlike the image stores, refilling them needs FFmpeg keyframe re-extraction at the original percent positions, which belongs in a Quartz job rather than the startup path.

The table self-heals as new videos are labelled, so this is a capacity restoration rather than a correctness fix."
```

- [ ] **Step 7: Close the issue manually after merge**

`Closes #523` never auto-fires here, because PRs target `develop` rather than the default branch `master`.

```bash
gh issue close 523 --comment "Shipped in #<PR number>."
```

---

## Notes for the executor

- **The golden hash value is written once, from observed output** (Task 2 Step 5). Every later failure of that test is a real regression — never update the expected value to make it pass.
- **`ImageProcessingHandler` uses JPEG quality 75, not 85.** That matches ImageSharp's `JpegEncoder` default, which the old code relied on implicitly. Raising it would change every newly stored thumbnail.
- **`BotMediaService` must keep all I/O on `IFileSystem`.** `IImageProcessor` only transforms streams; it never opens files itself.
- **Deviation from the spec, deliberate:** the spec proposed a completion marker in the `configs` table to make the rehash one-shot. The migration clearing all hashes makes `photo_hash IS NULL` a natural, self-maintaining marker, so no marker mechanism is needed. This is strictly simpler and closes the mixed-generation window earlier — at migration time rather than at first startup.

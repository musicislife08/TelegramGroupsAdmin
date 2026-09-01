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
        using var skStream = new SKManagedStream(source, disposeManagedStream: false);
        using var codec = SKCodec.Create(skStream);
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
    ///
    /// Wraps <paramref name="source"/> in a non-owning <see cref="SKManagedStream"/>:
    /// passing the .NET stream straight to Skia lets it take ownership and dispose the
    /// caller's stream once decoding finishes, which breaks any second operation on the
    /// same stream.
    /// </summary>
    private static SKBitmap? Decode(Stream source)
    {
        try
        {
            using var skStream = new SKManagedStream(source, disposeManagedStream: false);
            return SKBitmap.Decode(skStream);
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

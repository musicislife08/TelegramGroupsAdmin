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

    /// <summary>
    /// Box-averages the source into a <paramref name="gridSize"/> square grid of Rec.601
    /// luminance.
    ///
    /// Every dimension is read into a local before the pixel loops. <see cref="SKBitmap.Width"/>
    /// and <see cref="SKBitmap.Height"/> both forward to <see cref="SKBitmap.Info"/>, which
    /// makes a native call and marshals a struct on each access — evaluating either in a loop
    /// condition costs a P/Invoke per pixel and dominates the whole operation.
    /// </summary>
    public byte[]? ReadLuminanceGrid(Stream source, int gridSize)
    {
        using var decoded = Decode(source);
        if (decoded is null) return null;

        // A truncated source decodes with its missing rows as solid black. Hashing that
        // would hand back an identity derived mostly from the failure, and two unrelated
        // truncated images would converge on the same near-black hash — a false match on a
        // path whose action (deleting a message) cannot be undone. Refuse instead.
        if (!decoded.IsComplete) return null;

        var bitmap = decoded.Bitmap;
        var info = bitmap.Info;
        var width = info.Width;
        var height = info.Height;
        if (width <= 0 || height <= 0) return null;

        var layout = PixelLayout.For(info);
        var pixels = layout is null ? default : bitmap.GetPixelSpan();
        var rowBytes = bitmap.RowBytes;

        var grid = new byte[gridSize * gridSize];

        for (var cellY = 0; cellY < gridSize; cellY++)
        {
            var y0 = (int)((long)cellY * height / gridSize);
            var y1 = Math.Max(y0 + 1, (int)((long)(cellY + 1) * height / gridSize));

            for (var cellX = 0; cellX < gridSize; cellX++)
            {
                var x0 = (int)((long)cellX * width / gridSize);
                var x1 = Math.Max(x0 + 1, (int)((long)(cellX + 1) * width / gridSize));

                double sum = 0;
                var count = 0;
                for (var y = y0; y < y1 && y < height; y++)
                {
                    if (layout is not null)
                    {
                        var row = pixels.Slice(y * rowBytes, rowBytes);
                        for (var x = x0; x < x1 && x < width; x++)
                        {
                            var offset = x * layout.BytesPerPixel;
                            // Rec.601 luminance, matching the greyscale conversion the
                            // previous ImageSharp L8 load performed.
                            sum += 0.299 * row[offset + layout.RedOffset]
                                   + 0.587 * row[offset + layout.GreenOffset]
                                   + 0.114 * row[offset + layout.BlueOffset];
                            count++;
                        }
                    }
                    else
                    {
                        // Exotic colour type: fall back to Skia's own conversion, which
                        // normalises whatever the layout is into an unpremultiplied SKColor.
                        for (var x = x0; x < x1 && x < width; x++)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            sum += 0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue;
                            count++;
                        }
                    }
                }

                grid[cellY * gridSize + cellX] = count == 0 ? (byte)0 : (byte)Math.Clamp(sum / count, 0, 255);
            }
        }

        return grid;
    }

    public Task<bool> ResizeToFitAsync(Stream source, Stream destination, int maxDimension, ImageEncoding encoding, CancellationToken ct = default)
    {
        // Thumbnails keep an incomplete decode: a partly-black preview is a cosmetic
        // problem, and refusing it would lose the thumbnail entirely.
        using var decoded = Decode(source);
        if (decoded is null) return Task.FromResult(false);

        var bitmap = decoded.Bitmap;
        var scale = Math.Min((float)maxDimension / bitmap.Width, (float)maxDimension / bitmap.Height);
        var width = Math.Max(1, (int)MathF.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(bitmap.Height * scale));

        using var resized = bitmap.Resize(new SKImageInfo(width, height), Sampling);
        return resized is null ? Task.FromResult(false) : WriteAsync(resized, destination, encoding, ct);
    }

    public Task<bool> ResizeToFillAsync(Stream source, Stream destination, int size, ImageEncoding encoding, CancellationToken ct = default)
    {
        using var decoded = Decode(source);
        if (decoded is null) return Task.FromResult(false);

        var bitmap = decoded.Bitmap;
        var side = Math.Min(bitmap.Width, bitmap.Height);
        var cropRect = SKRectI.Create((bitmap.Width - side) / 2, (bitmap.Height - side) / 2, side, side);

        using var cropped = new SKBitmap(side, side);
        if (!bitmap.ExtractSubset(cropped, cropRect)) return Task.FromResult(false);

        using var resized = cropped.Resize(new SKImageInfo(size, size), Sampling);
        return resized is null ? Task.FromResult(false) : WriteAsync(resized, destination, encoding, ct);
    }

    public Task<bool> BlurAsync(Stream source, Stream destination, float sigma, ImageEncoding encoding, CancellationToken ct = default)
    {
        using var decoded = Decode(source);
        if (decoded is null) return Task.FromResult(false);

        var bitmap = decoded.Bitmap;
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
    ///
    /// This reproduces <see cref="SKBitmap.Decode(SKCodec)"/> exactly — including its
    /// promotion of an unpremultiplied alpha type to premultiplied — so decoded pixels are
    /// unchanged, but keeps the <see cref="SKCodecResult"/> that overload discards. Skia
    /// reports <see cref="SKCodecResult.IncompleteInput"/> for a truncated source and still
    /// hands back a usable bitmap; only the caller knows whether that is acceptable.
    /// </summary>
    private static DecodedImage? Decode(Stream source)
    {
        try
        {
            using var skStream = new SKManagedStream(source, disposeManagedStream: false);
            using var codec = SKCodec.Create(skStream);
            if (codec is null) return null;

            var info = codec.Info;
            if (info.AlphaType == SKAlphaType.Unpremul) info.AlphaType = SKAlphaType.Premul;

            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                bitmap.Dispose();
                return null;
            }

            return new DecodedImage(bitmap, result == SKCodecResult.Success);
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

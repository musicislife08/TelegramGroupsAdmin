using SkiaSharp;

namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Byte offsets of the colour channels within one pixel of a decoded bitmap, for the
/// layouts whose raw bytes can be read directly without going through Skia's per-pixel
/// conversion.
///
/// Reading the pixel buffer is only equivalent to <see cref="SKBitmap.GetPixel"/> when the
/// channels are one byte each and are NOT premultiplied by alpha — Skia unpremultiplies on
/// the way out of <c>GetPixel</c>, so raw bytes from a premultiplied bitmap would carry
/// different values. <see cref="For"/> returns null for any layout where that equivalence
/// does not hold, and the caller falls back to <c>GetPixel</c>.
/// </summary>
internal sealed record PixelLayout(int BytesPerPixel, int RedOffset, int GreenOffset, int BlueOffset)
{
    private static readonly PixelLayout Rgba = new(4, 0, 1, 2);
    private static readonly PixelLayout Bgra = new(4, 2, 1, 0);
    private static readonly PixelLayout Gray = new(1, 0, 0, 0);

    public static PixelLayout? For(SKImageInfo info)
    {
        // Premultiplied colour bytes are scaled by alpha; GetPixel reverses that, a raw
        // read cannot.
        var premultiplied = info.AlphaType == SKAlphaType.Premul;

        return info.ColorType switch
        {
            SKColorType.Rgba8888 when !premultiplied => Rgba,
            SKColorType.Bgra8888 when !premultiplied => Bgra,
            // Rgb888x has no alpha channel at all: the fourth byte is ignored padding.
            SKColorType.Rgb888x => Rgba,
            SKColorType.Gray8 => Gray,
            _ => null
        };
    }
}

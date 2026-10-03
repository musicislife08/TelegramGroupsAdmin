using SkiaSharp;

namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// A decoded bitmap together with whether the encoded source actually contained all of it.
///
/// Skia fills the rows it could not read with zeroes and reports success, so a truncated
/// download decodes to a real bitmap whose tail is solid black. Callers that only display
/// the result can live with that; callers that derive an identity from the pixels cannot.
/// </summary>
internal sealed record DecodedImage(SKBitmap Bitmap, bool IsComplete) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

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

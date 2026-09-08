namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Every image decode, transform and encode in the application goes through this
/// interface, so the underlying imaging library can be replaced by editing one file.
///
/// All operations read the source stream from its current position and are tolerant
/// of undecodable input, returning null or false rather than throwing.
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

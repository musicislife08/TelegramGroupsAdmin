namespace TelegramGroupsAdmin.Core.Imaging;

/// <summary>
/// Pixel dimensions of an image, read without fully decoding it.
/// </summary>
public sealed record ImageDimensions(int Width, int Height);

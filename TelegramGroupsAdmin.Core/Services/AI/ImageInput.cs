namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Represents an image to include in a multi-image vision completion request.
/// </summary>
public record ImageInput(byte[] Data, string MimeType);

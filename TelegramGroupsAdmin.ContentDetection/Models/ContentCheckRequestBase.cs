namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Base class for all content check requests
/// Contains common properties needed by all checks
/// </summary>
public abstract class ContentCheckRequestBase
{
    public required string Message { get; init; }
    public required long UserId { get; init; }
    public required string? UserName { get; init; }
    public required long ChatId { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Base class for all content check requests
/// Contains common properties needed by all checks
/// </summary>
public abstract class ContentCheckRequestBase
{
    public required string Message { get; init; }
    public required UserIdentity User { get; init; }
    public required ChatIdentity Chat { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

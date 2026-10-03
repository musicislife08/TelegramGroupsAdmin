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

    /// <summary>
    /// Telegram message ID of the message under evaluation (null for text not tied to a stored message).
    /// Lets the AI veto leave the message out of its own history context.
    /// </summary>
    public int? MessageId { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}

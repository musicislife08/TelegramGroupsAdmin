using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Provides message context for spam detection algorithms
/// </summary>
public interface IMessageContextProvider
{
    /// <summary>
    /// Get recent messages from a chat for context, leaving out <paramref name="excludeMessageId"/>
    /// (the message under evaluation) so a message is never shown to the AI as its own history.
    /// </summary>
    Task<IEnumerable<HistoryMessage>> GetRecentMessagesAsync(ChatIdentity chat, int count, int? excludeMessageId, CancellationToken cancellationToken = default);
}
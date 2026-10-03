using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using ContentDetectionServices = TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Adapter to convert from main app's MessageHistoryRepository
/// to ContentDetection's IMessageContextProvider interface
/// </summary>
public class MessageContextAdapter : ContentDetectionServices.IMessageContextProvider
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly ILogger<MessageContextAdapter> _logger;

    public MessageContextAdapter(
        IMessageHistoryRepository messageHistoryRepository,
        ILogger<MessageContextAdapter> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<ContentDetectionServices.HistoryMessage>> GetRecentMessagesAsync(
        ChatIdentity chat,
        int count,
        int? excludeMessageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get recent messages with their current verdict (message_verdicts view) to determine spam status
            var messages = await _messageHistoryRepository.GetRecentMessagesWithVerdictAsync(chat.Id, count, excludeMessageId, cancellationToken);

            return messages.Select(m => new ContentDetectionServices.HistoryMessage
            {
                UserId = m.UserId.ToString(),
                UserName = m.Username ?? "Unknown",
                Message = m.MessageText ?? string.Empty,
                Timestamp = m.Timestamp.UtcDateTime,
                WasSpam = m.IsSpam
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message history for {Chat}", chat.ToLogDebug());
            return [];
        }
    }
}

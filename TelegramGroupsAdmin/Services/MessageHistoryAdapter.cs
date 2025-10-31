using TelegramGroupsAdmin.Telegram.Repositories;
using ContentDetectionServices = TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Adapter to convert from main app's MessageHistoryRepository
/// to spam library's IMessageHistoryService interface
/// </summary>
public class MessageHistoryAdapter : ContentDetectionServices.IMessageHistoryService
{
    private readonly IMessageHistoryRepository _repository;
    private readonly ILogger<MessageHistoryAdapter> _logger;

    public MessageHistoryAdapter(
        IMessageHistoryRepository repository,
        ILogger<MessageHistoryAdapter> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IEnumerable<ContentDetectionServices.HistoryMessage>> GetRecentMessagesAsync(
        long chatId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get recent messages from repository (filtered by chat_id in query)
            var messages = await _repository.GetMessagesByChatIdAsync(chatId, count);

            // Convert to spam library's HistoryMessage format
            return messages.Select(m => new ContentDetectionServices.HistoryMessage
            {
                UserId = m.UserId.ToString(),
                UserName = m.UserName ?? "Unknown",
                Message = m.MessageText ?? string.Empty,
                Timestamp = m.Timestamp.UtcDateTime,
                WasSpam = false // TODO: Join with detection_results table to populate this
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message history for chat {ChatId}", chatId);
            return [];
        }
    }
}

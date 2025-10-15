using TelegramGroupsAdmin.Telegram.Repositories;
using SpamDetectionServices = TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Adapter to convert from main app's MessageHistoryRepository
/// to spam library's IMessageHistoryService interface
/// </summary>
public class MessageHistoryAdapter : SpamDetectionServices.IMessageHistoryService
{
    private readonly MessageHistoryRepository _repository;
    private readonly ILogger<MessageHistoryAdapter> _logger;

    public MessageHistoryAdapter(
        MessageHistoryRepository repository,
        ILogger<MessageHistoryAdapter> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IEnumerable<SpamDetectionServices.HistoryMessage>> GetRecentMessagesAsync(
        string chatId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!long.TryParse(chatId, out var chatIdLong))
            {
                _logger.LogWarning("Invalid chat ID format: {ChatId}", chatId);
                return Enumerable.Empty<SpamDetectionServices.HistoryMessage>();
            }

            // Get recent messages from repository (filtered by chat_id in query)
            var messages = await _repository.GetMessagesByChatIdAsync(chatIdLong, count);

            // Convert to spam library's HistoryMessage format
            return messages.Select(m => new SpamDetectionServices.HistoryMessage
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
            return Enumerable.Empty<SpamDetectionServices.HistoryMessage>();
        }
    }
}

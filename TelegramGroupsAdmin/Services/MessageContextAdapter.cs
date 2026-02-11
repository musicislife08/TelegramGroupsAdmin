using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using ContentDetectionServices = TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Adapter to convert from main app's MessageQueryService
/// to ContentDetection's IMessageContextProvider interface
/// </summary>
public class MessageContextAdapter : ContentDetectionServices.IMessageContextProvider
{
    private readonly IMessageQueryService _queryService;
    private readonly ILogger<MessageContextAdapter> _logger;

    public MessageContextAdapter(
        IMessageQueryService queryService,
        ILogger<MessageContextAdapter> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<IEnumerable<ContentDetectionServices.HistoryMessage>> GetRecentMessagesAsync(
        ChatIdentity chat,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get recent messages with detection history to determine spam status
            var messages = await _queryService.GetMessagesWithDetectionHistoryAsync(chat.Id, count, cancellationToken: cancellationToken);

            // Convert to spam library's HistoryMessage format
            return messages.Select(m => new ContentDetectionServices.HistoryMessage
            {
                UserId = m.Message.User.Id.ToString(),
                UserName = m.Message.User.Username ?? "Unknown",
                Message = m.Message.MessageText ?? string.Empty,
                Timestamp = m.Message.Timestamp.UtcDateTime,
                WasSpam = m.DetectionResults.Any(dr => dr.IsSpam)
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message history for {Chat}", chat.ToLogDebug());
            return [];
        }
    }
}

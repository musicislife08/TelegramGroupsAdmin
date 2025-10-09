namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Stub implementation of spam library's IMessageHistoryService
/// Returns empty message history since OpenAI context feature is currently unused
/// TODO: Implement proper adapter to convert from main app's MessageHistoryRepository
/// </summary>
public class StubMessageHistoryService : TelegramGroupsAdmin.SpamDetection.Services.IMessageHistoryService
{
    public Task<IEnumerable<TelegramGroupsAdmin.SpamDetection.Services.HistoryMessage>> GetRecentMessagesAsync(
        string chatId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        // Return empty list - OpenAI spam check will work without context
        return Task.FromResult(Enumerable.Empty<TelegramGroupsAdmin.SpamDetection.Services.HistoryMessage>());
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service implementation for retrieving message history from database
/// </summary>
public class MessageHistoryService(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<MessageHistoryService> logger) : IMessageHistoryService
{
    /// <summary>
    /// Get recent messages from a chat for context
    /// </summary>
    public async Task<IEnumerable<HistoryMessage>> GetRecentMessagesAsync(
        long chatId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Join messages with telegram_users and detection_results to get user info and spam status
            // Uses GroupJoin to ensure all messages are returned even if no detection results exist
            var messages = await (
                from m in context.Messages
                join tu in context.TelegramUsers on m.UserId equals tu.TelegramUserId into userJoin
                from tu in userJoin.DefaultIfEmpty()
                where m.ChatId == chatId
                   && m.MessageText != null
                   && m.MessageText != string.Empty
                select new
                {
                    m.MessageId,
                    m.UserId,
                    FirstName = tu != null ? tu.FirstName : null,
                    LastName = tu != null ? tu.LastName : null,
                    Username = tu != null ? tu.Username : null,
                    m.MessageText,
                    m.Timestamp,
                    DetectionResults = context.DetectionResults.Where(dr => dr.MessageId == m.MessageId)
                })
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return messages.Select(m => new HistoryMessage
            {
                UserId = m.UserId.ToString(),
                UserName = TelegramDisplayName.Format(m.FirstName, m.LastName, m.Username, m.UserId),
                Message = m.MessageText ?? string.Empty,
                Timestamp = m.Timestamp.UtcDateTime,
                WasSpam = m.DetectionResults.Any(dr => dr.IsSpam)
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve message history for chat {ChatId}", chatId);
            return [];
        }
    }
}
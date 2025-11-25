using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service implementation for retrieving message history from database
/// </summary>
public class MessageHistoryService : IMessageHistoryService
{
    private readonly IDbConnection _connection;
    private readonly ILogger<MessageHistoryService> _logger;

    public MessageHistoryService(IDbConnection connection, ILogger<MessageHistoryService> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Get recent messages from a chat for context
    /// </summary>
    public async Task<IEnumerable<HistoryMessage>> GetRecentMessagesAsync(long chatId, int count = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            // Join with detection_results to determine if message was flagged as spam
            const string sql = @"
                SELECT
                    m.message_id,
                    m.user_id,
                    m.user_name,
                    m.message_text,
                    m.timestamp,
                    COALESCE(bool_or(dr.is_spam), false) AS was_spam
                FROM messages m
                LEFT JOIN detection_results dr ON m.message_id = dr.message_id
                WHERE m.chat_id = @ChatId
                  AND m.message_text IS NOT NULL
                  AND m.message_text != ''
                GROUP BY m.message_id, m.user_id, m.user_name, m.message_text, m.timestamp
                ORDER BY m.timestamp DESC
                LIMIT @Count";

            var messages = await _connection.QueryAsync<HistoryMessageDto>(sql, new { ChatId = chatId, Count = count });

            return messages.Select(m => new HistoryMessage
            {
                UserId = m.UserId?.ToString() ?? string.Empty,
                UserName = m.UserName ?? string.Empty,
                Message = m.MessageText ?? string.Empty,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(m.Timestamp).DateTime,
                WasSpam = m.WasSpam
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve message history for chat {ChatId}", chatId);
            return [];
        }
    }

    /// <summary>
    /// DTO for database query results
    /// </summary>
    private record HistoryMessageDto
    {
        public long? UserId { get; init; }
        public string? UserName { get; init; }
        public string? MessageText { get; init; }
        public long Timestamp { get; init; }
        public bool WasSpam { get; init; }
    }
}
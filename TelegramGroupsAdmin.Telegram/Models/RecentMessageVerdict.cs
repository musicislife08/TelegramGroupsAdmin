namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>A recent chat message with its current verdict (AI veto history context).</summary>
public sealed record RecentMessageVerdict(int MessageId, long UserId, string? Username, string? MessageText, DateTimeOffset Timestamp, bool IsSpam);

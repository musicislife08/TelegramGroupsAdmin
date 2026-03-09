namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Per-chat welcome statistics for breakdown table
/// </summary>
public class ChatWelcomeStats
{
    /// <summary>Chat ID</summary>
    public long ChatId { get; set; }

    /// <summary>Chat name from managed_chats</summary>
    public string ChatName { get; set; } = string.Empty;

    /// <summary>Total joins in this chat</summary>
    public int TotalJoins { get; set; }

    /// <summary>Number who accepted</summary>
    public int AcceptedCount { get; set; }

    /// <summary>Number who denied</summary>
    public int DeniedCount { get; set; }

    /// <summary>Number who timed out</summary>
    public int TimeoutCount { get; set; }

    /// <summary>Number who left</summary>
    public int LeftCount { get; set; }

    /// <summary>Acceptance rate percentage</summary>
    public double AcceptanceRate { get; set; }

    /// <summary>Timeout rate percentage</summary>
    public double TimeoutRate { get; set; }
}

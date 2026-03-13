using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of send/edit message operation
/// </summary>
public record WebBotMessageResult(
    bool Success,
    Message? Message,
    string? ErrorMessage);

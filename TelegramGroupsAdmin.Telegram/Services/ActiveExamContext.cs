namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Context for an active exam session, including whether it's awaiting an open-ended answer.
/// </summary>
/// <param name="GroupChatId">The group chat where user joined</param>
/// <param name="AwaitingOpenEndedAnswer">True if MC questions are complete and open-ended is pending</param>
public record ActiveExamContext(long GroupChatId, bool AwaitingOpenEndedAnswer);

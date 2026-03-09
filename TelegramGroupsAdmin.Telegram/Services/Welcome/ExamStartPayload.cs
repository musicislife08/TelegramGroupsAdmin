namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Parsed /start payload from an exam deep link.
/// </summary>
/// <param name="ChatId">Group chat ID where user joined</param>
/// <param name="UserId">Target user ID taking the exam</param>
public record ExamStartPayload(long ChatId, long UserId);

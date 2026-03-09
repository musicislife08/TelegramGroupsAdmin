namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of starting an exam session
/// </summary>
/// <param name="Success">Whether the exam started successfully</param>
/// <param name="WelcomeMessageId">ID of the first exam message sent</param>
public record ExamStartResult(bool Success, int WelcomeMessageId);

namespace TelegramGroupsAdmin.Ui.Models;

public record SendMessageRequest(long ChatId, string Text, long? ReplyToMessageId);

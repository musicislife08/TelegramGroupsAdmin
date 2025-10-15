using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /delete - TEMPORARY TEST COMMAND - Delete a message (testing Telegram.Bot API)
/// </summary>
public class DeleteCommand : IBotCommand
{
    private readonly ILogger<DeleteCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "delete";
    public string Description => "[TEST] Delete a message";
    public string Usage => "/delete (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up command message

    public DeleteCommand(
        ILogger<DeleteCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return "❌ Please reply to the message you want to delete.";
        }

        var targetMessage = message.ReplyToMessage;

        try
        {
            await botClient.DeleteMessage(
                chatId: message.Chat.Id,
                messageId: targetMessage.MessageId,
                cancellationToken: cancellationToken);

            // Mark message as deleted in database
            using var scope = _serviceProvider.CreateScope();
            var messageRepository = scope.ServiceProvider.GetRequiredService<MessageHistoryRepository>();
            await messageRepository.MarkMessageAsDeletedAsync(targetMessage.MessageId, "delete_command");

            _logger.LogInformation(
                "DELETE TEST: Admin {AdminId} deleted message {MessageId} in chat {ChatId}",
                message.From?.Id,
                targetMessage.MessageId,
                message.Chat.Id);

            return "✅ Message deleted successfully!\n\n_This is a temporary test command._";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} in chat {ChatId}", targetMessage.MessageId, message.Chat.Id);
            return $"❌ Failed to delete message: {ex.Message}";
        }
    }
}

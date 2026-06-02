using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /delete - Delete the message you reply to.
/// </summary>
public class DeleteCommand : IBotCommand
{
    private readonly ILogger<DeleteCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "delete";
    public string Description => "Delete the replied-to message";
    public string Usage => "/delete (reply to message)";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up command message
    public int? DeleteResponseAfterSeconds => null;

    public DeleteCommand(
        ILogger<DeleteCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to the message you want to delete."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetMessage = message.ReplyToMessage;

        try
        {
            // Use BotMessageService for tracked deletion
            using var scope = _serviceProvider.CreateScope();
            var botMessageService = scope.ServiceProvider.GetRequiredService<IBotMessageService>();
            await botMessageService.DeleteAndMarkMessageAsync(
                message.Chat.Id,
                targetMessage.MessageId,
                deletionSource: "delete_command",
                cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} in {Chat} by {Admin}",
                targetMessage.MessageId,
                message.Chat.ToLogInfo(),
                message.From.ToLogInfo());

            return new CommandResult(TelegramMessage.Plain("✅ Message deleted successfully!"), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} in {Chat}",
                targetMessage.MessageId, message.Chat.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain($"❌ Failed to delete message: {ex.Message}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

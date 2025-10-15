using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /unban - Remove ban from user
/// </summary>
public class UnbanCommand : IBotCommand
{
    private readonly ILogger<UnbanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

    public string Name => "unban";
    public string Description => "Remove ban from user";
    public string Usage => "/unban (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation

    public UnbanCommand(
        ILogger<UnbanCommand> logger,
        IServiceProvider serviceProvider,
        ModerationActionService moderationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
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
            return "❌ Please reply to a message from the user to unban.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        try
        {
            // Get executor user ID (maps Telegram user ID to web app user ID)
            var executorId = await _moderationService.GetExecutorUserIdAsync(message.From?.Id ?? 0);

            // Execute unban action through ModerationActionService
            var result = await _moderationService.UnbanUserAsync(
                botClient,
                targetUser.Id,
                executorId,
                $"Manual unban command by {message.From?.Username ?? message.From?.Id.ToString() ?? "unknown"}",
                restoreTrust: false,
                cancellationToken);

            // Build response based on result
            if (!result.Success)
            {
                return $"❌ {result.ErrorMessage}";
            }

            var response = $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} unbanned from {result.ChatsAffected} chat(s)";

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban user {UserId}", targetUser.Id);
            return $"❌ Failed to unban user: {ex.Message}";
        }
    }
}

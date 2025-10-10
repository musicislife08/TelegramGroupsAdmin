using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /unban - Remove ban from user
/// </summary>
public class UnbanCommand : IBotCommand
{
    private readonly ILogger<UnbanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "unban";
    public string Description => "Remove ban from user";
    public string Usage => "/unban (reply to message)";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation

    public UnbanCommand(
        ILogger<UnbanCommand> logger,
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
            return "❌ Please reply to a message from the user to unban.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
        var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

        try
        {
            // Check if user has active bans
            var activeBans = await userActionsRepository.GetActiveActionsAsync(targetUser.Id, UserActionType.Ban);

            if (!activeBans.Any())
            {
                return $"ℹ️ User @{targetUser.Username ?? targetUser.Id.ToString()} is not banned.";
            }

            // Get all managed chats for cross-chat unban
            var managedChats = await managedChatsRepository.GetAllAsync();
            var unbannedChats = new List<long>();
            var failedChats = new List<(long chatId, string error)>();

            foreach (var chat in managedChats)
            {
                try
                {
                    await botClient.UnbanChatMember(
                        chatId: chat.ChatId,
                        userId: targetUser.Id,
                        onlyIfBanned: true,
                        cancellationToken: cancellationToken);

                    unbannedChats.Add(chat.ChatId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unban user {UserId} from chat {ChatId}", targetUser.Id, chat.ChatId);
                    failedChats.Add((chat.ChatId, ex.Message));
                }
            }

            // Deactivate ban records
            foreach (var ban in activeBans)
            {
                await userActionsRepository.DeactivateAsync(ban.Id);
            }

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) unbanned by {ExecutorId} from {UnbannedCount} chats",
                targetUser.Id, targetUser.Username, message.From?.Id, unbannedChats.Count);

            var response = $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} unbanned from {unbannedChats.Count} chat(s)";

            if (failedChats.Any())
            {
                response += $"\n\n⚠️ Failed to unban from {failedChats.Count} chat(s)";
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unban user {UserId}", targetUser.Id);
            return $"❌ Failed to unban user: {ex.Message}";
        }
    }
}

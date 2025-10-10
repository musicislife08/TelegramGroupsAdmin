using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command

    public BanCommand(
        ILogger<BanCommand> logger,
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
            return "❌ Please reply to a message from the user to ban.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
        var managedChatsRepository = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id);
        if (isAdmin)
        {
            return "❌ Cannot ban chat admins.";
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "Banned by admin";

        try
        {
            // Get all managed chats for cross-chat ban
            var managedChats = await managedChatsRepository.GetAllAsync();
            var bannedChats = new List<long>();
            var failedChats = new List<(long chatId, string error)>();

            foreach (var chat in managedChats)
            {
                try
                {
                    await botClient.BanChatMember(
                        chatId: chat.ChatId,
                        userId: targetUser.Id,
                        untilDate: null, // Permanent ban
                        revokeMessages: true, // Delete all messages from this user
                        cancellationToken: cancellationToken);

                    bannedChats.Add(chat.ChatId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ban user {UserId} from chat {ChatId}", targetUser.Id, chat.ChatId);
                    failedChats.Add((chat.ChatId, ex.Message));
                }
            }

            // Save ban record to user_actions (global ban - chatIds = null)
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: targetUser.Id,
                ChatIds: null, // NULL = global ban across all chats
                ActionType: UserActionType.Ban,
                MessageId: message.ReplyToMessage.MessageId,
                IssuedBy: null, // TODO: Map Telegram user to web app user
                IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpiresAt: null, // Permanent ban
                Reason: reason
            );
            await userActionsRepository.InsertAsync(banAction);

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) banned by {ExecutorId} from {BannedCount} chats. Reason: {Reason}",
                targetUser.Id, targetUser.Username, message.From?.Id, bannedChats.Count, reason);

            var response = $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} banned from {bannedChats.Count} chat(s)\n" +
                          $"Reason: {reason}";

            if (failedChats.Any())
            {
                response += $"\n\n⚠️ Failed to ban from {failedChats.Count} chat(s)";
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban user {UserId}", targetUser.Id);
            return $"❌ Failed to ban user: {ex.Message}";
        }
    }
}

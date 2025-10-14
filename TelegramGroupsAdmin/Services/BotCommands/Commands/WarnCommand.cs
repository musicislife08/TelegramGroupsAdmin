using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /warn - Issue warning to user (auto-ban after threshold)
/// </summary>
public class WarnCommand : IBotCommand
{
    private readonly ILogger<WarnCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const int WarnThreshold = 3; // Auto-ban after 3 warnings

    public string Name => "warn";
    public string Description => "Issue warning to user";
    public string Usage => "/warn (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible as public warning

    public WarnCommand(
        ILogger<WarnCommand> logger,
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
            return "❌ Please reply to a message from the user to warn.";
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
        var telegramUserMappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();

        // Check if target is admin (can't warn admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id);
        if (isAdmin)
        {
            return "❌ Cannot warn chat admins.";
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "No reason provided";

        try
        {
            // Map executor Telegram ID to web app user ID
            string? executorUserId = null;
            if (message.From?.Id != null)
            {
                executorUserId = await telegramUserMappingRepository.GetUserIdByTelegramIdAsync(message.From.Id);
            }

            // Save warning to user_actions (all warnings are global)
            var warnAction = new UserActionRecord(
                Id: 0,
                UserId: targetUser.Id,
                ActionType: UserActionType.Warn,
                MessageId: message.ReplyToMessage.MessageId,
                IssuedBy: executorUserId, // Mapped from telegram_user_mappings (may be null if not linked)
                IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpiresAt: null, // Warnings don't expire
                Reason: reason
            );
            await userActionsRepository.InsertAsync(warnAction);

            // Check warning count for this user (globally across all chats)
            var activeWarnings = await userActionsRepository.GetActiveActionsAsync(targetUser.Id, UserActionType.Warn);
            var warnCount = activeWarnings.Count;

            _logger.LogInformation(
                "Warning {WarnCount}/{Threshold} issued to user {TargetId} ({TargetUsername}) by {ExecutorId} - Reason: {Reason}",
                warnCount, WarnThreshold, targetUser.Id, targetUser.Username, message.From?.Id, reason);

            // Auto-ban if threshold exceeded
            if (warnCount >= WarnThreshold)
            {
                var managedChats = await managedChatsRepository.GetAllAsync();
                var bannedChats = new List<long>();

                foreach (var chat in managedChats)
                {
                    try
                    {
                        await botClient.BanChatMember(
                            chatId: chat.ChatId,
                            userId: targetUser.Id,
                            untilDate: null, // Permanent ban
                            revokeMessages: true,
                            cancellationToken: cancellationToken);

                        bannedChats.Add(chat.ChatId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-ban user {UserId} from chat {ChatId}", targetUser.Id, chat.ChatId);
                    }
                }

                // Save ban record (all bans are global)
                var banAction = new UserActionRecord(
                    Id: 0,
                    UserId: targetUser.Id,
                    ActionType: UserActionType.Ban,
                    MessageId: message.ReplyToMessage.MessageId,
                    IssuedBy: null,
                    IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ExpiresAt: null,
                    Reason: $"Auto-banned after {warnCount} warnings. Last: {reason}"
                );
                await userActionsRepository.InsertAsync(banAction);

                return $"⚠️ Warning issued to @{targetUser.Username ?? targetUser.Id.ToString()}\n" +
                       $"Reason: {reason}\n\n" +
                       $"🚫 **User auto-banned** after {warnCount} warnings!";
            }

            return $"⚠️ Warning {warnCount}/{WarnThreshold} issued to @{targetUser.Username ?? targetUser.Id.ToString()}\n" +
                   $"Reason: {reason}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn user {UserId}", targetUser.Id);
            return $"❌ Failed to issue warning: {ex.Message}";
        }
    }
}

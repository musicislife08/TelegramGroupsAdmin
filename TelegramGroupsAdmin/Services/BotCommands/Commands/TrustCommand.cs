using Telegram.Bot.Types;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.BotCommands.Commands;

/// <summary>
/// /trust - Whitelist user to bypass spam detection (permanent)
/// </summary>
public class TrustCommand : IBotCommand
{
    private readonly ILogger<TrustCommand> _logger;
    private readonly IUserActionsRepository _userActionsRepository;

    public string Name => "trust";
    public string Description => "Whitelist user (bypass spam detection)";
    public string Usage => "/trust (reply to message) OR /trust <username>";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => false;

    public TrustCommand(
        ILogger<TrustCommand> logger,
        IUserActionsRepository userActionsRepository)
    {
        _logger = logger;
        _userActionsRepository = userActionsRepository;
    }

    public async Task<string> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        User? targetUser = null;
        long? messageId = null;

        // Option 1: Reply to message
        if (message.ReplyToMessage != null)
        {
            targetUser = message.ReplyToMessage.From;
            messageId = message.ReplyToMessage.MessageId;
        }
        // Option 2: Username provided
        else if (args.Length > 0)
        {
            var username = args[0].TrimStart('@');

            // Try to find user in chat members (limited by Telegram API - only works for recent messages)
            // For now, return error - need to implement GetChatMember API call
            return "❌ Username lookup not yet implemented. Please reply to a message from the user.";
        }
        else
        {
            return "❌ Please reply to a message from the user OR provide username: /trust <username>";
        }

        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        // Check if user is already trusted
        var isAlreadyTrusted = await _userActionsRepository.IsUserTrustedAsync(
            targetUser.Id,
            message.Chat.Id);

        if (isAlreadyTrusted)
        {
            return $"ℹ️ User @{targetUser.Username ?? targetUser.Id.ToString()} is already trusted.";
        }

        // Create trust action (permanent, global for now - can be enhanced later for per-chat)
        var trustAction = new UserActionRecord(
            Id: 0, // Will be assigned by database
            UserId: targetUser.Id,
            ChatIds: null, // NULL = global trust (all chats)
            ActionType: UserActionType.Trust,
            MessageId: messageId,
            IssuedBy: null, // TODO: Map Telegram user to web app user
            IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAt: null, // Permanent trust
            Reason: $"Trusted by admin in chat {message.Chat.Title ?? message.Chat.Username ?? message.Chat.Id.ToString()}"
        );

        await _userActionsRepository.InsertAsync(trustAction);

        _logger.LogInformation(
            "User {TargetId} (@{TargetUsername}) trusted by {ExecutorId} in chat {ChatId}",
            targetUser.Id,
            targetUser.Username,
            message.From?.Id,
            message.Chat.Id);

        return $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} marked as trusted\n\n" +
               $"This user's messages will bypass spam detection globally.";
    }
}

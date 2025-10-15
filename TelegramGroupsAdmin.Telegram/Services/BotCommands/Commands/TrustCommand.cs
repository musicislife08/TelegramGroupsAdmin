using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /trust - Whitelist user to bypass spam detection (permanent)
/// </summary>
public class TrustCommand : IBotCommand
{
    private readonly ILogger<TrustCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

    public string Name => "trust";
    public string Description => "Whitelist user (bypass spam detection)";
    public string Usage => "/trust (reply to message) OR /trust <username>";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public TrustCommand(
        ILogger<TrustCommand> logger,
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

        using var scope = _serviceProvider.CreateScope();
        var userActionsRepository = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

        // Check if user is already trusted
        var isAlreadyTrusted = await userActionsRepository.IsUserTrustedAsync(
            targetUser.Id,
            message.Chat.Id);

        if (isAlreadyTrusted)
        {
            return $"ℹ️ User @{targetUser.Username ?? targetUser.Id.ToString()} is already trusted.";
        }

        // Map executor Telegram ID to web app user ID
        string? executorUserId = await _moderationService.GetExecutorUserIdAsync(message.From?.Id);

        // Build reason with chat context
        var chatName = message.Chat.Title ?? message.Chat.Username ?? message.Chat.Id.ToString();
        var reason = $"Trusted by admin in chat {chatName}";

        // Execute trust action via centralized service
        var result = await _moderationService.TrustUserAsync(
            userId: targetUser.Id,
            executorId: executorUserId,
            reason: reason);

        // Build response based on result
        if (!result.Success)
        {
            _logger.LogError("Failed to trust user {UserId}: {Error}", targetUser.Id, result.ErrorMessage);
            return $"❌ Failed to trust user: {result.ErrorMessage}";
        }

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

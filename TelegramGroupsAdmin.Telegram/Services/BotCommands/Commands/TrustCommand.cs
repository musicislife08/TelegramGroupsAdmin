using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /trust - Toggle trust status: trust user if not trusted, untrust if already trusted.
/// Trusted users bypass spam detection globally.
/// </summary>
public class TrustCommand : IBotCommand
{
    private readonly ILogger<TrustCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;

    public string Name => "trust";
    public string Description => "Toggle trust status (bypass spam detection)";
    public string Usage => "/trust (reply to message) OR /trust <username>";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public TrustCommand(
        ILogger<TrustCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
    }

    public async Task<CommandResult> ExecuteAsync(
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
            return new CommandResult("❌ Username lookup not yet implemented. Please reply to a message from the user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        else
        {
            return new CommandResult("❌ Please reply to a message from the user OR provide username: /trust <username>", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        // Check current trust status to determine toggle direction
        var isAlreadyTrusted = await userRepository.IsTrustedAsync(
            targetUser.Id,
            cancellationToken);

        // Create executor actor from Telegram user
        var executor = Core.Models.Actor.FromTelegramUser(
            message.From!.Id,
            message.From.Username,
            message.From.FirstName,
            message.From.LastName);

        // Build reason with chat context
        var chatName = message.Chat.Title ?? message.Chat.Username ?? message.Chat.Id.ToString();
        var userDisplay = targetUser.Username ?? targetUser.Id.ToString();

        if (isAlreadyTrusted)
        {
            // UNTRUST: User is currently trusted, remove trust
            var reason = $"Untrusted by admin in chat {chatName}";

            var result = await _moderationService.UntrustUserAsync(
                userId: targetUser.Id,
                executor: executor,
                reason: reason,
                cancellationToken: cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to untrust {User}: {Error}",
                    LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                    result.ErrorMessage);
                return new CommandResult($"❌ Failed to untrust user: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            _logger.LogInformation(
                "{TargetUser} untrusted by {Executor} in {Chat}",
                LogDisplayName.UserInfo(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                LogDisplayName.ChatInfo(message.Chat.Title, message.Chat.Id));

            return new CommandResult($"✅ User @{userDisplay} is no longer trusted\n\n" +
                   $"This user's messages will now be subject to spam detection.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        else
        {
            // TRUST: User is not trusted, add trust
            var reason = $"Trusted by admin in chat {chatName}";

            var result = await _moderationService.TrustUserAsync(
                userId: targetUser.Id,
                executor: executor,
                reason: reason,
                cancellationToken: cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to trust {User}: {Error}",
                    LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                    result.ErrorMessage);
                return new CommandResult($"❌ Failed to trust user: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            _logger.LogInformation(
                "{TargetUser} trusted by {Executor} in {Chat}",
                LogDisplayName.UserInfo(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                LogDisplayName.ChatInfo(message.Chat.Title, message.Chat.Id));

            return new CommandResult($"✅ User @{userDisplay} marked as trusted\n\n" +
                   $"This user's messages will bypass spam detection globally.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

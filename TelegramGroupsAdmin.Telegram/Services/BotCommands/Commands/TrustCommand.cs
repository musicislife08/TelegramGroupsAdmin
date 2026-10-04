using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Identity;

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
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public string Name => "trust";
    public string Description => "Toggle trust status (bypass spam detection)";
    public string Usage => "/trust (reply to message) OR /trust <username>";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false; // Keep visible for confirmation
    public int? DeleteResponseAfterSeconds => null;

    public TrustCommand(
        ILogger<TrustCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService,
        IUserIdentityService identityService,
        IConfigService configService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _identityService = identityService;
        _configService = configService;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        UserIdentity sender,
        CancellationToken cancellationToken = default)
    {
        User? replyFrom = null;
        int? messageId = null;

        // Option 1: Reply to message
        if (message.ReplyToMessage != null)
        {
            replyFrom = message.ReplyToMessage.From;
            messageId = message.ReplyToMessage.MessageId;
        }
        // Option 2: Username provided
        else if (args.Length > 0)
        {
            var username = args[0].TrimStart('@');

            // Try to find user in chat members (limited by Telegram API - only works for recent messages)
            // For now, return error - need to implement GetChatMember API call
            return new CommandResult(TelegramMessage.Plain("❌ Username lookup not yet implemented. Please reply to a message from the user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        else
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to a message from the user OR provide username: /trust <username>"), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        if (replyFrom == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = await _identityService.ResolveAsync(replyFrom.Id, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        // Check current trust status to determine toggle direction
        var isAlreadyTrusted = await userRepository.IsTrustedAsync(
            targetUser.Id,
            cancellationToken);

        var executor = Core.Models.Actor.FromUserIdentity(sender);

        // Build reason with chat context
        var chatName = message.Chat.Title ?? message.Chat.Username ?? message.Chat.Id.ToString();

        if (isAlreadyTrusted)
        {
            // UNTRUST: User is currently trusted, remove trust
            var reason = $"Untrusted by admin in chat {chatName}";

            var result = await _moderationService.UntrustUserAsync(
                new UntrustIntent
                {
                    User = targetUser,
                    Executor = executor,
                    Reason = reason
                },
                cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to untrust {User}: {Error}",
                    targetUser.ToLogDebug(),
                    result.ErrorMessage);
                return new CommandResult(TelegramMessage.Plain($"❌ Failed to untrust user: {result.ErrorMessage}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            _logger.LogInformation(
                "{TargetUser} untrusted by {Executor} in {Chat}",
                targetUser.ToLogInfo(),
                sender.ToLogInfo(),
                message.Chat.ToLogInfo());

            // The confirmation mentions the target following the chat's name-masking setting
            return new CommandResult(
                (await _configService.CreateChatMessageBuilderAsync(message.Chat.Id, cancellationToken))
                    .Text("✅ User ").Mention(targetUser).Text(" is no longer trusted").LineBreak().LineBreak()
                    .Text("This user's messages will now be subject to spam detection.")
                    .Build(),
                DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        else
        {
            // TRUST: User is not trusted, add trust
            var reason = $"Trusted by admin in chat {chatName}";

            var result = await _moderationService.TrustUserAsync(
                new TrustIntent
                {
                    User = targetUser,
                    Executor = executor,
                    Reason = reason
                },
                cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to trust {User}: {Error}",
                    targetUser.ToLogDebug(),
                    result.ErrorMessage);
                return new CommandResult(TelegramMessage.Plain($"❌ Failed to trust user: {result.ErrorMessage}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            _logger.LogInformation(
                "{TargetUser} trusted by {Executor} in {Chat}",
                targetUser.ToLogInfo(),
                sender.ToLogInfo(),
                message.Chat.ToLogInfo());

            // The confirmation mentions the target following the chat's name-masking setting
            return new CommandResult(
                (await _configService.CreateChatMessageBuilderAsync(message.Chat.Id, cancellationToken))
                    .Text("✅ User ").Mention(targetUser).Text(" marked as trusted").LineBreak().LineBreak()
                    .Text("This user's messages will bypass spam detection globally.")
                    .Build(),
                DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

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
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /warn - Issue warning to user (auto-ban after threshold)
/// Notifies user via DM if available, falls back to chat mention
/// </summary>
public class WarnCommand : IBotCommand
{
    private readonly ILogger<WarnCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;
    private readonly IUserMessagingService _messagingService;
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public string Name => "warn";
    public string Description => "Issue warning to user (auto-ban after threshold)";
    public string Usage => "/warn (reply to message) [reason]";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible as public warning
    public int? DeleteResponseAfterSeconds => null;

    public WarnCommand(
        ILogger<WarnCommand> logger,
        IServiceProvider serviceProvider,
        IBotModerationService moderationService,
        IUserMessagingService messagingService,
        IUserIdentityService identityService,
        IConfigService configService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
        _messagingService = messagingService;
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
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to a message from the user to warn."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var replyFrom = message.ReplyToMessage.From;
        if (replyFrom == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = await _identityService.ResolveAsync(replyFrom.Id, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't warn admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Cannot warn chat admins."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "No reason provided";

        try
        {
            var executor = Core.Models.Actor.FromUserIdentity(sender);
            var masking = await _configService.GetNameMaskingAsync(message.Chat.Id, cancellationToken);

            // Execute warn action using service
            var result = await _moderationService.WarnUserAsync(
                new WarnIntent
                {
                    User = targetUser,
                    Chat = ChatIdentity.From(message.Chat),
                    Executor = executor,
                    Reason = reason,
                    MessageId = message.ReplyToMessage.MessageId
                },
                cancellationToken);

            if (!result.Success)
            {
                return new CommandResult(TelegramMessage.Plain($"❌ Failed to issue warning: {result.ErrorMessage}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Notify user of warning via DM (preferred) or chat mention (fallback)
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var warningBuilder = TelegramMessageBuilder.For(masking)
                .Text("⚠️ ").Bold("Warning Issued").LineBreak().LineBreak()
                .Bold("Chat: ").Text(chatName).LineBreak()
                .Bold("Reason: ").Text(reason).LineBreak()
                .Bold("Total Warnings: ").Text(result.WarningCount.ToString()).LineBreak().LineBreak()
                .Text("Please review the group rules.");

            if (result.AutoBanTriggered)
            {
                warningBuilder
                    .LineBreak().LineBreak()
                    .Text("🚫 ").Bold("Auto-ban triggered!")
                    .Text($" You have been banned from {result.ChatsAffected} chat(s) due to excessive warnings.");
            }

            var messageResult = await _messagingService.SendToUserAsync(
                userId: targetUser.Id,
                chat: message.Chat,
                message: warningBuilder.Build(),
                replyToMessageId: message.ReplyToMessage.MessageId,
                cancellationToken: cancellationToken);

            // Build admin confirmation response; the mention follows the chat's name-masking setting
            var deliveryNote = messageResult.DeliveryMethod == MessageDeliveryMethod.PrivateDm
                ? " (notified via DM)"
                : " (notified in chat)";

            var response = TelegramMessageBuilder.For(masking)
                .Text("⚠️ Warning issued to ").Mention(targetUser).Text(deliveryNote).LineBreak()
                .Text($"Reason: {reason}").LineBreak()
                .Text($"Total warnings: {result.WarningCount}");

            if (result.AutoBanTriggered)
            {
                response.LineBreak().LineBreak()
                    .Text($"🚫 Auto-ban triggered! User has been banned from {result.ChatsAffected} chat(s).");
            }

            return new CommandResult(response.Build(), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn {User}",
                targetUser.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain($"❌ Failed to issue warning: {ex.Message}"), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }
}

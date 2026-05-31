using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /invite - Get invite link for current chat (for users in private groups)
/// Permission level: -1 (everyone, including non-admins)
/// Configurable: Global and per-chat enable/disable
/// Auto-deletes: Command and response after 30 seconds (configurable)
/// </summary>
public class InviteCommand : IBotCommand
{
    private readonly ILogger<InviteCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public string Name => "invite";
    public string Description => "Get invite link for this chat";
    public string Usage => "/invite";
    public int MinPermissionLevel => -1; // Everyone can use this
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => true; // Default, overridden by config
    public int? DeleteResponseAfterSeconds => 30; // Default, overridden by config

    public InviteCommand(
        ILogger<InviteCommand> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Only works in groups/supergroups
        if (message.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ This command only works in group chats."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        var chatId = message.Chat.Id;

        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var chatService = scope.ServiceProvider.GetRequiredService<IBotChatService>();

        // Check if command is enabled (global + per-chat override)
        var config = await configService.GetEffectiveInviteCommandAsync(chatId, cancellationToken)
                     ?? InviteCommandConfig.Default;

        if (!config.Enabled)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ The /invite command is disabled in this chat."),
                config.DeleteCommandMessage,
                config.DeleteResponseAfterSeconds);
        }

        // Get cached invite link (or fetch from Telegram if not cached)
        var inviteLink = await chatService.GetInviteLinkAsync(ChatIdentity.From(message.Chat), cancellationToken);

        if (string.IsNullOrEmpty(inviteLink))
        {
            _logger.LogWarning(
                "Failed to get invite link for chat {ChatId} ({ChatName}). Bot may lack permissions or chat is private without link.",
                chatId,
                message.Chat.Title ?? "Unknown");

            return new CommandResult(
                TelegramMessage.Plain("❌ Unable to get invite link. The bot may need admin permissions to export invite links."),
                config.DeleteCommandMessage,
                config.DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "User {UserId} (@{Username}) requested invite link for chat {ChatId} ({ChatName})",
            message.From?.Id,
            message.From?.Username ?? "none",
            chatId,
            message.Chat.Title ?? "Unknown");

        var inviteMessage = new TelegramMessageBuilder()
            .Text("🔗 ").Bold("Invite Link").LineBreak()
            .LineBreak()
            .Text(inviteLink).LineBreak()
            .LineBreak()
            .Italic($"This message will auto-delete in {config.DeleteResponseAfterSeconds} seconds")
            .Build();

        return new CommandResult(
            inviteMessage,
            config.DeleteCommandMessage,
            config.DeleteResponseAfterSeconds);
    }
}

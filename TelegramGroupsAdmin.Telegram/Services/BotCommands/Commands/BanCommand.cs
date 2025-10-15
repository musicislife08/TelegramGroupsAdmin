using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /ban - Ban user from all managed chats
/// </summary>
public class BanCommand : IBotCommand
{
    private readonly ILogger<BanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

    public string Name => "ban";
    public string Description => "Ban user from all managed chats";
    public string Usage => "/ban (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command

    public BanCommand(
        ILogger<BanCommand> logger,
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
            return "❌ Please reply to a message from the user to ban.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id);
        if (isAdmin)
        {
            return "❌ Cannot ban chat admins.";
        }

        var reason = args.Length > 0 ? string.Join(" ", args) : "Banned by admin";

        try
        {
            // Map executor Telegram ID to web app user ID
            var executorUserId = await _moderationService.GetExecutorUserIdAsync(message.From?.Id);

            // Execute ban via ModerationActionService
            var result = await _moderationService.BanUserAsync(
                botClient,
                targetUser.Id,
                message.ReplyToMessage.MessageId,
                executorUserId,
                reason,
                cancellationToken);

            if (!result.Success)
            {
                return $"❌ Failed to ban user: {result.ErrorMessage}";
            }

            // Build success message
            var response = $"✅ User @{targetUser.Username ?? targetUser.Id.ToString()} banned from {result.ChatsAffected} chat(s)\n" +
                          $"Reason: {reason}";

            if (result.TrustRemoved)
            {
                response += "\n⚠️ User trust revoked";
            }

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) banned by {ExecutorId} from {ChatsAffected} chats. Reason: {Reason}",
                targetUser.Id, targetUser.Username, message.From?.Id, result.ChatsAffected, reason);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban user {UserId}", targetUser.Id);
            return $"❌ Failed to ban user: {ex.Message}";
        }
    }
}

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
    private readonly ModerationActionService _moderationService;

    public string Name => "warn";
    public string Description => "Issue warning to user";
    public string Usage => "/warn (reply to message) [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => false; // Keep visible as public warning

    public WarnCommand(
        ILogger<WarnCommand> logger,
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
            return "❌ Please reply to a message from the user to warn.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

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
            string? executorUserId = await _moderationService.GetExecutorUserIdAsync(message.From?.Id);

            // Execute warn action using service
            var result = await _moderationService.WarnUserAsync(
                userId: targetUser.Id,
                messageId: message.ReplyToMessage.MessageId,
                executorId: executorUserId,
                reason: reason
            );

            if (!result.Success)
            {
                return $"❌ Failed to issue warning: {result.ErrorMessage}";
            }

            // Build response message
            var username = targetUser.Username ?? targetUser.Id.ToString();
            var response = $"⚠️ Warning issued to @{username}\n" +
                          $"Reason: {reason}";

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warn user {UserId}", targetUser.Id);
            return $"❌ Failed to issue warning: {ex.Message}";
        }
    }
}

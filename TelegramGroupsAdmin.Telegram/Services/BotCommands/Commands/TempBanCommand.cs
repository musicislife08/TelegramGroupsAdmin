using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /tempban - Temporarily ban user from all managed chats with auto-unrestriction
/// Sends DM notification with rejoin link if user has bot DM enabled
/// </summary>
public class TempBanCommand : IBotCommand
{
    private readonly ILogger<TempBanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

    public string Name => "tempban";
    public string Description => "Temporarily ban user with auto-unrestriction";
    public string Usage => "/tempban (reply to message) <5m|1h|24h> [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public TempBanCommand(
        ILogger<TempBanCommand> logger,
        IServiceProvider serviceProvider,
        ModerationActionService moderationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _moderationService = moderationService;
    }

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return new CommandResult("❌ Please reply to a message from the user to temp ban.", DeleteCommandMessage);
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage);
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot temp ban chat admins.", DeleteCommandMessage);
        }

        // Parse duration (default 1 hour if not specified or invalid)
        TimeSpan duration = TimeSpan.FromHours(1);
        string? reason = null;

        if (args.Length > 0)
        {
            var durationArg = args[0].ToLower();
            if (TimeSpanUtilities.TryParseDuration(durationArg, out var parsedDuration))
            {
                duration = parsedDuration;
                reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;
            }
            else
            {
                // First arg wasn't a valid duration, treat entire args as reason
                reason = string.Join(" ", args);
            }
        }

        reason ??= $"Temp banned for {TimeSpanUtilities.FormatDuration(duration)}";

        try
        {
            // Get executor actor
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName);

            // Execute temp ban via ModerationActionService
            var result = await _moderationService.TempBanUserAsync(
                botClient,
                targetUser.Id,
                message.ReplyToMessage.MessageId,
                executor,
                reason,
                duration,
                cancellationToken);

            if (!result.Success)
            {
                return new CommandResult($"❌ Failed to temp ban user: {result.ErrorMessage}", DeleteCommandMessage);
            }

            // Build success message (DM notification sent by ModerationActionService)
            var response = $"⏱️ User @{targetUser.Username ?? targetUser.Id.ToString()} temp banned from {result.ChatsAffected} chat(s)\n" +
                          $"Duration: {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"Reason: {reason}\n" +
                          $"⚠️ Will be automatically unbanned at {DateTimeOffset.UtcNow.Add(duration):yyyy-MM-dd HH:mm} UTC";

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) temp banned by {ExecutorId} from {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                targetUser.Id, targetUser.Username, message.From?.Id, result.ChatsAffected, duration, reason);

            // Return CommandResult with dynamic deletion time matching tempban duration
            return new CommandResult(response, DeleteCommandMessage, (int)duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to temp ban user {UserId}", targetUser.Id);
            return new CommandResult($"❌ Failed to temp ban user: {ex.Message}", DeleteCommandMessage);
        }
    }

}

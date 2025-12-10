using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /mute - Temporarily restrict user (remove send permissions) from all managed chats with auto-unrestriction
/// </summary>
public class MuteCommand : IBotCommand
{
    private readonly ILogger<MuteCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ModerationActionService _moderationService;

    public string Name => "mute";
    public string Description => "Temporarily mute user with auto-unmute";
    public string Usage => "/mute (reply to message) <5m|1h|24h> [reason]";
    public int MinPermissionLevel => 1; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public MuteCommand(
        ILogger<MuteCommand> logger,
        IServiceProvider serviceProvider,
        ModerationActionService moderationService)
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
        if (message.ReplyToMessage == null)
        {
            return new CommandResult("❌ Please reply to a message from the user to mute.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return new CommandResult("❌ Could not identify target user.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't mute admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult("❌ Cannot mute chat admins.", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        // Parse duration (default 5 minutes if not specified or invalid)
        TimeSpan duration = TimeSpan.FromMinutes(5);
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

        reason ??= $"Muted for {TimeSpanUtilities.FormatDuration(duration)}";

        try
        {
            // Get executor actor
            var executor = Core.Models.Actor.FromTelegramUser(
                message.From!.Id,
                message.From.Username,
                message.From.FirstName,
                message.From.LastName);

            // Execute mute via ModerationActionService
            var result = await _moderationService.RestrictUserAsync(
                userId: targetUser.Id,
                messageId: message.ReplyToMessage.MessageId,
                executor: executor,
                reason: reason,
                duration: duration,
                cancellationToken: cancellationToken);

            if (!result.Success)
            {
                return new CommandResult($"❌ Failed to mute user: {result.ErrorMessage}", DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Build success message
            var response = $"🔇 User @{targetUser.Username ?? targetUser.Id.ToString()} muted in {result.ChatsAffected} chat(s)\n" +
                          $"Duration: {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"Reason: {reason}\n" +
                          $"⚠️ Will be automatically unmuted at {DateTimeOffset.UtcNow.Add(duration):yyyy-MM-dd HH:mm} UTC";

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) muted by {ExecutorId} in {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                targetUser.Id, targetUser.Username, message.From?.Id, result.ChatsAffected, duration, reason);

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute user {UserId}", targetUser.Id);
            return new CommandResult($"❌ Failed to mute user: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }

}

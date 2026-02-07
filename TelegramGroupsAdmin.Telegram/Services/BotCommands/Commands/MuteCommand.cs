using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /mute - Temporarily restrict user (remove send permissions) from all managed chats with auto-unrestriction
/// </summary>
public class MuteCommand : IBotCommand
{
    private readonly ILogger<MuteCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;

    public string Name => "mute";
    public string Description => "Temporarily mute user with auto-unmute";
    public string Usage => "/mute (reply to message) <5m|1h|24h> [reason]";
    public int MinPermissionLevel => ModerationConstants.AdminPermissionLevel; // Admin required
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public MuteCommand(
        ILogger<MuteCommand> logger,
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
        TimeSpan duration = CommandConstants.DefaultMuteDuration;
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
                new RestrictIntent
                {
                    User = UserIdentity.From(targetUser),
                    Executor = executor,
                    Reason = reason,
                    MessageId = message.ReplyToMessage.MessageId,
                    Duration = duration
                    // Chat = null → global restriction
                },
                cancellationToken);

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
                "{TargetUser} muted by {Executor} in {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                LogDisplayName.UserInfo(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id),
                LogDisplayName.UserInfo(message.From?.FirstName, message.From?.LastName, message.From?.Username, message.From?.Id ?? 0),
                result.ChatsAffected, duration, reason);

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute {User}",
                LogDisplayName.UserDebug(targetUser.FirstName, targetUser.LastName, targetUser.Username, targetUser.Id));
            return new CommandResult($"❌ Failed to mute user: {ex.Message}", DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }

}

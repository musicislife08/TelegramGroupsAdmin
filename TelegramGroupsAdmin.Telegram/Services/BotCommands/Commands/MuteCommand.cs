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
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public string Name => "mute";
    public string Description => "Temporarily mute user with auto-unmute";
    public string Usage => "/mute (reply to message) <5m|1h|24h> [reason]";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public MuteCommand(
        ILogger<MuteCommand> logger,
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
        if (message.ReplyToMessage == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to a message from the user to mute."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var replyFrom = message.ReplyToMessage.From;
        if (replyFrom == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }

        var targetUser = await _identityService.ResolveAsync(replyFrom.Id, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't mute admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Cannot mute chat admins."), DeleteCommandMessage, DeleteResponseAfterSeconds);
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
            var executor = Core.Models.Actor.FromUserIdentity(sender);

            // Execute mute via ModerationActionService
            var result = await _moderationService.RestrictUserAsync(
                new RestrictIntent
                {
                    User = targetUser,
                    Executor = executor,
                    Reason = reason,
                    Duration = duration
                    // Chat = null → global restriction
                },
                cancellationToken);

            if (!result.Success)
            {
                // The chat reply stays generic: ErrorMessage can carry exception text
                _logger.LogWarning("Failed to mute {User}: {Error}", targetUser.ToLogDebug(), result.ErrorMessage);
                return new CommandResult(TelegramMessage.Plain("❌ Failed to mute user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
            }

            // Build success message; the mention follows the chat's name-masking setting
            var response = (await _configService.CreateChatMessageBuilderAsync(message.Chat.Id, cancellationToken))
                .Text("🔇 User ").Mention(targetUser)
                .Text($" muted in {result.ChatsAffected} chat(s)\n" +
                          $"Duration: {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"Reason: {reason}\n" +
                          $"⚠️ Will be automatically unmuted at {DateTimeOffset.UtcNow.Add(duration):yyyy-MM-dd HH:mm} UTC")
                .Build();

            _logger.LogInformation(
                "{TargetUser} muted by {Executor} in {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                targetUser.ToLogInfo(),
                sender.ToLogInfo(),
                result.ChatsAffected, duration, reason);

            return new CommandResult(response, DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute {User}",
                targetUser.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain("❌ Failed to mute user."), DeleteCommandMessage, DeleteResponseAfterSeconds);
        }
    }

}

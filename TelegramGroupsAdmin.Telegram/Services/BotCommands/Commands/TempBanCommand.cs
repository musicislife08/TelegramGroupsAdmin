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
/// /tempban - Temporarily ban user from all managed chats with auto-unrestriction
/// Sends DM notification with rejoin link if user has bot DM enabled
/// </summary>
public class TempBanCommand : IBotCommand
{
    private readonly ILogger<TempBanCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotModerationService _moderationService;
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public string Name => "tempban";
    public string Description => "Temporarily ban user with auto-unrestriction";
    public string Usage => "/tempban (reply to message) <5m|1h|24h> [reason]";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
    public bool RequiresReply => true;
    public bool DeleteCommandMessage => true; // Clean up moderation command
    public int? DeleteResponseAfterSeconds => null;

    public TempBanCommand(
        ILogger<TempBanCommand> logger,
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
            return new CommandResult(TelegramMessage.Plain("❌ Please reply to a message from the user to temp ban."), DeleteCommandMessage);
        }

        var replyFrom = message.ReplyToMessage.From;
        if (replyFrom == null)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Could not identify target user."), DeleteCommandMessage);
        }

        var targetUser = await _identityService.ResolveAsync(replyFrom.Id, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't ban admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return new CommandResult(TelegramMessage.Plain("❌ Cannot temp ban chat admins."), DeleteCommandMessage);
        }

        // Parse duration (default 1 hour if not specified or invalid)
        TimeSpan duration = CommandConstants.DefaultTempBanDuration;
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
            var executor = Core.Models.Actor.FromUserIdentity(sender);

            // Execute temp ban via ModerationActionService
            var result = await _moderationService.TempBanUserAsync(
                new TempBanIntent
                {
                    User = targetUser,
                    Executor = executor,
                    Reason = reason,
                    MessageId = message.ReplyToMessage.MessageId,
                    Duration = duration
                },
                cancellationToken);

            if (!result.Success)
            {
                return new CommandResult(TelegramMessage.Plain($"❌ Failed to temp ban user: {result.ErrorMessage}"), DeleteCommandMessage);
            }

            // Build success message (DM notification sent by ModerationActionService);
            // the mention follows the chat's name-masking setting
            var masking = await _configService.GetNameMaskingAsync(message.Chat.Id, cancellationToken);
            var response = TelegramMessageBuilder.For(masking)
                .Text("⏱️ User ").Mention(targetUser)
                .Text($" temp banned from {result.ChatsAffected} chat(s)\n" +
                          $"Duration: {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"Reason: {reason}\n" +
                          $"⚠️ Will be automatically unbanned at {DateTimeOffset.UtcNow.Add(duration):yyyy-MM-dd HH:mm} UTC")
                .Build();

            _logger.LogInformation(
                "{TargetUser} temp banned by {Executor} from {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                targetUser.ToLogInfo(),
                sender.ToLogInfo(),
                result.ChatsAffected, duration, reason);

            // Return CommandResult with dynamic deletion time matching tempban duration
            return new CommandResult(response, DeleteCommandMessage, (int)duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to temp ban {User}",
                targetUser.ToLogDebug());
            return new CommandResult(TelegramMessage.Plain("❌ Failed to temp ban user."), DeleteCommandMessage);
        }
    }

}

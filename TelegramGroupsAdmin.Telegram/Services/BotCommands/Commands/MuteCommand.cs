using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
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

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage == null)
        {
            return "❌ Please reply to a message from the user to mute.";
        }

        var targetUser = message.ReplyToMessage.From;
        if (targetUser == null)
        {
            return "❌ Could not identify target user.";
        }

        using var scope = _serviceProvider.CreateScope();
        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        // Check if target is admin (can't mute admins)
        var isAdmin = await chatAdminsRepository.IsAdminAsync(message.Chat.Id, targetUser.Id, cancellationToken);
        if (isAdmin)
        {
            return "❌ Cannot mute chat admins.";
        }

        // Parse duration (default 5 minutes if not specified or invalid)
        TimeSpan duration = TimeSpan.FromMinutes(5);
        string? reason = null;

        if (args.Length > 0)
        {
            var durationArg = args[0].ToLower();
            if (TryParseDuration(durationArg, out var parsedDuration))
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

        reason ??= $"Muted for {FormatDuration(duration)}";

        try
        {
            // Get executor identifier (web app user ID if mapped, otherwise Telegram username/ID)
            var executorId = await _moderationService.GetExecutorIdentifierAsync(
                message.From!.Id,
                message.From.Username,
                cancellationToken);

            // Execute mute via ModerationActionService
            var result = await _moderationService.RestrictUserAsync(
                botClient,
                targetUser.Id,
                message.ReplyToMessage.MessageId,
                executorId,
                reason,
                duration,
                cancellationToken);

            if (!result.Success)
            {
                return $"❌ Failed to mute user: {result.ErrorMessage}";
            }

            // Build success message
            var response = $"🔇 User @{targetUser.Username ?? targetUser.Id.ToString()} muted in {result.ChatsAffected} chat(s)\n" +
                          $"Duration: {FormatDuration(duration)}\n" +
                          $"Reason: {reason}\n" +
                          $"⚠️ Will be automatically unmuted at {DateTimeOffset.UtcNow.Add(duration):yyyy-MM-dd HH:mm} UTC";

            _logger.LogInformation(
                "User {TargetId} ({TargetUsername}) muted by {ExecutorId} in {ChatsAffected} chats for {Duration}. Reason: {Reason}",
                targetUser.Id, targetUser.Username, message.From?.Id, result.ChatsAffected, duration, reason);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute user {UserId}", targetUser.Id);
            return $"❌ Failed to mute user: {ex.Message}";
        }
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        // Support formats: 5m, 1h, 24h, 5min, 1hr, 1hour, 24hours
        input = input.ToLower().Trim();

        if (input.EndsWith("m") || input.EndsWith("min") || input.EndsWith("mins"))
        {
            var numberPart = input.TrimEnd('m', 'i', 'n', 's');
            if (int.TryParse(numberPart, out var minutes))
            {
                duration = TimeSpan.FromMinutes(minutes);
                return true;
            }
        }
        else if (input.EndsWith("h") || input.EndsWith("hr") || input.EndsWith("hrs") || input.EndsWith("hour") || input.EndsWith("hours"))
        {
            var numberPart = input.TrimEnd('h', 'r', 's', 'o', 'u');
            if (int.TryParse(numberPart, out var hours))
            {
                duration = TimeSpan.FromHours(hours);
                return true;
            }
        }

        return false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 60)
        {
            return $"{(int)duration.TotalMinutes} minute{((int)duration.TotalMinutes != 1 ? "s" : "")}";
        }
        else if (duration.TotalHours < 24)
        {
            return $"{(int)duration.TotalHours} hour{((int)duration.TotalHours != 1 ? "s" : "")}";
        }
        else
        {
            return $"{(int)duration.TotalDays} day{((int)duration.TotalDays != 1 ? "s" : "")}";
        }
    }
}

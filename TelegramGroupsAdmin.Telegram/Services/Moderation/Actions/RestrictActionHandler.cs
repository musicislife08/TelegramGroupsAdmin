using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles restrict intents by muting users across all managed chats.
/// This is the domain expert for restrictions - it owns the Telegram restrict API integration.
/// </summary>
public class RestrictActionHandler : IActionHandler<RestrictIntent, RestrictResult>
{
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly ILogger<RestrictActionHandler> _logger;

    public RestrictActionHandler(
        ICrossChatExecutor crossChatExecutor,
        ILogger<RestrictActionHandler> logger)
    {
        _crossChatExecutor = crossChatExecutor;
        _logger = logger;
    }

    public async Task<RestrictResult> HandleAsync(RestrictIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Executing restriction for user {UserId} for {Duration} by {Executor}",
            intent.UserId, intent.Duration, intent.Executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(intent.Duration);

            // Restrict with no permissions (full mute)
            var mutePermissions = new ChatPermissions
            {
                CanSendMessages = false,
                CanSendAudios = false,
                CanSendDocuments = false,
                CanSendPhotos = false,
                CanSendVideos = false,
                CanSendVideoNotes = false,
                CanSendVoiceNotes = false,
                CanSendPolls = false,
                CanSendOtherMessages = false,
                CanAddWebPagePreviews = false,
                CanChangeInfo = false,
                CanInviteUsers = false,
                CanPinMessages = false,
                CanManageTopics = false
            };

            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.RestrictChatMemberAsync(
                    chatId: chatId,
                    userId: intent.UserId,
                    permissions: mutePermissions,
                    untilDate: expiresAt.DateTime,
                    ct: token),
                "Restrict",
                ct);

            _logger.LogInformation(
                "Restriction completed for user {UserId}: {Success} succeeded, {Failed} failed. Expires at {ExpiresAt}",
                intent.UserId, crossResult.SuccessCount, crossResult.FailCount, expiresAt);

            return RestrictResult.Succeeded(crossResult.SuccessCount, expiresAt, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute restriction for user {UserId}", intent.UserId);
            return RestrictResult.Failed(ex.Message);
        }
    }
}

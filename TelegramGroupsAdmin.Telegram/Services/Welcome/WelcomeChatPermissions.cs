using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Predefined ChatPermissions configurations for the welcome system.
/// Static readonly instances to avoid allocations on every user join/accept.
/// </summary>
public static class WelcomeChatPermissions
{
    /// <summary>
    /// Restricted permissions (all false) for new users awaiting welcome acceptance.
    /// Users cannot send any messages or media until they accept the rules.
    /// </summary>
    public static readonly ChatPermissions Restricted = new()
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

    /// <summary>
    /// Default permissions for accepted users (messaging enabled, admin features restricted).
    /// Used as fallback when chat's default permissions aren't available.
    /// </summary>
    public static readonly ChatPermissions Default = new()
    {
        CanSendMessages = true,
        CanSendAudios = true,
        CanSendDocuments = true,
        CanSendPhotos = true,
        CanSendVideos = true,
        CanSendVideoNotes = true,
        CanSendVoiceNotes = true,
        CanSendPolls = true,
        CanSendOtherMessages = true,
        CanAddWebPagePreviews = true,
        CanChangeInfo = false,      // Admin feature - restricted
        CanInviteUsers = true,
        CanPinMessages = false,     // Admin feature - restricted
        CanManageTopics = false     // Admin feature - restricted
    };
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Extracted profile data passed to the scoring engine.
/// Decouples scoring from the WTelegram API types.
/// </summary>
public record ProfileData(
    UserIdentity User,
    ChatIdentity Chat,
    string? FirstName,
    string? LastName,
    string? Username,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    int StoryCount,
    IReadOnlyList<string>? StoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified);

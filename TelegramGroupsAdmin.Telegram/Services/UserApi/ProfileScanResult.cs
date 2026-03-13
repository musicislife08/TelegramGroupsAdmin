using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Result of a profile scan containing all extracted data, computed score, and outcome.
/// </summary>
public record ProfileScanResult(
    long TelegramUserId,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified,
    decimal Score,
    ProfileScanOutcome Outcome,
    string? AiReason,
    string[]? AiSignalsDetected,
    bool ContainsNudity = false,
    string? SkipReason = null);

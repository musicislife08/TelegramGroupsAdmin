using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response containing notification preferences configuration.
/// </summary>
public record NotificationPreferencesResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Whether the user has a linked Telegram account (for TelegramDm channel availability).
    /// </summary>
    public bool HasTelegramLinked { get; init; }

    /// <summary>
    /// Per-channel notification preferences.
    /// </summary>
    public List<ChannelPreference> Channels { get; init; } = [];

    public static NotificationPreferencesResponse Ok(bool hasTelegramLinked, List<ChannelPreference> channels) => new()
    {
        Success = true,
        HasTelegramLinked = hasTelegramLinked,
        Channels = channels
    };

    public static NotificationPreferencesResponse Fail(string error) => new() { Success = false, Error = error };
}

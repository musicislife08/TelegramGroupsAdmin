using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to save notification preferences.
/// </summary>
public record NotificationPreferencesRequest(List<ChannelPreference> Channels);

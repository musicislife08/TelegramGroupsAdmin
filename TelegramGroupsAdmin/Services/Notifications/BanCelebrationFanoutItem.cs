using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// One celebration awaiting subscriber fan-out. Carries ids, not a GIF snapshot, so the worker
/// always sees the latest cached file_id.
/// </summary>
internal sealed record BanCelebrationFanoutItem(ChatIdentity Chat, string Caption, int GifId);

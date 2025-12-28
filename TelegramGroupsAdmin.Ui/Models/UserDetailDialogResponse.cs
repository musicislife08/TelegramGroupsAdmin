using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the UserDetailDialog.
/// Bundles all data needed to render the dialog in a single HTTP call.
/// Reduces network round-trips for WASM clients.
/// </summary>
public record UserDetailDialogResponse(
    TelegramUserDetail? UserDetail,
    Dictionary<string, TagColor> TagColors
);

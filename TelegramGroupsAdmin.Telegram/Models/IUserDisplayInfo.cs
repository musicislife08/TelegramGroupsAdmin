namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Common user display properties shared between <see cref="TelegramUserListItem"/> and <see cref="BannedUserListItem"/>.
/// Used by the UserInfoCell shared component.
/// </summary>
public interface IUserDisplayInfo
{
    long TelegramUserId { get; }
    string? Username { get; }
    string? FirstName { get; }
    string? LastName { get; }
    string? UserPhotoPath { get; }
    bool IsTrusted { get; }
    bool IsAdmin { get; }
    bool IsTagged { get; }
    string DisplayName { get; }
}

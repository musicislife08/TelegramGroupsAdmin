using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Invite records
/// </summary>
public static class InviteMappings
{
    public static UiModels.InviteRecord ToModel(this DataModels.InviteRecordDto data) => new(
        Token: data.Token,
        CreatedBy: data.CreatedBy,
        CreatedAt: data.CreatedAt,
        ExpiresAt: data.ExpiresAt,
        UsedBy: data.UsedBy,
        PermissionLevel: (PermissionLevel)data.PermissionLevel,
        Status: (UiModels.InviteStatus)data.Status,
        ModifiedAt: data.ModifiedAt
    );

    public static UiModels.InviteWithCreator ToModel(this DataModels.InviteWithCreatorDto data) => new(
        Token: data.Invite.Token,
        CreatedBy: data.Invite.CreatedBy,
        CreatedByEmail: data.CreatorEmail,
        CreatedAt: data.Invite.CreatedAt,
        ExpiresAt: data.Invite.ExpiresAt,
        UsedBy: data.Invite.UsedBy,
        UsedByEmail: null, // Not available in Data model - would need additional lookup
        PermissionLevel: (PermissionLevel)data.Invite.PermissionLevel,
        Status: (UiModels.InviteStatus)data.Invite.Status,
        ModifiedAt: data.Invite.ModifiedAt
    );

    public static DataModels.InviteRecordDto ToDto(this UiModels.InviteRecord ui) => new()
    {
        Token = ui.Token,
        CreatedBy = ui.CreatedBy,
        CreatedAt = ui.CreatedAt,
        ExpiresAt = ui.ExpiresAt,
        UsedBy = ui.UsedBy,
        PermissionLevel = (DataModels.PermissionLevel)(int)ui.PermissionLevel,
        Status = (DataModels.InviteStatus)(int)ui.Status,
        ModifiedAt = ui.ModifiedAt
    };
}

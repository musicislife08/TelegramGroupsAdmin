using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Mappings;

/// <summary>
/// Mapping extensions for Invite records
/// </summary>
public static class InviteMappings
{
    extension(DataModels.InviteRecordDto data)
    {
        public InviteRecord ToModel() => new(
            Token: data.Token,
            CreatedBy: data.CreatedBy,
            CreatedAt: data.CreatedAt,
            ExpiresAt: data.ExpiresAt,
            UsedBy: data.UsedBy,
            PermissionLevel: (PermissionLevel)data.PermissionLevel,
            Status: (InviteStatus)data.Status,
            ModifiedAt: data.ModifiedAt
        );
    }

    extension(DataModels.InviteWithCreatorDto data)
    {
        public InviteWithCreator ToModel() => new(
            Token: data.Invite.Token,
            CreatedBy: data.Invite.CreatedBy,
            CreatedByEmail: data.CreatorEmail,
            CreatedAt: data.Invite.CreatedAt,
            ExpiresAt: data.Invite.ExpiresAt,
            UsedBy: data.Invite.UsedBy,
            UsedByEmail: null, // Not available in Data model - would need additional lookup
            PermissionLevel: (PermissionLevel)data.Invite.PermissionLevel,
            Status: (InviteStatus)data.Invite.Status,
            ModifiedAt: data.Invite.ModifiedAt
        );
    }

    extension(InviteRecord ui)
    {
        public DataModels.InviteRecordDto ToDto() => new()
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
}

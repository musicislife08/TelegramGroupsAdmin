namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Filter options for querying invites by status (enum values match InviteStatus for direct comparison)
/// </summary>
public enum InviteFilter
{
    /// <summary>Show only pending (unused, not revoked) invites</summary>
    Pending = 0,

    /// <summary>Show only used invites</summary>
    Used = 1,

    /// <summary>Show only revoked invites</summary>
    Revoked = 2,

    /// <summary>Show all invites regardless of status</summary>
    All = -1
}

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Filter options for querying invites by status
/// </summary>
public enum InviteFilter
{
    /// <summary>Show only pending invites</summary>
    Pending = 0,

    /// <summary>Show only used invites</summary>
    Used = 1,

    /// <summary>Show only revoked invites</summary>
    Revoked = 2,

    /// <summary>Show all invites regardless of status</summary>
    All = 3
}

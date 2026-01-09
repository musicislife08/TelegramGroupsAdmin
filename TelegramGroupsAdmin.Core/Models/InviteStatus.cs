namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Invite token status lifecycle
/// </summary>
public enum InviteStatus
{
    /// <summary>Invite available and unused</summary>
    Pending = 0,

    /// <summary>Invite redeemed by a user</summary>
    Used = 1,

    /// <summary>Invite cancelled by creator or admin</summary>
    Revoked = 2
}

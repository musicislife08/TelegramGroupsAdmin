using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services;

public class UserManagementService(UserRepository userRepository, IAuditService auditLog) : IUserManagementService
{
    public async Task<List<UserRecord>> GetAllUsersAsync(CancellationToken ct = default)
    {
        return await userRepository.GetAllAsync(ct);
    }

    public async Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        return await userRepository.GetByIdAsync(userId, ct);
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken ct = default)
    {
        // Validate permission level (0=ReadOnly, 1=Admin, 2=Owner)
        if (permissionLevel < 0 || permissionLevel > 2)
        {
            throw new ArgumentException("Permission level must be 0 (ReadOnly), 1 (Admin), or 2 (Owner)", nameof(permissionLevel));
        }

        // Get old permission level for audit log
        var user = await userRepository.GetByIdAsync(userId, ct);
        var oldPermissionLevel = user?.PermissionLevel;

        await userRepository.UpdatePermissionLevelAsync(userId, permissionLevel, modifiedBy, ct);

        // Audit log
        var permissionName = permissionLevel switch
        {
            0 => "ReadOnly",
            1 => "Admin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        await auditLog.LogEventAsync(
            AuditEventType.UserPermissionChanged,
            actorUserId: modifiedBy,
            targetUserId: userId,
            value: $"Changed to {permissionName}",
            ct: ct);
    }

    public async Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken ct = default)
    {
        await userRepository.UpdateStatusAsync(userId, newStatus, modifiedBy, ct);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserStatusChanged,
            actorUserId: modifiedBy,
            targetUserId: userId,
            value: $"Status changed to {newStatus}",
            ct: ct);
    }

    public async Task SetUserActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        await userRepository.SetActiveAsync(userId, isActive, ct);
    }

    public async Task Reset2FAAsync(string userId, string modifiedBy, CancellationToken ct = default)
    {
        // Reset TOTP (clears secret, disables TOTP, clears setup timestamp)
        await userRepository.ResetTotpAsync(userId, ct);

        // Delete all recovery codes
        await userRepository.DeleteRecoveryCodesAsync(userId, ct);

        // Update security stamp to invalidate existing sessions
        await userRepository.UpdateSecurityStampAsync(userId, ct);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpDisabled,
            actorUserId: modifiedBy,
            targetUserId: userId,
            value: "2FA reset by admin",
            ct: ct);
    }
}

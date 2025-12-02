using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public class UserManagementService(IUserRepository userRepository, IAuditService auditLog) : IUserManagementService
{
    public async Task<List<UserRecord>> GetAllUsersAsync(CancellationToken ct = default)
    {
        return await userRepository.GetAllAsync(ct);
    }

    public async Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken ct = default)
    {
        return await userRepository.GetByIdAsync(userId, ct);
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, int modifierPermissionLevel, CancellationToken ct = default)
    {
        // Validate permission level (0=Admin, 1=GlobalAdmin, 2=Owner)
        if (permissionLevel is < 0 or > 2)
        {
            throw new ArgumentException("Permission level must be 0 (Admin), 1 (GlobalAdmin), or 2 (Owner)", nameof(permissionLevel));
        }

        // Escalation prevention: Users cannot assign permission levels above their own
        if (permissionLevel > modifierPermissionLevel)
        {
            var modifierPermissionName = modifierPermissionLevel switch
            {
                0 => "Admin",
                1 => "GlobalAdmin",
                2 => "Owner",
                _ => modifierPermissionLevel.ToString()
            };

            var requestedPermissionName = permissionLevel switch
            {
                0 => "Admin",
                1 => "GlobalAdmin",
                2 => "Owner",
                _ => permissionLevel.ToString()
            };

            throw new UnauthorizedAccessException(
                $"Cannot assign permission level {requestedPermissionName} (your level: {modifierPermissionName})");
        }

        // Get old permission level for audit log
        var user = await userRepository.GetByIdAsync(userId, ct);
        var oldPermissionLevel = user?.PermissionLevel;

        await userRepository.UpdatePermissionLevelAsync(userId, permissionLevel, modifiedBy, ct);

        // Audit log
        var permissionName = permissionLevel switch
        {
            0 => "Admin",
            1 => "GlobalAdmin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        await auditLog.LogEventAsync(
            AuditEventType.UserPermissionChanged,
            actor: Actor.FromWebUser(modifiedBy),
            target: Actor.FromWebUser(userId),
            value: $"Changed to {permissionName}",
            ct: ct);
    }

    public async Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken ct = default)
    {
        await userRepository.UpdateStatusAsync(userId, newStatus, modifiedBy, ct);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserStatusChanged,
            actor: Actor.FromWebUser(modifiedBy),
            target: Actor.FromWebUser(userId),
            value: $"Status changed to {newStatus}",
            ct: ct);
    }

    public async Task SetUserActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        await userRepository.SetActiveAsync(userId, isActive, ct);
    }

    public async Task Reset2FaAsync(string userId, string modifiedBy, CancellationToken ct = default)
    {
        // Reset TOTP (clears secret, disables TOTP, clears setup timestamp)
        await userRepository.ResetTotpAsync(userId, ct);

        // Delete all recovery codes
        await userRepository.DeleteRecoveryCodesAsync(userId, ct);

        // Update security stamp to invalidate existing sessions
        await userRepository.UpdateSecurityStampAsync(userId, ct);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpReset,
            actor: Actor.FromWebUser(modifiedBy),
            target: Actor.FromWebUser(userId),
            value: "2FA reset by admin",
            ct: ct);
    }
}

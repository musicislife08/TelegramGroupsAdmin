using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public class UserManagementService(IUserRepository userRepository, IAuditService auditLog) : IUserManagementService
{
    public async Task<List<UserRecord>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        return await userRepository.GetAllAsync(cancellationToken);
    }

    public async Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await userRepository.GetByIdAsync(userId, cancellationToken);
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, int modifierPermissionLevel, CancellationToken cancellationToken = default)
    {
        // Validate permission level (0=Admin, 1=GlobalAdmin, 2=Owner)
        if (permissionLevel is < 0 or > 2)
        {
            throw new ArgumentException("Permission level must be 0 (Admin), 1 (GlobalAdmin), or 2 (Owner)", nameof(permissionLevel));
        }

        // Escalation prevention: Users cannot assign permission levels above their own
        if (permissionLevel > modifierPermissionLevel)
        {
            var modifierPermissionName = ((PermissionLevel)modifierPermissionLevel).ToStringFast();
            var requestedPermissionName = ((PermissionLevel)permissionLevel).ToStringFast();

            throw new UnauthorizedAccessException(
                $"Cannot assign permission level {requestedPermissionName} (your level: {modifierPermissionName})");
        }

        // Get old permission level for audit log
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        var oldPermissionLevel = user?.PermissionLevel;

        await userRepository.UpdatePermissionLevelAsync(userId, permissionLevel, modifiedBy, cancellationToken);

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
            cancellationToken: cancellationToken);
    }

    public async Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken cancellationToken = default)
    {
        await userRepository.UpdateStatusAsync(userId, newStatus, modifiedBy, cancellationToken);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserStatusChanged,
            actor: Actor.FromWebUser(modifiedBy),
            target: Actor.FromWebUser(userId),
            value: $"Status changed to {newStatus}",
            cancellationToken: cancellationToken);
    }

    public async Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await userRepository.SetActiveAsync(userId, isActive, cancellationToken);
    }

    public async Task Reset2FaAsync(string userId, string modifiedBy, CancellationToken cancellationToken = default)
    {
        // Reset TOTP (clears secret, disables TOTP, clears setup timestamp)
        await userRepository.ResetTotpAsync(userId, cancellationToken);

        // Delete all recovery codes
        await userRepository.DeleteRecoveryCodesAsync(userId, cancellationToken);

        // Update security stamp to invalidate existing sessions
        await userRepository.UpdateSecurityStampAsync(userId, cancellationToken);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpReset,
            actor: Actor.FromWebUser(modifiedBy),
            target: Actor.FromWebUser(userId),
            value: "2FA reset by admin",
            cancellationToken: cancellationToken);
    }
}

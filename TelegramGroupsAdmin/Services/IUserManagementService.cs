using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

public interface IUserManagementService
{
    Task<List<UserRecord>> GetAllUsersAsync(CancellationToken ct = default);
    Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken ct = default);
    Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken ct = default);
    Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken ct = default);
    Task SetUserActiveAsync(string userId, bool isActive, CancellationToken ct = default); // Deprecated
    Task Reset2FAAsync(string userId, string modifiedBy, CancellationToken ct = default);
}

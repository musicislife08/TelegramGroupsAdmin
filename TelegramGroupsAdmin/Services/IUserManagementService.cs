using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public interface IUserManagementService
{
    Task<List<UserRecord>> GetAllUsersAsync(CancellationToken cancellationToken = default);
    Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, int modifierPermissionLevel, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken cancellationToken = default);
    Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default); // Deprecated
    Task Reset2FaAsync(string userId, string modifiedBy, CancellationToken cancellationToken = default);
}

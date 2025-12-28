using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Server.Repositories;

/// <summary>
/// Repository for invite management operations
/// </summary>
public interface IInviteRepository
{
    /// <summary>
    /// Get an invite by its token
    /// </summary>
    Task<UiModels.InviteRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new invite from an InviteRecord model
    /// </summary>
    Task CreateAsync(UiModels.InviteRecord invite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new invite and return the generated token
    /// </summary>
    Task<string> CreateAsync(string createdBy, int validDays = 7, int permissionLevel = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an invite as used by a specific user
    /// </summary>
    Task MarkAsUsedAsync(string token, string usedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all invites created by a specific user
    /// </summary>
    Task<List<UiModels.InviteRecord>> GetByCreatorAsync(string createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired invites and return count of deleted records
    /// </summary>
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all invites filtered by status
    /// </summary>
    Task<List<UiModels.InviteRecord>> GetAllAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all invites with creator and used-by email addresses, filtered by status
    /// </summary>
    Task<List<UiModels.InviteWithCreator>> GetAllWithCreatorEmailAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a pending invite and return true if successful
    /// </summary>
    Task<bool> RevokeAsync(string token, CancellationToken cancellationToken = default);
}

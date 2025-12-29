using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository interface for managing admin notes on Telegram users
/// </summary>
public interface IAdminNotesRepository
{
    /// <summary>
    /// Get all notes for a specific Telegram user
    /// </summary>
    Task<List<AdminNote>> GetNotesByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific note by ID
    /// </summary>
    Task<AdminNote?> GetNoteByIdAsync(long noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new note
    /// </summary>
    Task<long> AddNoteAsync(AdminNote note, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing note
    /// </summary>
    Task<bool> UpdateNoteAsync(AdminNote note, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a note
    /// </summary>
    Task<bool> DeleteNoteAsync(long noteId, Actor deletedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pinned notes for a user
    /// </summary>
    Task<List<AdminNote>> GetPinnedNotesByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggle pin status for a note
    /// </summary>
    Task<bool> TogglePinAsync(long noteId, Actor toggledBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all notes for multiple users (for batch loading in message lists)
    /// </summary>
    Task<Dictionary<long, List<AdminNote>>> GetNotesByUserIdsAsync(IEnumerable<long> telegramUserIds, CancellationToken cancellationToken = default);
}

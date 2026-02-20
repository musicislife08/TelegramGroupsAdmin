using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing WTelegram/MTProto session records.
/// Handles CRUD operations for per-admin Telegram User API sessions.
/// </summary>
public interface ITelegramSessionRepository
{
    /// <summary>
    /// Get the active session for a web user (at most one per user, enforced by partial unique index)
    /// </summary>
    Task<TelegramSession?> GetActiveSessionAsync(string webUserId, CancellationToken ct);

    /// <summary>
    /// Get all active sessions across all users (for startup reconnection and GetAnyClientAsync)
    /// </summary>
    Task<List<TelegramSession>> GetAllActiveSessionsAsync(CancellationToken ct);

    /// <summary>
    /// Check if any active session exists without materializing rows or decrypting session data
    /// </summary>
    Task<bool> AnyActiveSessionExistsAsync(CancellationToken ct);

    /// <summary>
    /// Create a new session record. Returns the generated ID.
    /// </summary>
    Task<long> CreateSessionAsync(TelegramSession session, CancellationToken ct);

    /// <summary>
    /// Update the last_used_at timestamp for an active session
    /// </summary>
    Task UpdateLastUsedAsync(long sessionId, CancellationToken ct);

    /// <summary>
    /// Update the encrypted session data (called when WTelegram flushes session changes)
    /// </summary>
    Task UpdateSessionDataAsync(long sessionId, byte[] sessionData, CancellationToken ct);

    /// <summary>
    /// Deactivate a session (set is_active=false, disconnected_at=now, clear session_data)
    /// </summary>
    Task DeactivateSessionAsync(long sessionId, CancellationToken ct);
}

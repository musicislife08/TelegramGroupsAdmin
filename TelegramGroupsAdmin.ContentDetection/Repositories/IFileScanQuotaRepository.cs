using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository interface for tracking cloud service quota usage
/// Supports both calendar-based (daily/monthly) and rolling window quotas
/// </summary>
public interface IFileScanQuotaRepository
{
    /// <summary>
    /// Check if quota is available for a service
    /// Returns true if current count &lt; limit, false otherwise
    /// </summary>
    Task<bool> IsQuotaAvailableAsync(
        string serviceName,
        string quotaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment quota usage for a service
    /// Creates quota record if it doesn't exist
    /// </summary>
    Task IncrementQuotaUsageAsync(
        string serviceName,
        string quotaType,
        int limitValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current quota usage for a service
    /// Returns null if no quota record exists for current window
    /// </summary>
    Task<FileScanQuotaModel?> GetCurrentQuotaAsync(
        string serviceName,
        string quotaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired quota records (older than their window)
    /// Daily quotas: delete if window_end &lt; now
    /// Monthly quotas: delete if window_end &lt; now
    /// </summary>
    Task<int> CleanupExpiredQuotasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all quota records for a service (for UI display)
    /// </summary>
    Task<List<FileScanQuotaModel>> GetServiceQuotasAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset quota usage for a service (admin override)
    /// </summary>
    Task ResetQuotaAsync(
        string serviceName,
        string quotaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active quotas for UI dashboard display (Phase 4.22)
    /// Returns all quota records within their current windows
    /// </summary>
    Task<List<FileScanQuotaModel>> GetAllActiveQuotasAsync(CancellationToken cancellationToken = default);
}

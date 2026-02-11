using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Pre-filter service for hard-blocking URLs before spam detection
/// Phase 4.13: URL Filtering
/// </summary>
public interface IUrlPreFilterService
{
    /// <summary>
    /// Check if message should be hard-blocked based on URL filters
    /// Runs BEFORE spam detection - instant ban, no OpenAI veto
    /// </summary>
    Task<HardBlockResult> CheckHardBlockAsync(string messageText, ChatIdentity chat, CancellationToken cancellationToken = default);
}

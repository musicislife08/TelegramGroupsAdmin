using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for the spam/ham/scan corpora consumed by stop word recommendation generation.
/// Every corpus follows each message's current verdict (message_verdicts view), not a raw
/// detection_results row.
/// </summary>
public interface IStopWordCorpusRepository
{
    /// <summary>
    /// Gets data-availability counts (training-spam messages, non-spam messages, ContentScan
    /// detection results) since the given time, for the recommendation batch's minimum-data gate.
    /// </summary>
    Task<StopWordCorpusCounts> GetCountsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets message text (translated text preferred) for messages whose current verdict is in
    /// TrainingSpam, posted since the given time.
    /// </summary>
    Task<IReadOnlyList<string>> GetSpamTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets message text (translated text preferred) for messages whose current verdict is not
    /// spam (includes Unscanned), posted since the given time.
    /// </summary>
    Task<IReadOnlyList<string>> GetLegitTextsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ContentScan detection_results rows with check_results_json since the given time,
    /// for stop word removal precision analysis. Excludes non-scan decision rows.
    /// </summary>
    Task<IReadOnlyList<ScanCheckResults>> GetScanCheckResultsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

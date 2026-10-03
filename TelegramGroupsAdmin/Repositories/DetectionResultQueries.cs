using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>Reusable detection_results filters for analytics queries (compose into EF LINQ).</summary>
internal static class DetectionResultQueries
{
    extension(IQueryable<DetectionResultRecordDto> results)
    {
        /// <summary>Detector output only: content scans (no admin decisions, no file scans).</summary>
        public IQueryable<DetectionResultRecordDto> ContentScans() =>
            results.Where(dr => dr.Source == (int)VerdictSource.ContentScan);

        /// <summary>Content scans that classified the message as spam.</summary>
        public IQueryable<DetectionResultRecordDto> ContentScanSpam() =>
            results.ContentScans().Where(dr => VerdictClassifications.SpamValues.Contains(dr.Classification));
    }
}

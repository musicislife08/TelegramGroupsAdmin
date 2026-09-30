using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Layer 1 media similarity samples: messages that carry media features and whose current verdict
/// (message_verdicts view) is a training sample, newest verdict first. Admin corrections apply
/// automatically because the current verdict is read at query time. Each sample carries its current
/// classification, so a check can tell an admin-verified (ExplicitHam) anchor from an auto-scanned one.
/// </summary>
public interface IMediaSampleRepository
{
    /// <summary>Photo samples (features + the current verdict's classification), newest verdict first.</summary>
    Task<IReadOnlyList<(PhotoFeatures Features, VerdictClassification Classification)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Video samples (keyframe features + the current verdict's classification), newest verdict first.</summary>
    Task<IReadOnlyList<(VideoFeatures Features, VerdictClassification Classification)>> GetRecentVideoSamplesAsync(int limit, CancellationToken cancellationToken = default);
}

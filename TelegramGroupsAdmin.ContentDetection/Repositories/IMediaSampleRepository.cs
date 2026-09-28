using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Layer 1 media similarity samples: messages that carry media features and whose current verdict
/// (message_verdicts view) is a training sample, newest verdict first. Admin corrections apply
/// automatically because the current verdict is read at query time.
/// </summary>
public interface IMediaSampleRepository
{
    /// <summary>Photo samples (features + whether the current verdict is spam), newest verdict first.</summary>
    Task<IReadOnlyList<(PhotoFeatures Features, bool IsSpam)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Video samples (keyframe features + whether the current verdict is spam), newest verdict first.</summary>
    Task<IReadOnlyList<(VideoFeatures Features, bool IsSpam)>> GetRecentVideoSamplesAsync(int limit, CancellationToken cancellationToken = default);
}

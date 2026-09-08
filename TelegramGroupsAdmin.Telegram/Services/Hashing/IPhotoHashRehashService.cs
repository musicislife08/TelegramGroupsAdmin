namespace TelegramGroupsAdmin.Telegram.Services.Hashing;

/// <summary>
/// Refills perceptual hashes cleared by the v1-to-v2 hash migration.
///
/// Idempotent by construction: it only visits rows whose hash is NULL, so a row
/// already refilled is skipped by the query itself and no completion marker is
/// needed. Rows whose source image is gone stay NULL permanently and are revisited
/// cheaply on each run in case the file reappears.
/// </summary>
public interface IPhotoHashRehashService
{
    Task<PhotoHashRehashResult> RehashAsync(CancellationToken ct = default);
}

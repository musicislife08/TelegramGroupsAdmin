using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <inheritdoc />
public class MediaSampleRepository(IDbContextFactory<AppDbContext> contextFactory) : IMediaSampleRepository
{
    private static readonly int[] TrainingSampleValues =
        [.. VerdictClassifications.TrainingSpamValues, (int)VerdictClassification.ExplicitHam, (int)VerdictClassification.ImplicitHam];

    public Task<IReadOnlyList<(PhotoFeatures Features, VerdictClassification Classification)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken cancellationToken = default)
        => LoadAsync<PhotoFeatures>(photo: true, limit, cancellationToken);

    public Task<IReadOnlyList<(VideoFeatures Features, VerdictClassification Classification)>> GetRecentVideoSamplesAsync(int limit, CancellationToken cancellationToken = default)
        => LoadAsync<VideoFeatures>(photo: false, limit, cancellationToken);

    /// <summary>
    /// media_features is stored through a value converter, so SQL cannot read its "type": PhotoFileId
    /// pre-selects photos vs videos in the query, and the type check on the deserialized features is
    /// the authority (a row whose stored features are the other kind is skipped).
    /// </summary>
    private async Task<IReadOnlyList<(T Features, VerdictClassification Classification)>> LoadAsync<T>(bool photo, int limit, CancellationToken cancellationToken)
        where T : MediaFeatures
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var messages = context.Messages.AsNoTracking().Where(m => m.MediaFeatures != null);
        messages = photo
            ? messages.Where(m => m.PhotoFileId != null)
            : messages.Where(m => m.PhotoFileId == null);

        var rows = await (
            from v in context.MessageVerdicts.AsNoTracking()
            where TrainingSampleValues.Contains(v.Classification)
            join m in messages on new { v.MessageId, v.ChatId } equals new { m.MessageId, m.ChatId }
            orderby v.DetectedAt descending
            select new { m.MediaFeatures, v.Classification }
        ).Take(limit).ToListAsync(cancellationToken);

        return [.. rows
            .Select(r => (Features: r.MediaFeatures!.ToModel(), Classification: (VerdictClassification)r.Classification))
            .Where(r => r.Features is T)
            .Select(r => ((T)r.Features, r.Classification))];
    }
}

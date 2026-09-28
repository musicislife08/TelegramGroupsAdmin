using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <inheritdoc />
public class MediaSampleRepository(IDbContextFactory<AppDbContext> contextFactory) : IMediaSampleRepository
{
    private static readonly int[] TrainingSampleValues =
        [.. VerdictClassifications.TrainingSpamValues, (int)VerdictClassification.ExplicitHam, (int)VerdictClassification.ImplicitHam];

    public async Task<IReadOnlyList<(PhotoFeatures Features, bool IsSpam)>> GetRecentPhotoSamplesAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAsync(photo: true, limit, cancellationToken);
        return [.. rows
            .Select(r => (Features: r.Features.ToModel(), r.IsSpam))
            .Where(r => r.Features is PhotoFeatures)
            .Select(r => ((PhotoFeatures)r.Features, r.IsSpam))];
    }

    public async Task<IReadOnlyList<(VideoFeatures Features, bool IsSpam)>> GetRecentVideoSamplesAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAsync(photo: false, limit, cancellationToken);
        return [.. rows
            .Select(r => (Features: r.Features.ToModel(), r.IsSpam))
            .Where(r => r.Features is VideoFeatures)
            .Select(r => ((VideoFeatures)r.Features, r.IsSpam))];
    }

    private async Task<List<(DataModels.MediaFeaturesDto Features, bool IsSpam)>> LoadAsync(bool photo, int limit, CancellationToken cancellationToken)
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
            select new { m.MediaFeatures, v.IsSpam }
        ).Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(r => (r.MediaFeatures!, r.IsSpam))];
    }
}

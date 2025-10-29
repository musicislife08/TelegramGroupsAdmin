using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class WelcomeResponsesRepository : IWelcomeResponsesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<WelcomeResponsesRepository> _logger;

    public WelcomeResponsesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<WelcomeResponsesRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> InsertAsync(WelcomeResponse response, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = response.ToDto();
        context.WelcomeResponses.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded welcome response: User {UserId} (@{Username}) in chat {ChatId} - {Response} (DM: {DmSent}, Fallback: {DmFallback}, JobId: {JobId})",
            response.UserId,
            response.Username,
            response.ChatId,
            response.Response,
            response.DmSent,
            response.DmFallback,
            response.TimeoutJobId);

        return entity.Id;
    }

    public async Task<WelcomeResponse?> GetByUserAndChatAsync(long userId, long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.WelcomeResponses
            .AsNoTracking()
            .Where(wr => wr.UserId == userId && wr.ChatId == chatId)
            .OrderByDescending(wr => wr.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }

    public async Task UpdateResponseAsync(long id, WelcomeResponseType responseType, bool dmSent = false, bool dmFallback = false, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.WelcomeResponses.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Attempted to update non-existent welcome response ID {Id}", id);
            return;
        }

        entity.Response = (DataModels.WelcomeResponseType)responseType;
        entity.RespondedAt = DateTimeOffset.UtcNow;
        entity.DmSent = dmSent;
        entity.DmFallback = dmFallback;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated welcome response {Id}: {Response} (DM: {DmSent}, Fallback: {DmFallback})",
            id,
            responseType,
            dmSent,
            dmFallback);
    }

    public async Task SetTimeoutJobIdAsync(long id, Guid? jobId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.WelcomeResponses.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Attempted to set timeout job ID for non-existent welcome response ID {Id}", id);
            return;
        }

        entity.TimeoutJobId = jobId;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Set timeout job ID for welcome response {Id}: {JobId}", id, jobId);
    }

    public async Task<List<WelcomeResponse>> GetByChatIdAsync(long chatId, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.WelcomeResponses
            .AsNoTracking()
            .Where(wr => wr.ChatId == chatId)
            .OrderByDescending(wr => wr.RespondedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<WelcomeStats> GetStatsAsync(long? chatId = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.WelcomeResponses.AsNoTracking();

        if (chatId.HasValue)
        {
            query = query.Where(wr => wr.ChatId == chatId.Value);
        }

        if (since.HasValue)
        {
            query = query.Where(wr => wr.RespondedAt >= since.Value);
        }

        var responses = await query.ToListAsync(cancellationToken);

        var total = responses.Count;
        if (total == 0)
        {
            return new WelcomeStats(0, 0, 0, 0, 0, 0.0, 0, 0, 0.0);
        }

        var accepted = responses.Count(r => (int)r.Response == (int)WelcomeResponseType.Accepted);
        var denied = responses.Count(r => (int)r.Response == (int)WelcomeResponseType.Denied);
        var timeout = responses.Count(r => (int)r.Response == (int)WelcomeResponseType.Timeout);
        var left = responses.Count(r => (int)r.Response == (int)WelcomeResponseType.Left);

        var dmSent = responses.Count(r => r.DmSent);
        var dmFallback = responses.Count(r => r.DmFallback);
        var dmAttempted = dmSent + dmFallback;

        var acceptanceRate = total > 0 ? (double)accepted / total * 100.0 : 0.0;
        var dmSuccessRate = dmAttempted > 0 ? (double)dmSent / dmAttempted * 100.0 : 0.0;

        return new WelcomeStats(
            TotalResponses: total,
            AcceptedCount: accepted,
            DeniedCount: denied,
            TimeoutCount: timeout,
            LeftCount: left,
            AcceptanceRate: acceptanceRate,
            DmSuccessCount: dmSent,
            DmFallbackCount: dmFallback,
            DmSuccessRate: dmSuccessRate
        );
    }
}

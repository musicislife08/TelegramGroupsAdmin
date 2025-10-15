using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public interface IWelcomeResponsesRepository
{
    Task<long> InsertAsync(WelcomeResponse response);
    Task<List<WelcomeResponse>> GetByChatIdAsync(long chatId, int limit = 100);
    Task<WelcomeStats> GetStatsAsync(long? chatId = null, DateTimeOffset? since = null);
}

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

    public async Task<long> InsertAsync(WelcomeResponse response)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = response.ToDataModel();
        context.WelcomeResponses.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Recorded welcome response: User {UserId} (@{Username}) in chat {ChatId} - {Response} (DM: {DmSent}, Fallback: {DmFallback})",
            response.UserId,
            response.Username,
            response.ChatId,
            response.Response,
            response.DmSent,
            response.DmFallback);

        return entity.Id;
    }

    public async Task<List<WelcomeResponse>> GetByChatIdAsync(long chatId, int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.WelcomeResponses
            .AsNoTracking()
            .Where(wr => wr.ChatId == chatId)
            .OrderByDescending(wr => wr.RespondedAt)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<WelcomeStats> GetStatsAsync(long? chatId = null, DateTimeOffset? since = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.WelcomeResponses.AsNoTracking();

        if (chatId.HasValue)
        {
            query = query.Where(wr => wr.ChatId == chatId.Value);
        }

        if (since.HasValue)
        {
            query = query.Where(wr => wr.RespondedAt >= since.Value);
        }

        var responses = await query.ToListAsync();

        var total = responses.Count;
        if (total == 0)
        {
            return new WelcomeStats(0, 0, 0, 0, 0, 0.0, 0, 0, 0.0);
        }

        var accepted = responses.Count(r => r.Response == "accepted");
        var denied = responses.Count(r => r.Response == "denied");
        var timeout = responses.Count(r => r.Response == "timeout");
        var left = responses.Count(r => r.Response == "left");

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

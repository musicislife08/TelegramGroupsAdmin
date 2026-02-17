using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class TelegramSessionRepository(IDbContextFactory<AppDbContext> contextFactory) : ITelegramSessionRepository
{
    public async Task<TelegramSession?> GetActiveSessionAsync(string webUserId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.TelegramSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(ts => ts.WebUserId == webUserId && ts.IsActive, ct);

        return dto?.ToModel();
    }

    public async Task<List<TelegramSession>> GetAllActiveSessionsAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dtos = await context.TelegramSessions
            .AsNoTracking()
            .Where(ts => ts.IsActive)
            .ToListAsync(ct);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<long> CreateSessionAsync(TelegramSession session, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = session.ToDto();

        context.TelegramSessions.Add(dto);
        await context.SaveChangesAsync(ct);

        return dto.Id;
    }

    public async Task UpdateLastUsedAsync(long sessionId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(ts => ts.LastUsedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task UpdateSessionDataAsync(long sessionId, byte[] sessionData, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(ts => ts.SessionData, sessionData), ct);
    }

    public async Task DeactivateSessionAsync(long sessionId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.TelegramSessions
            .Where(ts => ts.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ts => ts.IsActive, false)
                .SetProperty(ts => ts.DisconnectedAt, DateTimeOffset.UtcNow)
                .SetProperty(ts => ts.SessionData, Array.Empty<byte>()), ct);
    }
}

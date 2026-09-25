using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <inheritdoc />
public sealed class BanCelebrationSubscriberRepository(
    IDbContextFactory<AppDbContext> contextFactory) : IBanCelebrationSubscriberRepository
{
    public async Task<bool> UpsertAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        if (await context.BanCelebrationSubscribers.AnyAsync(
                s => s.TelegramUserId == telegramUserId && s.ChatId == chatId, ct))
        {
            return false;
        }

        context.BanCelebrationSubscribers.Add(new BanCelebrationSubscriberDto
        {
            TelegramUserId = telegramUserId,
            ChatId = chatId,
            SubscribedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent /dmcelebrations on inserted the same row between AnyAsync and SaveChanges.
            return false;
        }
    }

    public async Task<BanCelebrationSubscriber?> GetAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationSubscribers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId, ct);
        return dto?.ToModel();
    }

    public async Task<bool> DeleteAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var deleted = await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<int> DeleteAllForUserAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> HasDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationSubscribers
            .AnyAsync(s => s.ChatId == chatId && s.TelegramUser!.BotDmEnabled, ct);
    }

    public async Task<List<UserIdentity>> GetDeliverableSubscribersAsync(long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var users = await context.BanCelebrationSubscribers.AsNoTracking()
            .Where(s => s.ChatId == chatId && s.TelegramUser!.BotDmEnabled)
            .OrderBy(s => s.SubscribedAt)
            .Select(s => s.TelegramUser!)
            .ToListAsync(ct);
        return users.Select(UserIdentity.From).ToList();
    }

    public async Task SetPromptAsync(long telegramUserId, long chatId, int promptMessageId, string promptDeleteJobId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PromptMessageId, promptMessageId)
                .SetProperty(s => s.PromptDeleteJobId, promptDeleteJobId), ct);
    }

    public async Task ClearPromptAsync(long telegramUserId, long chatId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.BanCelebrationSubscribers
            .Where(s => s.TelegramUserId == telegramUserId && s.ChatId == chatId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PromptMessageId, (int?)null)
                .SetProperty(s => s.PromptDeleteJobId, (string?)null), ct);
    }
}

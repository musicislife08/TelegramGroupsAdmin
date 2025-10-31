using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing message edit history
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageEditService : IMessageEditService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MessageEditService> _logger;

    public MessageEditService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MessageEditService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task InsertMessageEditAsync(UiModels.MessageEditRecord edit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = edit.ToDto();
        context.MessageEdits.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Inserted edit for message {MessageId} at {EditDate}",
            edit.MessageId,
            edit.EditDate);
    }

    public async Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.MessageEdits
            .AsNoTracking()
            .Where(e => e.MessageId == messageId)
            .OrderBy(e => e.EditDate)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var messageIdArray = messageIds.ToArray();

        var results = await context.MessageEdits
            .AsNoTracking()
            .Where(e => messageIdArray.Contains(e.MessageId))
            .GroupBy(e => e.MessageId)
            .Select(g => new { MessageId = g.Key, EditCount = g.Count() })
            .ToListAsync(cancellationToken);

        return results.ToDictionary(r => r.MessageId, r => r.EditCount);
    }
}

using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing Telegram admin status per chat
/// Caches admin permissions to avoid API calls on every command
/// </summary>
public class ChatAdminsRepository : IChatAdminsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ChatAdminsRepository> _logger;

    public ChatAdminsRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<ChatAdminsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> GetPermissionLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var admin = await context.ChatAdmins
            .AsNoTracking()
            .Where(ca => ca.ChatId == chatId && ca.TelegramId == telegramId && ca.IsActive == true)
            .Select(ca => new { ca.IsCreator })
            .FirstOrDefaultAsync(cancellationToken);

        if (admin == null)
        {
            return -1; // Not an admin
        }

        return admin.IsCreator ? 2 : 1; // Creator = Owner (2), Admin = Admin (1)
    }

    /// <inheritdoc/>
    public async Task<bool> IsAdminAsync(long chatId, long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatAdmins
            .AsNoTracking()
            .AnyAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId && ca.IsActive == true, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<ChatAdmin>> GetChatAdminsAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.ChatAdmins
            .AsNoTracking()
            .Include(ca => ca.TelegramUser)
                .ThenInclude(tu => tu!.UserMappings.Where(m => m.IsActive))
                    .ThenInclude(m => m.User)
            .Where(ca => ca.ChatId == chatId && ca.IsActive == true)
            .OrderByDescending(ca => ca.IsCreator)
            .ThenBy(ca => ca.PromotedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<long>> GetAdminChatsAsync(long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var chatIds = await context.ChatAdmins
            .AsNoTracking()
            .Where(ca => ca.TelegramId == telegramId && ca.IsActive == true)
            .OrderBy(ca => ca.ChatId)
            .Select(ca => ca.ChatId)
            .ToListAsync(cancellationToken);

        return chatIds;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(long chatId, long telegramId, bool isCreator, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var existing = await context.ChatAdmins
            .FirstOrDefaultAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.IsCreator = isCreator;
            existing.LastVerifiedAt = now;
            existing.IsActive = true;
        }
        else
        {
            // Insert new record
            var newAdmin = new Data.Models.ChatAdminRecordDto
            {
                ChatId = chatId,
                TelegramId = telegramId,
                IsCreator = isCreator,
                PromotedAt = now,
                LastVerifiedAt = now,
                IsActive = true
            };
            context.ChatAdmins.Add(newAdmin);
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Upserted admin: chat={ChatId}, user={TelegramId}, creator={IsCreator}",
            chatId, telegramId, isCreator);
    }

    /// <inheritdoc/>
    public async Task DeactivateAsync(ChatIdentity chat, UserIdentity user, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var admins = await context.ChatAdmins
            .Where(ca => ca.ChatId == chat.Id && ca.TelegramId == user.Id)
            .ToListAsync(cancellationToken);

        foreach (var admin in admins)
        {
            admin.IsActive = false;
            admin.LastVerifiedAt = now;
        }

        var rowsAffected = admins.Count;
        await context.SaveChangesAsync(cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deactivated admin: chat={Chat}, user={User}",
                chat.ToLogInfo(), user.ToLogInfo());
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByChatIdAsync(ChatIdentity chat, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var toDelete = await context.ChatAdmins
            .Where(ca => ca.ChatId == chat.Id)
            .ToListAsync(cancellationToken);

        var rowsAffected = toDelete.Count;

        if (rowsAffected > 0)
        {
            context.ChatAdmins.RemoveRange(toDelete);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted {Count} admin records for {Chat}",
                rowsAffected, chat.ToLogInfo());
        }
    }

    /// <inheritdoc/>
    public async Task UpdateLastVerifiedAsync(long chatId, long telegramId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var admin = await context.ChatAdmins
            .FirstOrDefaultAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId, cancellationToken);

        if (admin != null)
        {
            admin.LastVerifiedAt = now;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetAdminCountAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ChatAdmins
            .Where(ca => ca.ChatId == chatId && ca.IsActive)
            .CountAsync(cancellationToken);
    }
}

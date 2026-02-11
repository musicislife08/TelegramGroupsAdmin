using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class ManagedChatsRepository : IManagedChatsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ManagedChatsRepository> _logger;

    public ManagedChatsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ManagedChatsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task UpsertAsync(ManagedChatRecord chat, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chat.Identity.Id, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.ChatName = chat.Identity.ChatName;
            existing.ChatType = (Data.Models.ManagedChatType)(int)chat.ChatType;
            existing.BotStatus = (Data.Models.BotChatStatus)(int)chat.BotStatus;
            existing.IsAdmin = chat.IsAdmin;
            existing.IsActive = chat.IsActive;
            existing.IsDeleted = false; // Restore if previously deleted (re-add scenario)
            existing.LastSeenAt = chat.LastSeenAt;
            // Only update settings if provided (COALESCE logic)
            if (chat.SettingsJson != null)
            {
                existing.SettingsJson = chat.SettingsJson;
            }
            // Update icon path if provided
            if (chat.ChatIconPath != null)
            {
                existing.ChatIconPath = chat.ChatIconPath;
            }
        }
        else
        {
            // Insert new record
            var entity = chat.ToDto();
            context.ManagedChats.Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Upserted managed chat {ChatId} ({ChatName}): {BotStatus}, admin={IsAdmin}, active={IsActive}",
            chat.Identity.Id,
            chat.Identity.ChatName,
            chat.BotStatus,
            chat.IsAdmin,
            chat.IsActive);
    }

    public async Task<ManagedChatRecord?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ManagedChats
            .AsNoTracking()
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId, cancellationToken);

        return entity?.ToModel();
    }

    /// <inheritdoc/>
    public async Task<List<ManagedChatRecord>> GetByChatIdsAsync(
        IEnumerable<long> chatIds,
        CancellationToken cancellationToken = default)
    {
        var idList = chatIds.ToList();
        if (idList.Count == 0)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => idList.Contains(mc.ChatId))
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetActiveChatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true && mc.IsDeleted == false)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAdminChatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true && mc.IsAdmin == true && mc.IsDeleted == false)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<bool> IsActiveAndAdminAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ManagedChats
            .AsNoTracking()
            .AnyAsync(mc => mc.ChatId == chatId && mc.IsActive == true && mc.IsAdmin == true && mc.IsDeleted == false, cancellationToken);
    }

    public async Task MarkInactiveAsync(ChatIdentity chat, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chat.Id, cancellationToken);

        if (entity != null)
        {
            entity.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Marked {Chat} as inactive", chat.ToLogInfo());
        }
    }

    public async Task UpdateLastSeenAsync(long chatId, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.LastSeenAt = timestamp;
        }
        else
        {
            // UPSERT: Insert if chat doesn't exist (with minimal default values)
            // Infer chat type from chat ID format:
            // - Positive ID = Private chat (user ID)
            // - Negative ID without 100 prefix = Group
            // - Negative ID with 100 prefix (-100xxxxxxxxx) = Supergroup
            var chatType = chatId > 0
                ? Data.Models.ManagedChatType.Private
                : (chatId.ToString().StartsWith("-100")
                    ? Data.Models.ManagedChatType.Supergroup
                    : Data.Models.ManagedChatType.Group);

            var newChat = new Data.Models.ManagedChatRecordDto
            {
                ChatId = chatId,
                ChatName = "Unknown",
                ChatType = chatType,
                BotStatus = Data.Models.BotChatStatus.Member,
                IsAdmin = false,
                AddedAt = timestamp,
                IsActive = true,
                LastSeenAt = timestamp,
                SettingsJson = null
            };
            context.ManagedChats.Add(newChat);

            _logger.LogDebug(
                "Auto-created managed chat {ChatId} with inferred type {ChatType}",
                chatId,
                chatType);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ManagedChatRecord>> GetAllChatsAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ManagedChats.AsNoTracking();

        if (!includeDeleted)
        {
            query = query.Where(mc => mc.IsDeleted == false);
        }

        var entities = await query
            .OrderByDescending(mc => mc.IsActive)
            .ThenBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllChatsAsync(includeDeleted: false, cancellationToken);
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await context.ManagedChats.FirstOrDefaultAsync(mc => mc.ChatId == chatId, cancellationToken);

        if (chat != null)
        {
            // Soft delete - set IsDeleted flag instead of removing
            chat.IsDeleted = true;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Soft-deleted managed chat {ChatId} ({ChatName})",
                chatId,
                chat.ChatName);
        }
        else
        {
            _logger.LogWarning(
                "Cannot delete chat {ChatId} - not found in database",
                chatId);
        }
    }

    public async Task<List<ManagedChatRecord>> GetUserAccessibleChatsAsync(
        string userId,
        PermissionLevel permissionLevel,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // GlobalAdmin (1) and Owner (2) see all chats (active + inactive)
        if (permissionLevel >= PermissionLevel.GlobalAdmin)
        {
            return await GetAllChatsAsync(includeDeleted, cancellationToken);
        }

        // Admin (0) sees only chats where their linked Telegram account is admin
        // Query: managed_chats JOIN chat_admins ON chat_id WHERE telegram_id IN (user's linked accounts)
        var query =
            from mc in context.ManagedChats
            join ca in context.ChatAdmins on mc.ChatId equals ca.ChatId
            join tum in context.TelegramUserMappings on ca.TelegramId equals tum.TelegramId
            where tum.UserId == userId
                && tum.IsActive == true
            select mc;

        if (!includeDeleted)
        {
            query = query.Where(mc => mc.IsDeleted == false);
        }

        var accessibleChats = await query
            .Distinct()
            .OrderBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        var result = accessibleChats.Select(e => e.ToModel()).ToList();

        _logger.LogDebug(
            "User {UserId} (Admin) has access to {Count} chats",
            userId,
            result.Count);

        return result;
    }
}

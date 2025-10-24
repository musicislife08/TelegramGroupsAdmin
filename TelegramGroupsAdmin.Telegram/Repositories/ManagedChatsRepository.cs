using Microsoft.EntityFrameworkCore;
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
            .FirstOrDefaultAsync(mc => mc.ChatId == chat.ChatId, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.ChatName = chat.ChatName;
            existing.ChatType = (Data.Models.ManagedChatType)(int)chat.ChatType;
            existing.BotStatus = (Data.Models.BotChatStatus)(int)chat.BotStatus;
            existing.IsAdmin = chat.IsAdmin;
            existing.IsActive = chat.IsActive;
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
            chat.ChatId,
            chat.ChatName,
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

    public async Task<List<ManagedChatRecord>> GetActiveChatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAdminChatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true && mc.IsAdmin == true)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<bool> IsActiveAndAdminAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ManagedChats
            .AsNoTracking()
            .AnyAsync(mc => mc.ChatId == chatId && mc.IsActive == true && mc.IsAdmin == true, cancellationToken);
    }

    public async Task MarkInactiveAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId, cancellationToken);

        if (entity != null)
        {
            entity.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Marked chat {ChatId} as inactive", chatId);
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

    public async Task<List<ManagedChatRecord>> GetAllChatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ManagedChats
            .AsNoTracking()
            .OrderByDescending(mc => mc.IsActive)
            .ThenBy(mc => mc.ChatName)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetActiveChatsAsync(cancellationToken);
    }

    public async Task DeleteAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await context.ManagedChats.FirstOrDefaultAsync(mc => mc.ChatId == chatId, cancellationToken);

        if (chat != null)
        {
            context.ManagedChats.Remove(chat);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Deleted managed chat {ChatId} ({ChatName})",
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
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // GlobalAdmin (1) and Owner (2) see all active chats
        if (permissionLevel >= PermissionLevel.GlobalAdmin)
        {
            return await GetActiveChatsAsync(cancellationToken);
        }

        // Admin (0) sees only chats where their linked Telegram account is admin
        // Query: managed_chats JOIN chat_admins ON chat_id WHERE telegram_id IN (user's linked accounts)
        var accessibleChats = await (
            from mc in context.ManagedChats
            join ca in context.ChatAdmins on mc.ChatId equals ca.ChatId
            join tum in context.TelegramUserMappings on ca.TelegramId equals tum.TelegramId
            where mc.IsActive == true
                && tum.UserId == userId
                && tum.IsActive == true
            select mc
        )
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

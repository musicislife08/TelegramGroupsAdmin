using Microsoft.EntityFrameworkCore;
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

    public async Task UpsertAsync(ManagedChatRecord chat)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chat.ChatId);

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

        await context.SaveChangesAsync();

        _logger.LogDebug(
            "Upserted managed chat {ChatId} ({ChatName}): {BotStatus}, admin={IsAdmin}, active={IsActive}",
            chat.ChatId,
            chat.ChatName,
            chat.BotStatus,
            chat.IsAdmin,
            chat.IsActive);
    }

    public async Task<ManagedChatRecord?> GetByChatIdAsync(long chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.ManagedChats
            .AsNoTracking()
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId);

        return entity?.ToModel();
    }

    public async Task<List<ManagedChatRecord>> GetActiveChatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAdminChatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.ManagedChats
            .AsNoTracking()
            .Where(mc => mc.IsActive == true && mc.IsAdmin == true)
            .OrderBy(mc => mc.ChatName)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<bool> IsActiveAndAdminAsync(long chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ManagedChats
            .AsNoTracking()
            .AnyAsync(mc => mc.ChatId == chatId && mc.IsActive == true && mc.IsAdmin == true);
    }

    public async Task MarkInactiveAsync(long chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId);

        if (entity != null)
        {
            entity.IsActive = false;
            await context.SaveChangesAsync();

            _logger.LogInformation("Marked chat {ChatId} as inactive", chatId);
        }
    }

    public async Task UpdateLastSeenAsync(long chatId, DateTimeOffset timestamp)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.ManagedChats
            .FirstOrDefaultAsync(mc => mc.ChatId == chatId);

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

        await context.SaveChangesAsync();
    }

    public async Task<List<ManagedChatRecord>> GetAllChatsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.ManagedChats
            .AsNoTracking()
            .OrderByDescending(mc => mc.IsActive)
            .ThenBy(mc => mc.ChatName)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAllAsync()
    {
        return await GetActiveChatsAsync();
    }

    public async Task DeleteAsync(long chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var chat = await context.ManagedChats.FirstOrDefaultAsync(mc => mc.ChatId == chatId);

        if (chat != null)
        {
            context.ManagedChats.Remove(chat);
            await context.SaveChangesAsync();

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
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing Telegram admin status per chat
/// Caches admin permissions to avoid API calls on every command
/// </summary>
public class ChatAdminsRepository : IChatAdminsRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<ChatAdminsRepository> _logger;

    public ChatAdminsRepository(AppDbContext context, ILogger<ChatAdminsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> GetPermissionLevelAsync(long chatId, long telegramId)
    {
        var admin = await _context.ChatAdmins
            .AsNoTracking()
            .Where(ca => ca.ChatId == chatId && ca.TelegramId == telegramId && ca.IsActive == true)
            .Select(ca => new { ca.IsCreator })
            .FirstOrDefaultAsync();

        if (admin == null)
        {
            return -1; // Not an admin
        }

        return admin.IsCreator ? 2 : 1; // Creator = Owner (2), Admin = Admin (1)
    }

    /// <inheritdoc/>
    public async Task<bool> IsAdminAsync(long chatId, long telegramId)
    {
        return await _context.ChatAdmins
            .AsNoTracking()
            .AnyAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId && ca.IsActive == true);
    }

    /// <inheritdoc/>
    public async Task<List<ChatAdmin>> GetChatAdminsAsync(long chatId)
    {
        var entities = await _context.ChatAdmins
            .AsNoTracking()
            .Where(ca => ca.ChatId == chatId && ca.IsActive == true)
            .OrderByDescending(ca => ca.IsCreator)
            .ThenBy(ca => ca.PromotedAt)
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<long>> GetAdminChatsAsync(long telegramId)
    {
        var chatIds = await _context.ChatAdmins
            .AsNoTracking()
            .Where(ca => ca.TelegramId == telegramId && ca.IsActive == true)
            .OrderBy(ca => ca.ChatId)
            .Select(ca => ca.ChatId)
            .ToListAsync();

        return chatIds;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(long chatId, long telegramId, bool isCreator, string? username = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var existing = await _context.ChatAdmins
            .FirstOrDefaultAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId);

        if (existing != null)
        {
            // Update existing record
            existing.Username = username;
            existing.IsCreator = isCreator;
            existing.LastVerifiedAt = now;
            existing.IsActive = true;
        }
        else
        {
            // Insert new record
            var newAdmin = new Data.Models.ChatAdminRecord
            {
                ChatId = chatId,
                TelegramId = telegramId,
                Username = username,
                IsCreator = isCreator,
                PromotedAt = now,
                LastVerifiedAt = now,
                IsActive = true
            };
            _context.ChatAdmins.Add(newAdmin);
        }

        await _context.SaveChangesAsync();

        _logger.LogDebug("Upserted admin: chat={ChatId}, user={TelegramId} (@{Username}), creator={IsCreator}",
            chatId, telegramId, username ?? "unknown", isCreator);
    }

    /// <inheritdoc/>
    public async Task DeactivateAsync(long chatId, long telegramId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var admins = await _context.ChatAdmins
            .Where(ca => ca.ChatId == chatId && ca.TelegramId == telegramId)
            .ToListAsync();

        foreach (var admin in admins)
        {
            admin.IsActive = false;
            admin.LastVerifiedAt = now;
        }

        var rowsAffected = admins.Count;
        await _context.SaveChangesAsync();

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deactivated admin: chat={ChatId}, user={TelegramId}", chatId, telegramId);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByChatIdAsync(long chatId)
    {
        var toDelete = await _context.ChatAdmins
            .Where(ca => ca.ChatId == chatId)
            .ToListAsync();

        var rowsAffected = toDelete.Count;

        if (rowsAffected > 0)
        {
            _context.ChatAdmins.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted {Count} admin records for chat {ChatId}", rowsAffected, chatId);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateLastVerifiedAsync(long chatId, long telegramId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var admin = await _context.ChatAdmins
            .FirstOrDefaultAsync(ca => ca.ChatId == chatId && ca.TelegramId == telegramId);

        if (admin != null)
        {
            admin.LastVerifiedAt = now;
            await _context.SaveChangesAsync();
        }
    }
}

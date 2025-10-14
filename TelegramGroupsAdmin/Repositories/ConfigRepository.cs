using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing configs table (unified configuration storage)
/// </summary>
public interface IConfigRepository
{
    /// <summary>
    /// Get config record for a specific chat (null = global)
    /// </summary>
    Task<ConfigRecordDto?> GetAsync(long? chatId);

    /// <summary>
    /// Upsert (insert or update) config record for a specific chat
    /// </summary>
    Task UpsertAsync(ConfigRecordDto config);

    /// <summary>
    /// Delete config record for a specific chat
    /// </summary>
    Task DeleteAsync(long? chatId);
}

public class ConfigRepository(AppDbContext context) : IConfigRepository
{
    private readonly AppDbContext _context = context;

    public async Task<ConfigRecordDto?> GetAsync(long? chatId)
    {
        return await _context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == chatId);
    }

    public async Task UpsertAsync(ConfigRecordDto config)
    {
        var existing = await _context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == config.ChatId);

        if (existing != null)
        {
            // Update existing record
            existing.SpamDetectionConfig = config.SpamDetectionConfig;
            existing.WelcomeConfig = config.WelcomeConfig;
            existing.LogConfig = config.LogConfig;
            existing.ModerationConfig = config.ModerationConfig;
            existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            // Insert new record
            config.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            config.UpdatedAt = null;
            await _context.Configs.AddAsync(config);
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(long? chatId)
    {
        var config = await _context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId);

        if (config != null)
        {
            _context.Configs.Remove(config);
            await _context.SaveChangesAsync();
        }
    }
}

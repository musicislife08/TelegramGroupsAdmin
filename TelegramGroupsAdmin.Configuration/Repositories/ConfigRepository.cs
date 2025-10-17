using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing configs table (unified configuration storage)
/// </summary>
public interface IConfigRepository
{
    /// <summary>
    /// Get config record for a specific chat (null = global)
    /// </summary>
    Task<ConfigRecordDto?> GetAsync(long? chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert (insert or update) config record for a specific chat
    /// </summary>
    Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete config record for a specific chat
    /// </summary>
    Task DeleteAsync(long? chatId, CancellationToken cancellationToken = default);
}

public class ConfigRepository(AppDbContext context) : IConfigRepository
{
    private readonly AppDbContext _context = context;

    public async Task<ConfigRecordDto?> GetAsync(long? chatId, CancellationToken cancellationToken = default)
    {
        return await _context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
    }

    public async Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == config.ChatId, cancellationToken);

        if (existing != null)
        {
            // Update existing record
            existing.SpamDetectionConfig = config.SpamDetectionConfig;
            existing.WelcomeConfig = config.WelcomeConfig;
            existing.LogConfig = config.LogConfig;
            existing.ModerationConfig = config.ModerationConfig;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Insert new record (CreatedAt will be set by database default)
            config.UpdatedAt = null;
            await _context.Configs.AddAsync(config, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long? chatId, CancellationToken cancellationToken = default)
    {
        var config = await _context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (config != null)
        {
            _context.Configs.Remove(config);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

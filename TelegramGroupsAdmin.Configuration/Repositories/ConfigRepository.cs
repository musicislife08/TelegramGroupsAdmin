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

    /// <summary>
    /// Get config record by chat ID (alias for GetAsync with non-null chatId)
    /// </summary>
    Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save/update invite link for a chat (upserts config row if needed)
    /// </summary>
    Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear cached invite link for a chat (set to null)
    /// Use when link is known to be invalid/revoked
    /// </summary>
    Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all cached invite links (for all chats)
    /// Use when admin regenerates all chat links or troubleshooting
    /// </summary>
    Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default);
}

public class ConfigRepository(AppDbContext context) : IConfigRepository
{
    public async Task<ConfigRecordDto?> GetAsync(long? chatId, CancellationToken cancellationToken = default)
    {
        return await context.Configs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default)
    {
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == config.ChatId, cancellationToken).ConfigureAwait(false);

        if (existing != null)
        {
            // Update existing record using EF Core's SetValues
            context.Entry(existing).CurrentValues.SetValues(config);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Insert new record (CreatedAt will be set by database default)
            config.UpdatedAt = null;
            await context.Configs.AddAsync(config, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long? chatId, CancellationToken cancellationToken = default)
    {
        var config = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken).ConfigureAwait(false);

        if (config != null)
        {
            context.Configs.Remove(config);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default)
    {
        return await GetAsync(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default)
    {
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken).ConfigureAwait(false);

        if (existing != null)
        {
            // Update existing config
            existing.InviteLink = inviteLink;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Create new config row just for invite link
            await context.Configs.AddAsync(new ConfigRecordDto
            {
                ChatId = chatId,
                InviteLink = inviteLink,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var existing = await context.Configs
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken).ConfigureAwait(false);

        if (existing != null)
        {
            existing.InviteLink = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default)
    {
        var configsWithLinks = await context.Configs
            .Where(c => c.InviteLink != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var config in configsWithLinks)
        {
            config.InviteLink = null;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (configsWithLinks.Any())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

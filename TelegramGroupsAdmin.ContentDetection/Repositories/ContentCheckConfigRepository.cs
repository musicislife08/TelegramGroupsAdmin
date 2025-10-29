using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository implementation for content check configurations (EF Core)
/// Manages per-chat configurations and "always-run" critical checks
/// Uses DbContextFactory to avoid concurrency issues
/// </summary>
public class ContentCheckConfigRepository : IContentCheckConfigRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ContentCheckConfigRepository> _logger;

    public ContentCheckConfigRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<ContentCheckConfigRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get all checks marked as always_run=true for a specific chat (or global if chat not found)
    /// Critical checks run for ALL users regardless of trust/admin status
    /// </summary>
    public async Task<IEnumerable<ContentCheckConfig>> GetCriticalChecksAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Get chat-specific critical checks, fall back to global (chatId=0) if none found
            var chatConfigs = await context.SpamCheckConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == chatId && c.AlwaysRun)
                .ToListAsync(cancellationToken);

            // If chat has no specific critical checks, use global configs
            if (!chatConfigs.Any())
            {
                chatConfigs = await context.SpamCheckConfigs
                    .AsNoTracking()
                    .Where(c => c.ChatId == 0 && c.AlwaysRun)
                    .ToListAsync(cancellationToken);
            }

            return chatConfigs.Select(c => c.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve critical checks for chat {ChatId}", chatId);
            return Enumerable.Empty<ContentCheckConfig>();
        }
    }

    /// <summary>
    /// Get configuration for a specific check in a chat
    /// Returns null if not found
    /// </summary>
    public async Task<ContentCheckConfig?> GetCheckConfigAsync(long chatId, string checkName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Try chat-specific config first
            var config = await context.SpamCheckConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ChatId == chatId && c.CheckName == checkName, cancellationToken);

            // Fall back to global config if no chat-specific config
            if (config == null)
            {
                config = await context.SpamCheckConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.ChatId == 0 && c.CheckName == checkName, cancellationToken);
            }

            return config?.ToModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get config for check {CheckName} in chat {ChatId}", checkName, chatId);
            return null;
        }
    }

    /// <summary>
    /// Get all check configurations for a chat (both chat-specific and global)
    /// Chat-specific configs override global configs
    /// </summary>
    public async Task<IEnumerable<ContentCheckConfig>> GetAllCheckConfigsAsync(long chatId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Get both global and chat-specific configs
            var allConfigs = await context.SpamCheckConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0 || c.ChatId == chatId)
                .OrderBy(c => c.CheckName)
                .ToListAsync(cancellationToken);

            // Group by check name and take chat-specific over global
            var effectiveConfigs = allConfigs
                .GroupBy(c => c.CheckName)
                .Select(g => g.FirstOrDefault(c => c.ChatId == chatId) ?? g.First())
                .Select(c => c.ToModel());

            return effectiveConfigs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all check configs for chat {ChatId}", chatId);
            return Enumerable.Empty<ContentCheckConfig>();
        }
    }

    /// <summary>
    /// Update or insert a check configuration
    /// </summary>
    public async Task<ContentCheckConfig> UpsertCheckConfigAsync(ContentCheckConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var existing = await context.SpamCheckConfigs
                .FirstOrDefaultAsync(c => c.ChatId == config.ChatId && c.CheckName == config.CheckName, cancellationToken);

            if (existing != null)
            {
                // Update existing
                existing.Enabled = config.Enabled;
                existing.AlwaysRun = config.AlwaysRun;
                existing.ConfidenceThreshold = config.ConfidenceThreshold;
                existing.ConfigurationJson = config.ConfigurationJson;
                existing.ModifiedDate = DateTimeOffset.UtcNow;
                existing.ModifiedBy = config.ModifiedBy;
            }
            else
            {
                // Insert new
                var dto = config.ToDto();
                dto.ModifiedDate = DateTimeOffset.UtcNow;
                context.SpamCheckConfigs.Add(dto);
                existing = dto;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Upserted config for check {CheckName} in chat {ChatId}", config.CheckName, config.ChatId);
            return existing.ToModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert config for check {CheckName} in chat {ChatId}", config.CheckName, config.ChatId);
            throw;
        }
    }

    /// <summary>
    /// Toggle the always_run flag for a specific check
    /// Only allows updating global config (chatId=0)
    /// </summary>
    public async Task<bool> SetAlwaysRunAsync(string checkName, bool alwaysRun, string modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var config = await context.SpamCheckConfigs
                .FirstOrDefaultAsync(c => c.ChatId == 0 && c.CheckName == checkName, cancellationToken);

            if (config == null)
            {
                // Create global config if it doesn't exist
                config = new Data.Models.SpamCheckConfigRecordDto
                {
                    ChatId = 0,
                    CheckName = checkName,
                    Enabled = true,
                    AlwaysRun = alwaysRun,
                    ModifiedDate = DateTimeOffset.UtcNow,
                    ModifiedBy = modifiedBy
                };
                context.SpamCheckConfigs.Add(config);
            }
            else
            {
                config.AlwaysRun = alwaysRun;
                config.ModifiedDate = DateTimeOffset.UtcNow;
                config.ModifiedBy = modifiedBy;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Set always_run={AlwaysRun} for check {CheckName}", alwaysRun, checkName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set always_run for check {CheckName}", checkName);
            throw;
        }
    }

    /// <summary>
    /// Get all global check configurations (chatId=0)
    /// Used by Settings UI to show which checks are critical
    /// </summary>
    public async Task<IEnumerable<ContentCheckConfig>> GetGlobalConfigsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var configs = await context.SpamCheckConfigs
                .AsNoTracking()
                .Where(c => c.ChatId == 0)
                .OrderBy(c => c.CheckName)
                .ToListAsync(cancellationToken);

            return configs.Select(c => c.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve global check configs");
            return Enumerable.Empty<ContentCheckConfig>();
        }
    }
}

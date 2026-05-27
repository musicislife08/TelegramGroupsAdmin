using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class TagDefinitionsRepository : ITagDefinitionsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<TagDefinitionsRepository> _logger;

    public TagDefinitionsRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<TagDefinitionsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<TagDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var definitions = await context.TagDefinitions
            .OrderByDescending(td => td.UsageCount)
            .ThenBy(td => td.TagName)
            .ToListAsync(cancellationToken);

        return definitions.Select(td => td.ToModel()).ToList();
    }

    public async Task<TagDefinition?> GetByNameAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var definition = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        return definition?.ToModel();
    }

    public async Task<TagDefinition> CreateAsync(string tagName, Models.TagColor color, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var dataColor = (int)(Data.Models.TagColor)color;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
            VALUES ({normalizedTag}, {dataColor}, {0}, {now})
            ON CONFLICT (tag_name) DO NOTHING
            """, cancellationToken);

        var definition = await context.TagDefinitions
            .AsNoTracking()
            .FirstAsync(td => td.TagName == normalizedTag, cancellationToken);

        return definition.ToModel();
    }

    public async Task<bool> UpdateColorAsync(string tagName, Models.TagColor color, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var definition = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            _logger.LogWarning("Tag definition not found for update: {TagName}", normalizedTag);
            return false;
        }

        definition.Color = (Data.Models.TagColor)color; // Cast from UI to Data layer
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated tag definition color: {TagName} to {Color}", normalizedTag, color);

        return true;
    }

    public async Task<bool> DeleteAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var definition = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            _logger.LogWarning("Tag definition not found for deletion: {TagName}", normalizedTag);
            return false;
        }

        // Warn if usage count > 0, but allow deletion (cascade will update usage count)
        if (definition.UsageCount > 0)
        {
            _logger.LogWarning("Deleting tag definition with usage count {Count}: {TagName}", definition.UsageCount, normalizedTag);
        }

        context.TagDefinitions.Remove(definition);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted tag definition: {TagName}", normalizedTag);

        return true;
    }

    public async Task IncrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var primaryColor = (int)Data.Models.TagColor.Primary;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
            VALUES ({normalizedTag}, {primaryColor}, {1}, {now})
            ON CONFLICT (tag_name) DO UPDATE SET usage_count = tag_definitions.usage_count + 1
            """, cancellationToken);
    }

    public async Task DecrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var definition = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            _logger.LogWarning("Tag definition not found for decrement: {TagName}", normalizedTag);
            return;
        }

        if (definition.UsageCount > 0)
        {
            definition.UsageCount--;
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning("Usage count already 0 for tag: {TagName}", normalizedTag);
        }
    }

    public async Task<bool> ExistsAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TagDefinitions
            .AnyAsync(td => td.TagName == normalizedTag, cancellationToken);
    }

    public async Task<List<string>> SearchTagNamesAsync(string searchTerm, int limit = 50, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = searchTerm.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tagNames = await context.TagDefinitions
            .Where(td => td.TagName.Contains(normalizedSearch))
            .OrderByDescending(td => td.UsageCount)
            .ThenBy(td => td.TagName)
            .Take(limit)
            .Select(td => td.TagName)
            .ToListAsync(cancellationToken);

        return tagNames;
    }
}

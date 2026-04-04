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

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Check if already exists
        var existing = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (existing != null)
        {
            _logger.LogWarning("Tag definition already exists: {TagName}", normalizedTag);
            return existing.ToModel();
        }

        var definition = new TagDefinitionDto
        {
            TagName = normalizedTag,
            Color = (Data.Models.TagColor)color, // Cast from UI to Data layer
            UsageCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.TagDefinitions.Add(definition);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created tag definition: {TagName} with color {Color}", normalizedTag, color);

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

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Find or create tag definition
        var definition = await context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            // Auto-create with default color (Primary/Blue)
            definition = new TagDefinitionDto
            {
                TagName = normalizedTag,
                Color = Data.Models.TagColor.Primary, // Use Data layer enum
                UsageCount = 1,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.TagDefinitions.Add(definition);

            _logger.LogInformation("Auto-created tag definition: {TagName}", normalizedTag);
        }
        else
        {
            definition.UsageCount++;
        }

        await context.SaveChangesAsync(cancellationToken);
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

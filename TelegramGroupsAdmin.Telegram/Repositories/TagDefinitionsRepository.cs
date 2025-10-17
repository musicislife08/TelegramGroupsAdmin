using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class TagDefinitionsRepository : ITagDefinitionsRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<TagDefinitionsRepository> _logger;

    public TagDefinitionsRepository(AppDbContext context, ILogger<TagDefinitionsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<TagDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _context.TagDefinitions
            .OrderByDescending(td => td.UsageCount)
            .ThenBy(td => td.TagName)
            .ToListAsync(cancellationToken);

        return definitions.Select(td => td.ToModel()).ToList();
    }

    public async Task<TagDefinition?> GetByNameAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        var definition = await _context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        return definition?.ToModel();
    }

    public async Task<TagDefinition> CreateAsync(string tagName, TagColor color, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.Trim().ToLowerInvariant();

        // Check if already exists
        var existing = await _context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (existing != null)
        {
            _logger.LogWarning("Tag definition already exists: {TagName}", normalizedTag);
            return existing.ToModel();
        }

        var definition = new TagDefinitionDto
        {
            TagName = normalizedTag,
            Color = color,
            UsageCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.TagDefinitions.Add(definition);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created tag definition: {TagName} with color {Color}", normalizedTag, color);

        return definition.ToModel();
    }

    public async Task<bool> UpdateColorAsync(string tagName, TagColor color, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        var definition = await _context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            _logger.LogWarning("Tag definition not found for update: {TagName}", normalizedTag);
            return false;
        }

        definition.Color = color;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated tag definition color: {TagName} to {Color}", normalizedTag, color);

        return true;
    }

    public async Task<bool> DeleteAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        var definition = await _context.TagDefinitions
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

        _context.TagDefinitions.Remove(definition);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted tag definition: {TagName}", normalizedTag);

        return true;
    }

    public async Task IncrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        // Find or create tag definition
        var definition = await _context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            // Auto-create with default color (Primary/Blue)
            definition = new TagDefinitionDto
            {
                TagName = normalizedTag,
                Color = TagColor.Primary,
                UsageCount = 1,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _context.TagDefinitions.Add(definition);

            _logger.LogInformation("Auto-created tag definition: {TagName}", normalizedTag);
        }
        else
        {
            definition.UsageCount++;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DecrementUsageAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        var definition = await _context.TagDefinitions
            .FirstOrDefaultAsync(td => td.TagName == normalizedTag, cancellationToken);

        if (definition == null)
        {
            _logger.LogWarning("Tag definition not found for decrement: {TagName}", normalizedTag);
            return;
        }

        if (definition.UsageCount > 0)
        {
            definition.UsageCount--;
            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning("Usage count already 0 for tag: {TagName}", normalizedTag);
        }
    }

    public async Task<bool> ExistsAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        return await _context.TagDefinitions
            .AnyAsync(td => td.TagName == normalizedTag, cancellationToken);
    }

    public async Task<List<string>> SearchTagNamesAsync(string searchTerm, int limit = 50, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = searchTerm.ToLowerInvariant();

        var tagNames = await _context.TagDefinitions
            .Where(td => td.TagName.Contains(normalizedSearch))
            .OrderByDescending(td => td.UsageCount)
            .ThenBy(td => td.TagName)
            .Take(limit)
            .Select(td => td.TagName)
            .ToListAsync(cancellationToken);

        return tagNames;
    }
}

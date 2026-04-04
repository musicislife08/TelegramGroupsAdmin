using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing user tags
/// </summary>
public class UserTagsRepository : IUserTagsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ITagDefinitionsRepository _tagDefinitionsRepository;

    public UserTagsRepository(IDbContextFactory<AppDbContext> contextFactory, ITagDefinitionsRepository tagDefinitionsRepository)
    {
        _contextFactory = contextFactory;
        _tagDefinitionsRepository = tagDefinitionsRepository;
    }

    public async Task<List<UserTag>> GetTagsByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tags = await context.UserTags
            .Where(t => t.TelegramUserId == telegramUserId && t.RemovedAt == null)
            .OrderBy(t => t.TagName)
            .ToListAsync(cancellationToken);

        return tags.Select(t => t.ToModel()).ToList();
    }

    public async Task<UserTag?> GetTagByIdAsync(long tagId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tag = await context.UserTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        return tag?.ToModel();
    }

    public async Task<long> AddTagAsync(UserTag tag, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var dto = tag.ToDto();
        dto.AddedAt = DateTimeOffset.UtcNow;

        context.UserTags.Add(dto);
        await context.SaveChangesAsync(cancellationToken);

        // Increment usage count for tag definition
        await _tagDefinitionsRepository.IncrementUsageAsync(tag.TagName, cancellationToken);

        return dto.Id;
    }

    public async Task<bool> DeleteTagAsync(long tagId, Actor deletedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var tag = await context.UserTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag == null)
            return false;

        var tagName = tag.TagName;

        // Hard delete for now (can switch to soft delete later if needed)
        context.UserTags.Remove(tag);
        await context.SaveChangesAsync(cancellationToken);

        // Decrement usage count for tag definition
        await _tagDefinitionsRepository.DecrementUsageAsync(tagName, cancellationToken);

        return true;
    }

    public async Task<List<long>> GetUserIdsByTagNameAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserTags
            .Where(t => t.TagName == normalizedTag && t.RemovedAt == null)
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasTagAsync(long telegramUserId, string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserTags
            .AnyAsync(t => t.TelegramUserId == telegramUserId && t.TagName == normalizedTag && t.RemovedAt == null, cancellationToken);
    }
}

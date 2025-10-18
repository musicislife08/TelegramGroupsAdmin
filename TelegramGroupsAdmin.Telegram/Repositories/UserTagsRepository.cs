using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing user tags
/// </summary>
public class UserTagsRepository : IUserTagsRepository
{
    private readonly AppDbContext _context;
    private readonly ITagDefinitionsRepository _tagDefinitionsRepository;

    public UserTagsRepository(AppDbContext context, ITagDefinitionsRepository tagDefinitionsRepository)
    {
        _context = context;
        _tagDefinitionsRepository = tagDefinitionsRepository;
    }

    public async Task<List<UserTag>> GetTagsByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var tags = await _context.UserTags
            .Where(t => t.TelegramUserId == telegramUserId && t.RemovedAt == null)
            .OrderBy(t => t.TagName)
            .ToListAsync(cancellationToken);

        return tags.Select(t => t.ToModel()).ToList();
    }

    public async Task<UserTag?> GetTagByIdAsync(long tagId, CancellationToken cancellationToken = default)
    {
        var tag = await _context.UserTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        return tag?.ToModel();
    }

    public async Task<long> AddTagAsync(UserTag tag, CancellationToken cancellationToken = default)
    {
        var dto = tag.ToDto();
        dto.AddedAt = DateTimeOffset.UtcNow;

        _context.UserTags.Add(dto);
        await _context.SaveChangesAsync(cancellationToken);

        // Increment usage count for tag definition
        await _tagDefinitionsRepository.IncrementUsageAsync(tag.TagName, cancellationToken);

        return dto.Id;
    }

    public async Task<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        var tag = await _context.UserTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag == null)
            return false;

        var tagName = tag.TagName;

        // Hard delete for now (can switch to soft delete later if needed)
        _context.UserTags.Remove(tag);
        await _context.SaveChangesAsync(cancellationToken);

        // Decrement usage count for tag definition
        await _tagDefinitionsRepository.DecrementUsageAsync(tagName, cancellationToken);

        return true;
    }

    public async Task<List<long>> GetUserIdsByTagNameAsync(string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        return await _context.UserTags
            .Where(t => t.TagName == normalizedTag && t.RemovedAt == null)
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasTagAsync(long telegramUserId, string tagName, CancellationToken cancellationToken = default)
    {
        var normalizedTag = tagName.ToLowerInvariant();

        return await _context.UserTags
            .AnyAsync(t => t.TelegramUserId == telegramUserId && t.TagName == normalizedTag && t.RemovedAt == null, cancellationToken);
    }
}

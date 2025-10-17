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

    public UserTagsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<UserTag>> GetTagsByUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var tags = await _context.UserTags
            .Where(t => t.TelegramUserId == telegramUserId)
            .OrderBy(t => t.TagType)
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

        return dto.Id;
    }

    public async Task<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        var tag = await _context.UserTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);

        if (tag == null)
            return false;

        _context.UserTags.Remove(tag);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<long>> GetUserIdsByTagTypeAsync(TagType tagType, CancellationToken cancellationToken = default)
    {
        var dataTagType = (Data.Models.TagType)(int)tagType;

        return await _context.UserTags
            .Where(t => t.TagType == dataTagType)
            .Select(t => t.TelegramUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasTagAsync(long telegramUserId, TagType tagType, CancellationToken cancellationToken = default)
    {
        var dataTagType = (Data.Models.TagType)(int)tagType;

        return await _context.UserTags
            .AnyAsync(t => t.TelegramUserId == telegramUserId && t.TagType == dataTagType, cancellationToken);
    }

    public async Task<int> GetTotalConfidenceModifierAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var modifiers = await _context.UserTags
            .Where(t => t.TelegramUserId == telegramUserId && t.ConfidenceModifier.HasValue)
            .Select(t => t.ConfidenceModifier!.Value)
            .ToListAsync(cancellationToken);

        return modifiers.Sum();
    }
}

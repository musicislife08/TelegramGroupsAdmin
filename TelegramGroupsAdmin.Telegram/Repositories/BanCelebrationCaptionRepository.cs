using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ban celebration captions
/// </summary>
public class BanCelebrationCaptionRepository : IBanCelebrationCaptionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<BanCelebrationCaptionRepository> _logger;

    public BanCelebrationCaptionRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<BanCelebrationCaptionRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<BanCelebrationCaption>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dtos = await context.BanCelebrationCaptions
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return dtos.Select(d => d.ToModel()).ToList();
    }

    public async Task<List<int>> GetAllIdsAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationCaptions
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<BanCelebrationCaption?> GetRandomAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Use SQL RANDOM() for efficient random selection
        var dto = await context.BanCelebrationCaptions
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);

        return dto?.ToModel();
    }

    public async Task<BanCelebrationCaption?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationCaptions.FindAsync([id], ct);
        return dto?.ToModel();
    }

    public async Task<BanCelebrationCaption> AddAsync(string text, string dmText, string? name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(dmText);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var dto = new BanCelebrationCaptionDto
        {
            Text = text,
            DmText = dmText,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.BanCelebrationCaptions.Add(dto);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Added ban celebration caption: {Id} ({Name})", dto.Id, name ?? "unnamed");

        return dto.ToModel();
    }

    public async Task<BanCelebrationCaption> UpdateAsync(int id, string text, string dmText, string? name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(dmText);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var dto = await context.BanCelebrationCaptions.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Caption with ID {id} not found");

        dto.Text = text;
        dto.DmText = dmText;
        dto.Name = name;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated ban celebration caption: {Id} ({Name})", dto.Id, name ?? "unnamed");

        return dto.ToModel();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var dto = await context.BanCelebrationCaptions.FindAsync([id], ct);

        if (dto == null)
        {
            _logger.LogWarning("Attempted to delete non-existent ban celebration caption: {Id}", id);
            return;
        }

        context.BanCelebrationCaptions.Remove(dto);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted ban celebration caption: {Id}", id);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BanCelebrationCaptions.CountAsync(ct);
    }

    public async Task SeedDefaultsIfEmptyAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        if (await context.BanCelebrationCaptions.AnyAsync(ct))
        {
            _logger.LogDebug("Ban celebration captions table already has data, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding {Count} default ban celebration captions", BanCelebrationDefaults.Captions.Count);

        var dtos = BanCelebrationDefaults.Captions
            .Select(c => new BanCelebrationCaptionDto
            {
                Name = c.Name,
                Text = c.ChatText,
                DmText = c.DmText,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        context.BanCelebrationCaptions.AddRange(dtos);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Successfully seeded {Count} default ban celebration captions", dtos.Count);
    }
}

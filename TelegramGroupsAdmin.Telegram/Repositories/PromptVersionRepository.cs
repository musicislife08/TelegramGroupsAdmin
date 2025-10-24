using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing OpenAI custom prompt versions with rollback capability
/// Phase 4.X: AI-powered prompt builder
/// </summary>
public class PromptVersionRepository : IPromptVersionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public PromptVersionRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<PromptVersion>> GetVersionHistoryByChatIdAsync(
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var versions = await context.PromptVersions
            .Where(pv => pv.ChatId == chatId)
            .OrderByDescending(pv => pv.Version)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return versions.Select(v => v.ToModel()).ToList();
    }

    public async Task<PromptVersion?> GetActiveVersionAsync(
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activeVersion = await context.PromptVersions
            .Where(pv => pv.ChatId == chatId && pv.IsActive)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return activeVersion?.ToModel();
    }

    public async Task<PromptVersion?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var version = await context.PromptVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(pv => pv.Id == id, cancellationToken);

        return version?.ToModel();
    }

    public async Task<PromptVersion> CreateVersionAsync(
        long chatId,
        string promptText,
        string? createdBy,
        string? generationMetadata,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use execution strategy to handle retry logic with transaction
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Deactivate current active version (if any)
                var currentActive = await context.PromptVersions
                    .FirstOrDefaultAsync(pv => pv.ChatId == chatId && pv.IsActive, cancellationToken);

                if (currentActive != null)
                {
                    currentActive.IsActive = false;
                }

                // Get next version number
                var maxVersion = await context.PromptVersions
                    .Where(pv => pv.ChatId == chatId)
                    .MaxAsync(pv => (int?)pv.Version, cancellationToken) ?? 0;

                var newVersion = new Data.Models.PromptVersionDto
                {
                    ChatId = chatId,
                    Version = maxVersion + 1,
                    PromptText = promptText,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = createdBy,
                    GenerationMetadata = generationMetadata
                };

                context.PromptVersions.Add(newVersion);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return newVersion.ToModel();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<PromptVersion> RestoreVersionAsync(
        long versionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use execution strategy to handle retry logic with transaction
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Get the version to restore
                var versionToRestore = await context.PromptVersions
                    .FirstOrDefaultAsync(pv => pv.Id == versionId, cancellationToken);

                if (versionToRestore == null)
                {
                    throw new InvalidOperationException($"Prompt version {versionId} not found");
                }

                // Deactivate current active version
                var currentActive = await context.PromptVersions
                    .FirstOrDefaultAsync(pv => pv.ChatId == versionToRestore.ChatId && pv.IsActive, cancellationToken);

                if (currentActive != null)
                {
                    currentActive.IsActive = false;
                }

                // Activate the selected version
                versionToRestore.IsActive = true;

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return versionToRestore.ToModel();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<bool> DeleteVersionAsync(
        long versionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var version = await context.PromptVersions
            .FirstOrDefaultAsync(pv => pv.Id == versionId, cancellationToken);

        if (version == null)
        {
            return false;
        }

        // Cannot delete active version
        if (version.IsActive)
        {
            throw new InvalidOperationException("Cannot delete the currently active prompt version. Restore a different version first.");
        }

        context.PromptVersions.Remove(version);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

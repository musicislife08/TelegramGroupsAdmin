using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using DataModels = TelegramGroupsAdmin.Data.Models;

using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing message translations
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageTranslationService : IMessageTranslationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly SimHashService _simHashService;

    public MessageTranslationService(
        IDbContextFactory<AppDbContext> contextFactory,
        SimHashService simHashService)
    {
        _contextFactory = contextFactory;
        _simHashService = simHashService;
    }

    public async Task<MessageTranslation?> GetTranslationForMessageAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var translation = await context.MessageTranslations
            .Where(t => t.MessageId == messageId)
            .OrderByDescending(t => t.TranslatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return translation?.ToModel();
    }

    public async Task<MessageTranslation?> GetTranslationForEditAsync(long editId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var translation = await context.MessageTranslations
            .Where(t => t.EditId == editId)
            .OrderByDescending(t => t.TranslatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return translation?.ToModel();
    }

    public async Task InsertTranslationAsync(MessageTranslation translation, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Compute SimHash for the translated text (used for near-duplicate detection)
        var hash = _simHashService.ComputeHash(translation.TranslatedText);

        // Check if translation already exists (unique constraint on message_id or edit_id)
        DataModels.MessageTranslationDto? existing = null;
        if (translation.MessageId.HasValue)
        {
            existing = await context.MessageTranslations
                .FirstOrDefaultAsync(mt => mt.MessageId == translation.MessageId, cancellationToken);
        }
        else if (translation.EditId.HasValue)
        {
            existing = await context.MessageTranslations
                .FirstOrDefaultAsync(mt => mt.EditId == translation.EditId, cancellationToken);
        }

        if (existing != null)
        {
            // Update existing translation
            existing.TranslatedText = translation.TranslatedText;
            existing.DetectedLanguage = translation.DetectedLanguage;
            existing.Confidence = translation.Confidence;
            existing.TranslatedAt = translation.TranslatedAt;
            existing.SimilarityHash = hash;
        }
        else
        {
            // Insert new translation
            var dto = translation.ToDto();
            dto.SimilarityHash = hash;
            context.MessageTranslations.Add(dto);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

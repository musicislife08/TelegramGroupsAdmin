using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing message translations
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public class MessageTranslationService : IMessageTranslationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MessageTranslationService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UiModels.MessageTranslation?> GetTranslationForMessageAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var translation = await context.MessageTranslations
            .Where(t => t.MessageId == messageId)
            .OrderByDescending(t => t.TranslatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return translation?.ToModel();
    }

    public async Task<UiModels.MessageTranslation?> GetTranslationForEditAsync(long editId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var translation = await context.MessageTranslations
            .Where(t => t.EditId == editId)
            .OrderByDescending(t => t.TranslatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return translation?.ToModel();
    }

    public async Task InsertTranslationAsync(UiModels.MessageTranslation translation, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

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
        }
        else
        {
            // Insert new translation
            var dto = translation.ToDto();
            context.MessageTranslations.Add(dto);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

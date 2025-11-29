using TelegramGroupsAdmin.Core.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing message translations
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public interface IMessageTranslationService
{
    /// <summary>
    /// Get translation for a message
    /// </summary>
    Task<MessageTranslation?> GetTranslationForMessageAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get translation for a message edit
    /// </summary>
    Task<MessageTranslation?> GetTranslationForEditAsync(long editId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update a translation
    /// </summary>
    Task InsertTranslationAsync(MessageTranslation translation, CancellationToken cancellationToken = default);
}

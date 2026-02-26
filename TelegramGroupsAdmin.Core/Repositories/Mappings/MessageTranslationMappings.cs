using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Message Translation records (Phase 4.20)
/// </summary>
public static class MessageTranslationMappings
{
    extension(DataModels.MessageTranslationDto data)
    {
        public UiModels.MessageTranslation ToModel() => new(
            Id: data.Id,
            MessageId: data.MessageId,
            ChatId: data.ChatId,
            EditId: data.EditId,
            TranslatedText: data.TranslatedText,
            DetectedLanguage: data.DetectedLanguage,
            Confidence: data.Confidence,
            TranslatedAt: data.TranslatedAt
        );
    }

    extension(UiModels.MessageTranslation ui)
    {
        public DataModels.MessageTranslationDto ToDto() => new()
        {
            Id = ui.Id,
            MessageId = ui.MessageId,
            ChatId = ui.ChatId,
            EditId = ui.EditId,
            TranslatedText = ui.TranslatedText,
            DetectedLanguage = ui.DetectedLanguage,
            Confidence = ui.Confidence,
            TranslatedAt = ui.TranslatedAt
        };
    }
}

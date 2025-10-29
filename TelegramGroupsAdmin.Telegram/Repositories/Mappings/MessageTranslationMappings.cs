using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Message Translation records (Phase 4.20)
/// </summary>
public static class MessageTranslationMappings
{
    public static UiModels.MessageTranslation ToModel(this DataModels.MessageTranslationDto data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        EditId: data.EditId,
        TranslatedText: data.TranslatedText,
        DetectedLanguage: data.DetectedLanguage,
        Confidence: data.Confidence,
        TranslatedAt: data.TranslatedAt
    );

    public static DataModels.MessageTranslationDto ToDto(this UiModels.MessageTranslation ui) => new()
    {
        Id = ui.Id,
        MessageId = ui.MessageId,
        EditId = ui.EditId,
        TranslatedText = ui.TranslatedText,
        DetectedLanguage = ui.DetectedLanguage,
        Confidence = ui.Confidence,
        TranslatedAt = ui.TranslatedAt
    };
}

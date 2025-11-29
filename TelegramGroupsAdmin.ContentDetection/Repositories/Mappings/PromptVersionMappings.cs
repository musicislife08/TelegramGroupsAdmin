using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Prompt Version records (Phase 4.X: AI-powered prompt builder)
/// </summary>
public static class PromptVersionMappings
{
    extension(DataModels.PromptVersionDto data)
    {
        public UiModels.PromptVersion ToModel() => new()
        {
            Id = data.Id,
            ChatId = data.ChatId,
            Version = data.Version,
            PromptText = data.PromptText,
            IsActive = data.IsActive,
            CreatedAt = data.CreatedAt,
            CreatedBy = data.CreatedBy,
            GenerationMetadata = data.GenerationMetadata
        };
    }

    extension(UiModels.PromptVersion ui)
    {
        public DataModels.PromptVersionDto ToDto() => new()
        {
            Id = ui.Id,
            ChatId = ui.ChatId,
            Version = ui.Version,
            PromptText = ui.PromptText,
            IsActive = ui.IsActive,
            CreatedAt = ui.CreatedAt,
            CreatedBy = ui.CreatedBy,
            GenerationMetadata = ui.GenerationMetadata
        };
    }
}

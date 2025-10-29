using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Prompt Version records (Phase 4.X: AI-powered prompt builder)
/// </summary>
public static class PromptVersionMappings
{
    public static UiModels.PromptVersion ToModel(this DataModels.PromptVersionDto data) => new()
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

    public static DataModels.PromptVersionDto ToDto(this UiModels.PromptVersion ui) => new()
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

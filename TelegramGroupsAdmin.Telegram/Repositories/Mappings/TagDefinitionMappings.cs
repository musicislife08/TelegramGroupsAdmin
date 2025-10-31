using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Tag Definition records (Phase 4.12: Tag color preferences)
/// </summary>
public static class TagDefinitionMappings
{
    public static UiModels.TagDefinition ToModel(this DataModels.TagDefinitionDto data) => new()
    {
        TagName = data.TagName,
        Color = (UiModels.TagColor)data.Color, // Cast enum from Data to UI layer
        UsageCount = data.UsageCount,
        CreatedAt = data.CreatedAt
    };

    public static DataModels.TagDefinitionDto ToDto(this UiModels.TagDefinition ui) => new()
    {
        TagName = ui.TagName,
        Color = (DataModels.TagColor)ui.Color, // Cast enum from UI to Data layer
        UsageCount = ui.UsageCount,
        CreatedAt = ui.CreatedAt
    };
}

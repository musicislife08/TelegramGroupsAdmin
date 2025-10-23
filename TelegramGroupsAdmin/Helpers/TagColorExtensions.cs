using MudBlazor;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Helpers;

/// <summary>
/// Extension methods for TagColor enum to convert to MudBlazor Color
/// Centralizes the mapping logic for consistent color usage across the UI
/// </summary>
public static class TagColorExtensions
{
    /// <summary>
    /// Convert TagColor (Data layer) to MudBlazor Color (UI enum)
    /// </summary>
    public static Color ToMudColor(this DataModels.TagColor tagColor) => tagColor switch
    {
        DataModels.TagColor.Primary => Color.Primary,
        DataModels.TagColor.Secondary => Color.Secondary,
        DataModels.TagColor.Info => Color.Info,
        DataModels.TagColor.Success => Color.Success,
        DataModels.TagColor.Warning => Color.Warning,
        DataModels.TagColor.Error => Color.Error,
        DataModels.TagColor.Dark => Color.Dark,
        _ => Color.Primary
    };

    /// <summary>
    /// Convert TagColor (UI/Telegram layer) to MudBlazor Color (UI enum)
    /// </summary>
    public static Color ToMudColor(this UiModels.TagColor tagColor) => tagColor switch
    {
        UiModels.TagColor.Primary => Color.Primary,
        UiModels.TagColor.Secondary => Color.Secondary,
        UiModels.TagColor.Info => Color.Info,
        UiModels.TagColor.Success => Color.Success,
        UiModels.TagColor.Warning => Color.Warning,
        UiModels.TagColor.Error => Color.Error,
        UiModels.TagColor.Dark => Color.Dark,
        _ => Color.Primary
    };
}

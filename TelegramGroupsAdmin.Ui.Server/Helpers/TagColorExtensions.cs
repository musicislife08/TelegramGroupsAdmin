using MudBlazor;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Server.Helpers;

/// <summary>
/// Extension methods for TagColor enum to convert to MudBlazor Color
/// Centralizes the mapping logic for consistent color usage across the UI
/// </summary>
public static class TagColorExtensions
{
    extension(DataModels.TagColor tagColor)
    {
        /// <summary>
        /// Convert TagColor (Data layer) to MudBlazor Color (UI enum)
        /// </summary>
        public Color ToMudColor() => tagColor switch
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
    }

    extension(UiModels.TagColor tagColor)
    {
        /// <summary>
        /// Convert TagColor (UI/Telegram layer) to MudBlazor Color (UI enum)
        /// </summary>
        public Color ToMudColor() => tagColor switch
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
}

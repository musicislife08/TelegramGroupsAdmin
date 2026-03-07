using TelegramGroupsAdmin.Configuration.Models.ContentDetection;

namespace TelegramGroupsAdmin.Components.Shared.ContentDetection;

/// <summary>
/// Contract for detection config components rendered by ConfigDialogWrapper.
/// Eliminates reflection-based GetConfig() invocation.
/// </summary>
public interface IDetectionConfigComponent
{
    ContentDetectionConfig GetConfig();
}

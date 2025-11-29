using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Enum conversion helpers for ContentCheckSkipReason
/// </summary>
public static class ContentCheckSkipReasonMappings
{
    extension(UiModels.ContentCheckSkipReason reason)
    {
        /// <summary>
        /// Convert ContentCheckSkipReason from Telegram layer to Data layer
        /// </summary>
        public DataModels.ContentCheckSkipReason ToDataModel()
            => (DataModels.ContentCheckSkipReason)(int)reason;
    }

    extension(DataModels.ContentCheckSkipReason reason)
    {
        /// <summary>
        /// Convert ContentCheckSkipReason from Data layer to Telegram layer
        /// </summary>
        public UiModels.ContentCheckSkipReason ToTelegramModel()
            => (UiModels.ContentCheckSkipReason)(int)reason;
    }
}

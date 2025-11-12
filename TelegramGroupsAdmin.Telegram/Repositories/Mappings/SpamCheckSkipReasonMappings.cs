using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Enum conversion helpers for SpamCheckSkipReason
/// </summary>
public static class SpamCheckSkipReasonMappings
{
    extension(UiModels.SpamCheckSkipReason reason)
    {
        /// <summary>
        /// Convert SpamCheckSkipReason from Telegram layer to Data layer
        /// </summary>
        public DataModels.SpamCheckSkipReason ToDataModel()
            => (DataModels.SpamCheckSkipReason)(int)reason;
    }

    extension(DataModels.SpamCheckSkipReason reason)
    {
        /// <summary>
        /// Convert SpamCheckSkipReason from Data layer to Telegram layer
        /// </summary>
        public UiModels.SpamCheckSkipReason ToTelegramModel()
            => (UiModels.SpamCheckSkipReason)(int)reason;
    }
}

using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Enum conversion helpers for SpamCheckSkipReason
/// </summary>
public static class SpamCheckSkipReasonMappings
{
    /// <summary>
    /// Convert SpamCheckSkipReason from Telegram layer to Data layer
    /// </summary>
    public static DataModels.SpamCheckSkipReason ToDataModel(this UiModels.SpamCheckSkipReason reason)
        => (DataModels.SpamCheckSkipReason)(int)reason;

    /// <summary>
    /// Convert SpamCheckSkipReason from Data layer to Telegram layer
    /// </summary>
    public static UiModels.SpamCheckSkipReason ToTelegramModel(this DataModels.SpamCheckSkipReason reason)
        => (UiModels.SpamCheckSkipReason)(int)reason;
}

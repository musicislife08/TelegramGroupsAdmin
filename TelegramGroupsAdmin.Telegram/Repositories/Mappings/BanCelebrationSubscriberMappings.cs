using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for DM ban celebration subscriber records.
/// </summary>
public static class BanCelebrationSubscriberMappings
{
    extension(DataModels.BanCelebrationSubscriberDto data)
    {
        public UiModels.BanCelebrationSubscriber ToModel() => new(
            data.TelegramUserId,
            data.ChatId,
            data.SubscribedAt,
            data.PromptMessageId,
            data.PromptDeleteJobId);
    }
}

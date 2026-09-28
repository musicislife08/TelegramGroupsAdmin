using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

public static class MessageVerdictMappings
{
    extension(DataModels.MessageVerdictView view)
    {
        public MessageVerdict ToModel() => new(
            view.ChatId,
            view.MessageId,
            (VerdictClassification)view.Classification,
            view.IsSpam,
            view.Source is { } source ? (VerdictSource)source : null,
            view.DetectedAt,
            view.VerdictId);
    }
}

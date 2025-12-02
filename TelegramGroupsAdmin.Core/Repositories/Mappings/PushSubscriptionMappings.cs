using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for PushSubscription
/// Converts between DTO and domain model
/// </summary>
public static class PushSubscriptionMappings
{
    extension(DataModels.PushSubscriptionDto data)
    {
        public PushSubscription ToModel() => new()
        {
            Id = data.Id,
            UserId = data.UserId,
            Endpoint = data.Endpoint,
            P256dh = data.P256dh,
            Auth = data.Auth,
            UserAgent = data.UserAgent,
            CreatedAt = data.CreatedAt
        };
    }

    extension(PushSubscription model)
    {
        public DataModels.PushSubscriptionDto ToDto() => new()
        {
            Id = model.Id,
            UserId = model.UserId,
            Endpoint = model.Endpoint,
            P256dh = model.P256dh,
            Auth = model.Auth,
            UserAgent = model.UserAgent,
            CreatedAt = model.CreatedAt
        };
    }
}

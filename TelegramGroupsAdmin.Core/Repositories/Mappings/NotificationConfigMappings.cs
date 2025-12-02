using System.Text.Json;
using System.Text.Json.Serialization;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Notification Preferences
/// Converts between raw JSONB (ints) and domain models (enums)
/// </summary>
public static class NotificationConfigMappings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Internal class for JSON deserialization - uses raw ints instead of enums
    /// This keeps Data layer independent of Core models
    /// </summary>
    private sealed class RawConfig
    {
        public List<RawChannelPreference> Channels { get; set; } = [];
    }

    private sealed class RawChannelPreference
    {
        public int Channel { get; set; }
        public List<int> EnabledEvents { get; set; } = [];
        public int DigestMinutes { get; set; }
    }

    extension(DataModels.NotificationPreferencesDto data)
    {
        /// <summary>
        /// Convert DTO to domain model (raw ints → enums)
        /// </summary>
        public NotificationConfig ToModel()
        {
            if (string.IsNullOrWhiteSpace(data.Config))
            {
                return new NotificationConfig();
            }

            var raw = JsonSerializer.Deserialize<RawConfig>(data.Config, JsonOptions);
            if (raw == null)
            {
                return new NotificationConfig();
            }

            return new NotificationConfig
            {
                Channels = raw.Channels.Select(c => new ChannelPreference
                {
                    Channel = (NotificationChannel)c.Channel,
                    EnabledEvents = c.EnabledEvents.Select(e => (NotificationEventType)e).ToList(),
                    DigestMinutes = c.DigestMinutes
                }).ToList()
            };
        }
    }

    extension(NotificationConfig model)
    {
        /// <summary>
        /// Convert domain model to JSON string for storage (enums → raw ints)
        /// </summary>
        public string ToConfigJson()
        {
            var raw = new RawConfig
            {
                Channels = model.Channels.Select(c => new RawChannelPreference
                {
                    Channel = (int)c.Channel,
                    EnabledEvents = c.EnabledEvents.Select(e => (int)e).ToList(),
                    DigestMinutes = c.DigestMinutes
                }).ToList()
            };

            return JsonSerializer.Serialize(raw, JsonOptions);
        }
    }
}

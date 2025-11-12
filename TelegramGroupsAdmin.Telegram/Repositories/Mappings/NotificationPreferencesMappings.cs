using System.Text.Json;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Notification Preferences records (Phase 5.1: Notification system)
/// </summary>
public static class NotificationPreferencesMappings
{
    extension(DataModels.NotificationPreferencesDto data)
    {
        public UiModels.NotificationPreferences ToModel()
        {
            // Deserialize JSONB columns
            var channelConfigs = string.IsNullOrWhiteSpace(data.ChannelConfigs)
                ? new UiModels.NotificationChannelConfigs()
                : JsonSerializer.Deserialize<UiModels.NotificationChannelConfigs>(data.ChannelConfigs)
                  ?? new UiModels.NotificationChannelConfigs();

            var eventFilters = string.IsNullOrWhiteSpace(data.EventFilters)
                ? new UiModels.NotificationEventFilters()
                : JsonSerializer.Deserialize<UiModels.NotificationEventFilters>(data.EventFilters)
                  ?? new UiModels.NotificationEventFilters();

            return new UiModels.NotificationPreferences
            {
                Id = data.Id,
                UserId = data.UserId,
                TelegramDmEnabled = data.TelegramDmEnabled,
                EmailEnabled = data.EmailEnabled,
                ChannelConfigs = channelConfigs,
                EventFilters = eventFilters,
                CreatedAt = data.CreatedAt,
                UpdatedAt = data.UpdatedAt
            };
        }
    }

    extension(UiModels.NotificationPreferences ui)
    {
        public DataModels.NotificationPreferencesDto ToDto() => new()
        {
            Id = ui.Id,
            UserId = ui.UserId,
            TelegramDmEnabled = ui.TelegramDmEnabled,
            EmailEnabled = ui.EmailEnabled,
            ChannelConfigs = JsonSerializer.Serialize(ui.ChannelConfigs),
            EventFilters = JsonSerializer.Serialize(ui.EventFilters),
            // Note: ProtectedSecrets is not included in UI model - managed separately by repository
            CreatedAt = ui.CreatedAt,
            UpdatedAt = ui.UpdatedAt
        };
    }
}

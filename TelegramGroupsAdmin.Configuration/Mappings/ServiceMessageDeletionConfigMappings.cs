using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class ServiceMessageDeletionConfigMappings
{
    extension(ServiceMessageDeletionConfigData data)
    {
        public ServiceMessageDeletionConfig ToModel() => new()
        {
            DeleteJoinMessages = data.DeleteJoinMessages,
            DeleteLeaveMessages = data.DeleteLeaveMessages,
            DeletePhotoChanges = data.DeletePhotoChanges,
            DeleteTitleChanges = data.DeleteTitleChanges,
            DeletePinNotifications = data.DeletePinNotifications,
            DeleteChatCreationMessages = data.DeleteChatCreationMessages
        };
    }

    extension(ServiceMessageDeletionConfig model)
    {
        public ServiceMessageDeletionConfigData ToData() => new()
        {
            DeleteJoinMessages = model.DeleteJoinMessages,
            DeleteLeaveMessages = model.DeleteLeaveMessages,
            DeletePhotoChanges = model.DeletePhotoChanges,
            DeleteTitleChanges = model.DeleteTitleChanges,
            DeletePinNotifications = model.DeletePinNotifications,
            DeleteChatCreationMessages = model.DeleteChatCreationMessages
        };
    }
}

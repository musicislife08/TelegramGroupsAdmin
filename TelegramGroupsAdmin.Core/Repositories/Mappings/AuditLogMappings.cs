using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Audit Log records
/// </summary>
public static class AuditLogMappings
{
    extension(DataModels.AuditLogRecordDto data)
    {
        public AuditLogRecord ToModel() => new(
            Id: data.Id,
            EventType: (AuditEventType)data.EventType,
            Timestamp: data.Timestamp,

            // Actor exclusive arc (ARCH-2)
            ActorWebUserId: data.ActorWebUserId,
            ActorTelegramUserId: data.ActorTelegramUserId,
            ActorSystemIdentifier: data.ActorSystemIdentifier,

            // Target exclusive arc (ARCH-2)
            TargetWebUserId: data.TargetWebUserId,
            TargetTelegramUserId: data.TargetTelegramUserId,
            TargetSystemIdentifier: data.TargetSystemIdentifier,

            Value: data.Value
        );
    }
}

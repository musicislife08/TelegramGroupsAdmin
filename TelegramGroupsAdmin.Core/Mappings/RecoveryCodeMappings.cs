using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Mappings;

/// <summary>
/// Mapping extensions for Recovery Code records
/// </summary>
public static class RecoveryCodeMappings
{
    extension(DataModels.RecoveryCodeRecordDto data)
    {
        public RecoveryCodeRecord ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            CodeHash: data.CodeHash,
            UsedAt: data.UsedAt
        );
    }
}

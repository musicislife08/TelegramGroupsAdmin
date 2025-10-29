using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Recovery Code records
/// </summary>
public static class RecoveryCodeMappings
{
    public static UiModels.RecoveryCodeRecord ToModel(this DataModels.RecoveryCodeRecordDto data) => new(
        Id: data.Id,
        UserId: data.UserId,
        CodeHash: data.CodeHash,
        UsedAt: data.UsedAt
    );
}

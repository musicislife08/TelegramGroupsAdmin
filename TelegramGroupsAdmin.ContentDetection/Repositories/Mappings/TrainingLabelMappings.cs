using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Training Label records
/// </summary>
public static class TrainingLabelMappings
{
    extension(DataModels.TrainingLabelDto data)
    {
        public UiModels.TrainingLabelRecord ToModel()
        {
            return new UiModels.TrainingLabelRecord
            {
                MessageId = data.MessageId,
                Label = (TrainingLabel)data.Label, // Cast short → enum
                LabeledByUserId = data.LabeledByUserId,
                LabeledAt = data.LabeledAt,
                Reason = data.Reason,
                AuditLogId = data.AuditLogId
            };
        }
    }

    extension(UiModels.TrainingLabelRecord ui)
    {
        public DataModels.TrainingLabelDto ToDto()
        {
            return new DataModels.TrainingLabelDto
            {
                MessageId = ui.MessageId,
                Label = (short)ui.Label, // Cast enum → short
                LabeledByUserId = ui.LabeledByUserId,
                LabeledAt = ui.LabeledAt,
                Reason = ui.Reason,
                AuditLogId = ui.AuditLogId
            };
        }
    }
}

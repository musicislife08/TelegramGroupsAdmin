using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Report records (maps to ReportDto in Data layer).
/// Handles conversion between Data layer short and domain ReportType enum.
/// </summary>
public static class ReportMappings
{
    extension(DataModels.ReportDto data)
    {
        /// <summary>
        /// Convert ReportDto to ContentReport model (full details for content reports)
        /// </summary>
        public UiModels.Report ToModel() => new(
            Id: data.Id,
            MessageId: data.MessageId,
            ChatId: data.ChatId,
            ReportCommandMessageId: data.ReportCommandMessageId,
            ReportedByUserId: data.ReportedByUserId,
            ReportedByUserName: data.ReportedByUserName,
            ReportedAt: data.ReportedAt,
            Status: (UiModels.ReportStatus)data.Status,  // int → enum
            ReviewedBy: data.ReviewedBy,
            ReviewedAt: data.ReviewedAt,
            ActionTaken: data.ActionTaken,
            AdminNotes: data.AdminNotes
        );

        /// <summary>
        /// Convert ReportDto to base ReportBase model for generic operations.
        /// Does not include type-specific context - use type-specific repository methods for that.
        /// </summary>
        public UiModels.ReportBase ToBaseModel(string? chatName = null, long? subjectUserId = null) => new()
        {
            Id = data.Id,
            Type = (UiModels.ReportType)data.Type,  // short → enum
            ChatId = data.ChatId,
            CreatedAt = data.ReportedAt,
            Status = (UiModels.ReportStatus)data.Status,  // int → enum
            ReviewedBy = data.ReviewedBy,
            ReviewedAt = data.ReviewedAt,
            ActionTaken = data.ActionTaken,
            AdminNotes = data.AdminNotes,
            Context = data.Context,
            ChatName = chatName,
            SubjectUserId = subjectUserId,
            // ContentReport-specific fields
            MessageId = data.MessageId > 0 ? data.MessageId : null,
            ReportCommandMessageId = data.ReportCommandMessageId
        };
    }

    extension(UiModels.Report ui)
    {
        public DataModels.ReportDto ToDto() => new()
        {
            Id = ui.Id,
            Type = (short)UiModels.ReportType.ContentReport,  // enum → short
            MessageId = ui.MessageId,
            ChatId = ui.ChatId,
            ReportCommandMessageId = ui.ReportCommandMessageId,
            ReportedByUserId = ui.ReportedByUserId,
            ReportedByUserName = ui.ReportedByUserName,
            ReportedAt = ui.ReportedAt,
            Status = (int)ui.Status,  // enum → int
            ReviewedBy = ui.ReviewedBy,
            ReviewedAt = ui.ReviewedAt,
            ActionTaken = ui.ActionTaken,
            AdminNotes = ui.AdminNotes
        };
    }
}

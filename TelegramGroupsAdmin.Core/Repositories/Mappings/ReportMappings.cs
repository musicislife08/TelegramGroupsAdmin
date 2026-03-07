using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

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
        public Report ToModel() => new(
            Id: data.Id,
            MessageId: data.MessageId,
            Chat: new ChatIdentity(data.ChatId, null),
            ReportCommandMessageId: data.ReportCommandMessageId,
            ReportedByUserId: data.ReportedByUserId,
            ReportedByUserName: data.ReportedByUserName,
            ReportedAt: data.ReportedAt,
            Status: (ReportStatus)data.Status,  // int → enum
            ReviewedBy: data.ReviewedBy,
            ReviewedAt: data.ReviewedAt,
            ActionTaken: data.ActionTaken,
            AdminNotes: data.AdminNotes
        );

        /// <summary>
        /// Convert ReportDto to base ReportBase model for generic operations.
        /// Does not include type-specific context - use type-specific repository methods for that.
        /// </summary>
        public ReportBase ToBaseModel(string? chatName = null, long? subjectUserId = null) => new()
        {
            Id = data.Id,
            Type = (ReportType)data.Type,  // short → enum
            Chat = new ChatIdentity(data.ChatId, chatName),
            CreatedAt = data.ReportedAt,
            Status = (ReportStatus)data.Status,  // int → enum
            ReviewedBy = data.ReviewedBy,
            ReviewedAt = data.ReviewedAt,
            ActionTaken = data.ActionTaken,
            AdminNotes = data.AdminNotes,
            Context = data.Context,
            SubjectUserId = subjectUserId,
            // ContentReport-specific fields
            MessageId = data.MessageId > 0 ? data.MessageId : null,
            ReportCommandMessageId = data.ReportCommandMessageId
        };
    }

    extension(Report ui)
    {
        public DataModels.ReportDto ToDto() => new()
        {
            Id = ui.Id,
            Type = (short)ReportType.ContentReport,  // enum → short
            MessageId = ui.MessageId,
            ChatId = ui.Chat.Id,
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

using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Report records
/// </summary>
public static class ReportMappings
{
    public static UiModels.Report ToModel(this DataModels.ReportDto data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        ChatId: data.ChatId,
        ReportCommandMessageId: data.ReportCommandMessageId,
        ReportedByUserId: data.ReportedByUserId,
        ReportedByUserName: data.ReportedByUserName,
        ReportedAt: data.ReportedAt,
        Status: data.Status,
        ReviewedBy: data.ReviewedBy,
        ReviewedAt: data.ReviewedAt,
        ActionTaken: data.ActionTaken,
        AdminNotes: data.AdminNotes
    );

    public static DataModels.ReportDto ToDto(this UiModels.Report ui) => new()
    {
        Id = ui.Id,
        MessageId = ui.MessageId,
        ChatId = ui.ChatId,
        ReportCommandMessageId = ui.ReportCommandMessageId,
        ReportedByUserId = ui.ReportedByUserId,
        ReportedByUserName = ui.ReportedByUserName,
        ReportedAt = ui.ReportedAt,
        Status = (DataModels.ReportStatus)ui.Status,
        ReviewedBy = ui.ReviewedBy,
        ReviewedAt = ui.ReviewedAt,
        ActionTaken = ui.ActionTaken,
        AdminNotes = ui.AdminNotes
    };
}

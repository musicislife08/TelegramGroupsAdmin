using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Impersonation Alert records (Phase 4.10)
/// </summary>
public static class ImpersonationAlertMappings
{
    extension(DataModels.ImpersonationAlertRecordDto data)
    {
        /// <summary>
        /// Convert ImpersonationAlertRecordDto to UI model
        /// Requires joined data for display names/photos
        /// </summary>
        public UiModels.ImpersonationAlertRecord ToModel(
            string? suspectedUserName = null,
            string? suspectedFirstName = null,
            string? suspectedLastName = null,
            string? suspectedPhotoPath = null,
            string? targetUserName = null,
            string? targetFirstName = null,
            string? targetLastName = null,
            string? targetPhotoPath = null,
            string? chatName = null,
            string? reviewedByEmail = null) => new()
            {
                Id = data.Id,
                SuspectedUserId = data.SuspectedUserId,
                TargetUserId = data.TargetUserId,
                ChatId = data.ChatId,
                TotalScore = data.TotalScore,
                RiskLevel = data.RiskLevel,
                NameMatch = data.NameMatch,
                PhotoMatch = data.PhotoMatch,
                PhotoSimilarityScore = data.PhotoSimilarityScore,
                DetectedAt = data.DetectedAt,
                AutoBanned = data.AutoBanned,
                ReviewedByUserId = data.ReviewedByUserId,
                ReviewedAt = data.ReviewedAt,
                Verdict = data.Verdict,
                SuspectedUserName = suspectedUserName,
                SuspectedFirstName = suspectedFirstName,
                SuspectedLastName = suspectedLastName,
                SuspectedPhotoPath = suspectedPhotoPath,
                TargetUserName = targetUserName,
                TargetFirstName = targetFirstName,
                TargetLastName = targetLastName,
                TargetPhotoPath = targetPhotoPath,
                ChatName = chatName,
                ReviewedByEmail = reviewedByEmail
            };
    }

    extension(UiModels.ImpersonationAlertRecord ui)
    {
        public DataModels.ImpersonationAlertRecordDto ToDto() => new()
        {
            Id = ui.Id,
            SuspectedUserId = ui.SuspectedUserId,
            TargetUserId = ui.TargetUserId,
            ChatId = ui.ChatId,
            TotalScore = ui.TotalScore,
            RiskLevel = ui.RiskLevel,
            NameMatch = ui.NameMatch,
            PhotoMatch = ui.PhotoMatch,
            PhotoSimilarityScore = ui.PhotoSimilarityScore,
            DetectedAt = ui.DetectedAt,
            AutoBanned = ui.AutoBanned,
            ReviewedByUserId = ui.ReviewedByUserId,
            ReviewedAt = ui.ReviewedAt,
            Verdict = ui.Verdict
        };
    }
}

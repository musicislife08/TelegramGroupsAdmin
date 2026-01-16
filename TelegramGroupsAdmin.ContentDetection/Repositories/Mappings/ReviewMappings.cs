using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;

/// <summary>
/// Mapping extensions for base Review records (generic operations across all types).
/// </summary>
public static class ReviewMappings
{
    extension(DataModels.ReviewDto data)
    {
        /// <summary>
        /// Convert ReviewDto to base Review model for generic operations.
        /// Does not include type-specific context - use type-specific repository methods for that.
        /// </summary>
        public UiModels.Review ToBaseModel(string? chatName = null) => new()
        {
            Id = data.Id,
            Type = data.Type,
            ChatId = data.ChatId,
            CreatedAt = data.ReportedAt,
            Status = data.Status,
            ReviewedBy = data.ReviewedBy,
            ReviewedAt = data.ReviewedAt,
            ActionTaken = data.ActionTaken,
            AdminNotes = data.AdminNotes,
            Context = data.Context,
            ChatName = chatName,
            // SubjectUserId depends on type - callers needing this should use type-specific methods
            SubjectUserId = null
        };
    }
}

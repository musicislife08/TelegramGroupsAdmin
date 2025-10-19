using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using DomainModels = TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Extension methods for converting Data DTOs to SpamDetection domain models
/// Internal to SpamDetection library - DTOs never exposed to consumers
/// </summary>
internal static class ModelMappings
{
    // StopWord mappings
    // NOTE: ToModel removed - StopWordsRepository now directly constructs domain models with Actor resolution (Phase 4.19)

    public static DataModels.StopWordDto ToDto(this DomainModels.StopWord model)
    {
        // Phase 4.19: AddedBy string → Actor system
        // For ToDto, we can only set system_identifier since we don't have enough info to determine actor type
        // This is fine since ToDto is only used for AddStopWordAsync which comes from UI with proper actor context
        return new()
        {
            Id = model.Id,
            Word = model.Word,
            Enabled = model.Enabled,
            AddedDate = model.AddedDate,
            SystemIdentifier = model.AddedBy, // Simplified: treat AddedBy string as system identifier
            Notes = model.Notes
        };
    }

    // NOTE: TrainingSample mappings removed - training data comes from detection_results.used_for_training
}

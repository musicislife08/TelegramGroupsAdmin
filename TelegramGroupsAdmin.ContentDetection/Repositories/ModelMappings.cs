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

    // ContentCheckConfig mappings (Phase 4.14: Critical Checks Infrastructure)

    public static DomainModels.ContentCheckConfig ToModel(this DataModels.SpamCheckConfigRecordDto dto)
    {
        return new DomainModels.ContentCheckConfig(
            Id: dto.Id,
            ChatId: dto.ChatId,
            CheckName: dto.CheckName,
            Enabled: dto.Enabled,
            AlwaysRun: dto.AlwaysRun,
            ConfidenceThreshold: dto.ConfidenceThreshold,
            ConfigurationJson: dto.ConfigurationJson,
            ModifiedDate: dto.ModifiedDate,
            ModifiedBy: dto.ModifiedBy
        );
    }

    public static DataModels.SpamCheckConfigRecordDto ToDto(this DomainModels.ContentCheckConfig model)
    {
        return new()
        {
            Id = model.Id,
            ChatId = model.ChatId,
            CheckName = model.CheckName,
            Enabled = model.Enabled,
            AlwaysRun = model.AlwaysRun,
            ConfidenceThreshold = model.ConfidenceThreshold,
            ConfigurationJson = model.ConfigurationJson,
            ModifiedDate = model.ModifiedDate,
            ModifiedBy = model.ModifiedBy
        };
    }
}

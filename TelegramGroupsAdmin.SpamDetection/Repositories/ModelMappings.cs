using DataModels = TelegramGroupsAdmin.Data.Models;
using DomainModels = TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Extension methods for converting Data DTOs to SpamDetection domain models
/// Internal to SpamDetection library - DTOs never exposed to consumers
/// </summary>
internal static class ModelMappings
{
    // StopWord mappings
    public static DomainModels.StopWord ToModel(this DataModels.StopWordWithEmailDto dto) => new(
        Id: dto.StopWord.Id,
        Word: dto.StopWord.Word,
        Enabled: dto.StopWord.Enabled,
        AddedDate: dto.StopWord.AddedDate,
        AddedBy: dto.AddedByEmail,
        Notes: dto.StopWord.Notes
    );

    public static DataModels.StopWordDto ToDto(this DomainModels.StopWord model) => new()
    {
        Id = model.Id,
        Word = model.Word,
        Enabled = model.Enabled,
        AddedDate = model.AddedDate,
        AddedBy = model.AddedBy,
        Notes = model.Notes
    };

    // NOTE: TrainingSample mappings removed - training data comes from detection_results.used_for_training
}

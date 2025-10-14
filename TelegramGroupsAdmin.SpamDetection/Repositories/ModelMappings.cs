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

    // TrainingSample mappings
    public static DomainModels.TrainingSample ToModel(this DataModels.TrainingSampleDto dto) => new(
        Id: dto.Id,
        MessageText: dto.MessageText,
        IsSpam: dto.IsSpam,
        AddedDate: dto.AddedDate,
        Source: dto.Source,
        ConfidenceWhenAdded: dto.ConfidenceWhenAdded,
        ChatIds: dto.ChatIds ?? Array.Empty<long>(),
        AddedBy: dto.AddedBy,
        DetectionCount: dto.DetectionCount,
        LastDetectedDate: dto.LastDetectedDate
    );

    public static DomainModels.TrainingStats ToModel(this DataModels.TrainingStatsDto dto) => new(
        TotalSamples: dto.TotalSamples,
        SpamSamples: dto.SpamSamples,
        HamSamples: dto.HamSamples,
        SpamPercentage: dto.SpamPercentage,
        SamplesBySource: dto.SamplesBySource
    );
}

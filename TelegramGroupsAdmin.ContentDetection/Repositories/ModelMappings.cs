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
    // NOTE: ContentCheckConfig mappings removed - critical checks now stored in ContentDetectionConfig.AlwaysRun

    // FileScanResult mappings (Phase 4.17: File Scanning)

    public static DomainModels.FileScanResultModel ToModel(this DataModels.FileScanResultRecord dto)
    {
        return new DomainModels.FileScanResultModel(
            Id: dto.Id,
            FileHash: dto.FileHash,
            Scanner: dto.Scanner,
            Result: dto.Result,
            ThreatName: dto.ThreatName,
            ScanDurationMs: dto.ScanDurationMs,
            ScannedAt: dto.ScannedAt,
            MetadataJson: dto.Metadata
        );
    }

    public static DataModels.FileScanResultRecord ToDto(this DomainModels.FileScanResultModel model)
    {
        return new()
        {
            Id = model.Id,
            FileHash = model.FileHash,
            Scanner = model.Scanner,
            Result = model.Result,
            ThreatName = model.ThreatName,
            ScanDurationMs = model.ScanDurationMs,
            ScannedAt = model.ScannedAt,
            Metadata = model.MetadataJson
        };
    }

    // FileScanQuota mappings (Phase 4.17 - Phase 2: Cloud Queue)

    public static DomainModels.FileScanQuotaModel ToModel(this DataModels.FileScanQuotaRecord dto)
    {
        return new DomainModels.FileScanQuotaModel(
            Id: dto.Id,
            Service: dto.Service,
            QuotaType: dto.QuotaType,
            QuotaWindowStart: dto.QuotaWindowStart,
            QuotaWindowEnd: dto.QuotaWindowEnd,
            Count: dto.Count,
            LimitValue: dto.LimitValue,
            LastUpdated: dto.LastUpdated
        );
    }

    public static DataModels.FileScanQuotaRecord ToDto(this DomainModels.FileScanQuotaModel model)
    {
        return new()
        {
            Id = model.Id,
            Service = model.Service,
            QuotaType = model.QuotaType,
            QuotaWindowStart = model.QuotaWindowStart,
            QuotaWindowEnd = model.QuotaWindowEnd,
            Count = model.Count,
            LimitValue = model.LimitValue,
            LastUpdated = model.LastUpdated
        };
    }
}

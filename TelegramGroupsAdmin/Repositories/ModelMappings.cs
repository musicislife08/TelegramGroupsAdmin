using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Extension methods to map Data layer models to UI layer models
/// This ensures the UI is decoupled from database structure
/// </summary>
internal static class ModelMappings
{
    // ChatAdmin mappings
    public static UiModels.ChatAdmin ToUiModel(this DataModels.ChatAdminRecord data) => new()
    {
        Id = data.id,
        ChatId = data.chat_id,
        TelegramId = data.telegram_id,
        IsCreator = data.is_creator,
        PromotedAt = data.promoted_at,
        LastVerifiedAt = data.last_verified_at,
        IsActive = data.is_active
    };

    public static DataModels.ChatAdminRecord ToDataModel(this UiModels.ChatAdmin ui) => new()
    {
        id = ui.Id,
        chat_id = ui.ChatId,
        telegram_id = ui.TelegramId,
        is_creator = ui.IsCreator,
        promoted_at = ui.PromotedAt,
        last_verified_at = ui.LastVerifiedAt,
        is_active = ui.IsActive
    };


    // User mappings
    public static UiModels.UserRecord ToUiModel(this DataModels.UserRecord data) => new(
        Id: data.Id,
        Email: data.Email,
        NormalizedEmail: data.NormalizedEmail,
        PasswordHash: data.PasswordHash,
        SecurityStamp: data.SecurityStamp,
        PermissionLevel: (UiModels.PermissionLevel)data.PermissionLevel,
        InvitedBy: data.InvitedBy,
        IsActive: data.IsActive,
        TotpSecret: data.TotpSecret,
        TotpEnabled: data.TotpEnabled,
        TotpSetupStartedAt: data.TotpSetupStartedAt,
        CreatedAt: data.CreatedAt,
        LastLoginAt: data.LastLoginAt,
        Status: (UiModels.UserStatus)data.Status,
        ModifiedBy: data.ModifiedBy,
        ModifiedAt: data.ModifiedAt,
        EmailVerified: data.EmailVerified,
        EmailVerificationToken: data.EmailVerificationToken,
        EmailVerificationTokenExpiresAt: data.EmailVerificationTokenExpiresAt,
        PasswordResetToken: data.PasswordResetToken,
        PasswordResetTokenExpiresAt: data.PasswordResetTokenExpiresAt
    );

    public static DataModels.UserRecord ToDataModel(this UiModels.UserRecord ui) => new(
        Id: ui.Id,
        Email: ui.Email,
        NormalizedEmail: ui.NormalizedEmail,
        PasswordHash: ui.PasswordHash,
        SecurityStamp: ui.SecurityStamp,
        PermissionLevel: (int)ui.PermissionLevel,
        InvitedBy: ui.InvitedBy,
        IsActive: ui.IsActive,
        TotpSecret: ui.TotpSecret,
        TotpEnabled: ui.TotpEnabled,
        TotpSetupStartedAt: ui.TotpSetupStartedAt,
        CreatedAt: ui.CreatedAt,
        LastLoginAt: ui.LastLoginAt,
        Status: (DataModels.UserStatus)ui.Status,
        ModifiedBy: ui.ModifiedBy,
        ModifiedAt: ui.ModifiedAt,
        EmailVerified: ui.EmailVerified,
        EmailVerificationToken: ui.EmailVerificationToken,
        EmailVerificationTokenExpiresAt: ui.EmailVerificationTokenExpiresAt,
        PasswordResetToken: ui.PasswordResetToken,
        PasswordResetTokenExpiresAt: ui.PasswordResetTokenExpiresAt
    );

    // Recovery Code mappings
    public static UiModels.RecoveryCodeRecord ToUiModel(this DataModels.RecoveryCodeRecord data) => new(
        Id: data.Id,
        UserId: data.UserId,
        CodeHash: data.CodeHash,
        UsedAt: data.UsedAt
    );

    // Invite mappings
    public static UiModels.InviteRecord ToUiModel(this DataModels.InviteRecord data) => new(
        Token: data.Token,
        CreatedBy: data.CreatedBy,
        CreatedAt: data.CreatedAt,
        ExpiresAt: data.ExpiresAt,
        UsedBy: data.UsedBy,
        PermissionLevel: (UiModels.PermissionLevel)data.PermissionLevel,
        Status: (UiModels.InviteStatus)data.Status,
        ModifiedAt: data.ModifiedAt
    );

    public static UiModels.InviteWithCreator ToUiModel(this DataModels.InviteWithCreator data) => new(
        Token: data.Invite.Token,
        CreatedBy: data.Invite.CreatedBy,
        CreatedByEmail: data.CreatorEmail,
        CreatedAt: data.Invite.CreatedAt,
        ExpiresAt: data.Invite.ExpiresAt,
        UsedBy: data.Invite.UsedBy,
        UsedByEmail: null, // Not available in Data model - would need additional lookup
        PermissionLevel: (UiModels.PermissionLevel)data.Invite.PermissionLevel,
        Status: (UiModels.InviteStatus)data.Invite.Status,
        ModifiedAt: data.Invite.ModifiedAt
    );

    // Audit Log mappings
    public static UiModels.AuditLogRecord ToUiModel(this DataModels.AuditLogRecord data) => new(
        Id: data.Id,
        EventType: (UiModels.AuditEventType)data.EventType,
        Timestamp: data.Timestamp,
        ActorUserId: data.ActorUserId,
        TargetUserId: data.TargetUserId,
        Value: data.Value
    );

    // Message mappings
    public static UiModels.MessageRecord ToUiModel(this DataModels.MessageRecord data) => new(
        MessageId: data.MessageId,
        UserId: data.UserId,
        UserName: data.UserName,
        ChatId: data.ChatId,
        Timestamp: data.Timestamp,
        MessageText: data.MessageText,
        PhotoFileId: data.PhotoFileId,
        PhotoFileSize: data.PhotoFileSize,
        Urls: data.Urls,
        EditDate: data.EditDate,
        ContentHash: data.ContentHash,
        ChatName: data.ChatName,
        PhotoLocalPath: data.PhotoLocalPath,
        PhotoThumbnailPath: data.PhotoThumbnailPath,
        DeletedAt: data.DeletedAt,
        DeletionSource: data.DeletionSource
    );

    public static DataModels.MessageRecord ToDataModel(this UiModels.MessageRecord ui) => new(
        MessageId: ui.MessageId,
        UserId: ui.UserId,
        UserName: ui.UserName,
        ChatId: ui.ChatId,
        Timestamp: ui.Timestamp,
        MessageText: ui.MessageText,
        PhotoFileId: ui.PhotoFileId,
        PhotoFileSize: ui.PhotoFileSize,
        Urls: ui.Urls,
        EditDate: ui.EditDate,
        ContentHash: ui.ContentHash,
        ChatName: ui.ChatName,
        PhotoLocalPath: ui.PhotoLocalPath,
        PhotoThumbnailPath: ui.PhotoThumbnailPath,
        DeletedAt: ui.DeletedAt,
        DeletionSource: ui.DeletionSource
    );

    public static UiModels.PhotoMessageRecord ToUiModel(this DataModels.PhotoMessageRecord data) => new(
        FileId: data.FileId,
        MessageText: data.MessageText,
        Timestamp: data.Timestamp
    );

    public static UiModels.HistoryStats ToUiModel(this DataModels.HistoryStats data) => new(
        TotalMessages: (int)data.TotalMessages,
        UniqueUsers: (int)data.UniqueUsers,
        PhotoCount: (int)data.PhotoCount,
        OldestTimestamp: data.OldestTimestamp,
        NewestTimestamp: data.NewestTimestamp
    );

    public static UiModels.MessageEditRecord ToUiModel(this DataModels.MessageEditRecord data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        OldText: data.OldText,
        NewText: data.NewText,
        EditDate: data.EditDate,
        OldContentHash: data.OldContentHash,
        NewContentHash: data.NewContentHash
    );

    public static DataModels.MessageEditRecord ToDataModel(this UiModels.MessageEditRecord ui) => new(
        Id: ui.Id,
        MessageId: ui.MessageId,
        EditDate: ui.EditDate,
        OldText: ui.OldText,
        NewText: ui.NewText,
        OldContentHash: ui.OldContentHash,
        NewContentHash: ui.NewContentHash
    );

    public static UiModels.SpamCheckRecord ToUiModel(this DataModels.SpamCheckRecord data) => new(
        Id: data.Id,
        CheckTimestamp: data.CheckTimestamp,
        UserId: data.UserId,
        ContentHash: data.ContentHash,
        IsSpam: data.IsSpam,
        Confidence: data.Confidence,
        Reason: data.Reason,
        CheckType: data.CheckType,
        MatchedMessageId: data.MatchedMessageId
    );

    public static DataModels.SpamCheckRecord ToDataModel(this UiModels.SpamCheckRecord ui) => new(
        Id: ui.Id,
        CheckTimestamp: ui.CheckTimestamp,
        UserId: ui.UserId,
        ContentHash: ui.ContentHash,
        IsSpam: ui.IsSpam,
        Confidence: ui.Confidence,
        Reason: ui.Reason,
        CheckType: ui.CheckType,
        MatchedMessageId: ui.MatchedMessageId
    );

    public static DataModels.InviteRecord ToDataModel(this UiModels.InviteRecord ui) => new(
        Token: ui.Token,
        CreatedBy: ui.CreatedBy,
        CreatedAt: ui.CreatedAt,
        ExpiresAt: ui.ExpiresAt,
        UsedBy: ui.UsedBy,
        PermissionLevel: (int)ui.PermissionLevel,
        Status: (DataModels.InviteStatus)ui.Status,
        ModifiedAt: ui.ModifiedAt
    );

    // Spam Detection mappings
    public static UiModels.TrainingSample ToUiModel(this DataModels.TrainingSample data) => new(
        Id: data.Id,
        MessageText: data.MessageText,
        IsSpam: data.IsSpam,
        AddedDate: data.AddedDate,
        Source: data.Source,
        ConfidenceWhenAdded: data.ConfidenceWhenAdded,
        ChatIds: data.ChatIds,
        AddedBy: data.AddedBy,
        DetectionCount: data.DetectionCount,
        LastDetectedDate: data.LastDetectedDate
    );

    public static UiModels.TrainingStats ToUiModel(this DataModels.TrainingStats data) => new(
        TotalSamples: data.TotalSamples,
        SpamSamples: data.SpamSamples,
        HamSamples: data.HamSamples,
        SpamPercentage: data.SpamPercentage,
        SamplesBySource: data.SamplesBySource
    );


    public static UiModels.StopWord ToUiModel(this DataModels.StopWord data) => new(
        Id: data.Id,
        Word: data.Word,
        Enabled: data.Enabled,
        AddedDate: data.AddedDate,
        AddedBy: data.AddedBy,
        Notes: data.Notes
    );

    // Verification Token mappings
    public static UiModels.VerificationToken ToUiModel(this DataModels.VerificationToken data) => new(
        Id: data.Id,
        UserId: data.UserId,
        TokenType: (UiModels.TokenType)data.TokenType,
        Token: data.Token,
        Value: data.Value,
        ExpiresAt: data.ExpiresAt,
        CreatedAt: data.CreatedAt,
        UsedAt: data.UsedAt
    );

    public static DataModels.VerificationToken ToDataModel(this UiModels.VerificationToken ui) => new(
        Id: ui.Id,
        UserId: ui.UserId,
        TokenType: (DataModels.TokenType)ui.TokenType,
        Token: ui.Token,
        Value: ui.Value,
        ExpiresAt: ui.ExpiresAt,
        CreatedAt: ui.CreatedAt,
        UsedAt: ui.UsedAt
    );

    // Detection Result mappings
    public static UiModels.DetectionResultRecord ToUiModel(this DataModels.DetectionResultRecord data)
    {
        return new UiModels.DetectionResultRecord
        {
            Id = data.Id,
            MessageId = data.MessageId,
            DetectedAt = data.DetectedAt,
            DetectionSource = data.DetectionSource,
            DetectionMethod = data.DetectionMethod,
            IsSpam = data.IsSpam,
            Confidence = data.Confidence,
            Reason = data.Reason,
            AddedBy = data.AddedBy,
            UserId = 0, // Will be populated by repository join
            MessageText = null // Will be populated by repository join
        };
    }

    public static DataModels.DetectionResultRecord ToDataModel(this UiModels.DetectionResultRecord ui) => new(
        Id: ui.Id,
        MessageId: ui.MessageId,
        DetectedAt: ui.DetectedAt,
        DetectionSource: ui.DetectionSource,
        DetectionMethod: ui.DetectionMethod,
        IsSpam: ui.IsSpam,
        Confidence: ui.Confidence,
        Reason: ui.Reason,
        AddedBy: ui.AddedBy
    );

    // User Action mappings
    public static UiModels.UserActionRecord ToUiModel(this DataModels.UserActionRecord data) => new(
        Id: data.Id,
        UserId: data.UserId,
        ChatIds: data.ChatIds,
        ActionType: (UiModels.UserActionType)data.ActionType,
        MessageId: data.MessageId,
        IssuedBy: data.IssuedBy,
        IssuedAt: data.IssuedAt,
        ExpiresAt: data.ExpiresAt,
        Reason: data.Reason
    );

    public static DataModels.UserActionRecord ToDataModel(this UiModels.UserActionRecord ui) => new(
        Id: ui.Id,
        UserId: ui.UserId,
        ChatIds: ui.ChatIds,
        ActionType: (int)ui.ActionType,
        MessageId: ui.MessageId,
        IssuedBy: ui.IssuedBy,
        IssuedAt: ui.IssuedAt,
        ExpiresAt: ui.ExpiresAt,
        Reason: ui.Reason
    );

    // Managed Chat mappings
    public static UiModels.ManagedChatRecord ToUiModel(this DataModels.ManagedChatRecord data) => new(
        ChatId: data.ChatId,
        ChatName: data.ChatName,
        ChatType: (UiModels.ManagedChatType)data.ChatType,
        BotStatus: (UiModels.BotChatStatus)data.BotStatus,
        IsAdmin: data.IsAdmin,
        AddedAt: data.AddedAt,
        IsActive: data.IsActive,
        LastSeenAt: data.LastSeenAt,
        SettingsJson: data.SettingsJson
    );

    public static DataModels.ManagedChatRecord ToDataModel(this UiModels.ManagedChatRecord ui) => new(
        ChatId: ui.ChatId,
        ChatName: ui.ChatName,
        ChatType: (int)ui.ChatType,
        BotStatus: (int)ui.BotStatus,
        IsAdmin: ui.IsAdmin,
        AddedAt: ui.AddedAt,
        IsActive: ui.IsActive,
        LastSeenAt: ui.LastSeenAt,
        SettingsJson: ui.SettingsJson
    );

    // TelegramUserMapping mappings
    public static UiModels.TelegramUserMappingRecord ToUiModel(this DataModels.TelegramUserMappingRecord data) => new(
        Id: data.id,
        TelegramId: data.telegram_id,
        TelegramUsername: data.telegram_username,
        UserId: data.user_id,
        LinkedAt: data.linked_at,
        IsActive: data.is_active
    );

    public static DataModels.TelegramUserMappingRecord ToDataModel(this UiModels.TelegramUserMappingRecord ui) => new()
    {
        id = ui.Id,
        telegram_id = ui.TelegramId,
        telegram_username = ui.TelegramUsername,
        user_id = ui.UserId,
        linked_at = ui.LinkedAt,
        is_active = ui.IsActive
    };

    // TelegramLinkToken mappings
    public static UiModels.TelegramLinkTokenRecord ToUiModel(this DataModels.TelegramLinkTokenRecord data) => new(
        Token: data.token,
        UserId: data.user_id,
        CreatedAt: data.created_at,
        ExpiresAt: data.expires_at,
        UsedAt: data.used_at,
        UsedByTelegramId: data.used_by_telegram_id
    );

    public static DataModels.TelegramLinkTokenRecord ToDataModel(this UiModels.TelegramLinkTokenRecord ui) => new()
    {
        token = ui.Token,
        user_id = ui.UserId,
        created_at = ui.CreatedAt,
        expires_at = ui.ExpiresAt,
        used_at = ui.UsedAt,
        used_by_telegram_id = ui.UsedByTelegramId
    };

    // Report mappings
    public static UiModels.Report ToUiModel(this DataModels.Report data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        ChatId: data.ChatId,
        ReportCommandMessageId: data.ReportCommandMessageId,
        ReportedByUserId: data.ReportedByUserId,
        ReportedByUserName: data.ReportedByUserName,
        ReportedAt: data.ReportedAt,
        Status: (UiModels.ReportStatus)data.Status,
        ReviewedBy: data.ReviewedBy,
        ReviewedAt: data.ReviewedAt,
        ActionTaken: data.ActionTaken,
        AdminNotes: data.AdminNotes
    );

    public static DataModels.Report ToDataModel(this UiModels.Report ui) => new(
        Id: ui.Id,
        MessageId: ui.MessageId,
        ChatId: ui.ChatId,
        ReportCommandMessageId: ui.ReportCommandMessageId,
        ReportedByUserId: ui.ReportedByUserId,
        ReportedByUserName: ui.ReportedByUserName,
        ReportedAt: ui.ReportedAt,
        Status: (DataModels.ReportStatus)ui.Status,
        ReviewedBy: ui.ReviewedBy,
        ReviewedAt: ui.ReviewedAt,
        ActionTaken: ui.ActionTaken,
        AdminNotes: ui.AdminNotes
    );
}

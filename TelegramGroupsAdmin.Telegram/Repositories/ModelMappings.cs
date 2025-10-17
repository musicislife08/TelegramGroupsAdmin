using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Extension methods to map Data layer models to UI layer models
/// This ensures the UI is decoupled from database structure
/// </summary>
public static class ModelMappings
{
    // ============================================================================
    // Actor Conversion Helpers (Phase 4.19)
    // ============================================================================

    /// <summary>
    /// Convert database actor columns (exclusive arc) to Actor model
    /// Exactly one of the three parameters must be non-null (enforced by DB CHECK constraint)
    /// </summary>
    public static UiModels.Actor ToActor(
        string? webUserId,
        long? telegramUserId,
        string? systemIdentifier,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null)
    {
        if (webUserId != null)
        {
            return UiModels.Actor.FromWebUser(webUserId, webUserEmail);
        }

        if (telegramUserId != null)
        {
            return UiModels.Actor.FromTelegramUser(telegramUserId.Value, telegramUsername, telegramFirstName);
        }

        if (systemIdentifier != null)
        {
            return UiModels.Actor.FromSystem(systemIdentifier);
        }

        // Should never happen due to CHECK constraint, but handle gracefully
        return UiModels.Actor.FromSystem("unknown");
    }

    /// <summary>
    /// Populate DTO actor columns from Actor model (for ToDto methods)
    /// </summary>
    public static void SetActorColumns(
        UiModels.Actor actor,
        out string? webUserId,
        out long? telegramUserId,
        out string? systemIdentifier)
    {
        webUserId = actor.GetWebUserId();
        telegramUserId = actor.GetTelegramUserId();
        systemIdentifier = actor.GetSystemIdentifier();
    }

    // ChatAdmin mappings
    public static UiModels.ChatAdmin ToModel(this DataModels.ChatAdminRecordDto data) => new()
    {
        Id = data.Id,
        ChatId = data.ChatId,
        TelegramId = data.TelegramId,
        Username = data.Username,
        IsCreator = data.IsCreator,
        PromotedAt = data.PromotedAt,
        LastVerifiedAt = data.LastVerifiedAt,
        IsActive = data.IsActive
    };

    public static DataModels.ChatAdminRecordDto ToDto(this UiModels.ChatAdmin ui) => new()
    {
        Id = ui.Id,
        ChatId = ui.ChatId,
        TelegramId = ui.TelegramId,
        Username = ui.Username,
        IsCreator = ui.IsCreator,
        PromotedAt = ui.PromotedAt,
        LastVerifiedAt = ui.LastVerifiedAt,
        IsActive = ui.IsActive
    };


    // User mappings
    public static UiModels.UserRecord ToModel(this DataModels.UserRecordDto data) => new(
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

    public static DataModels.UserRecordDto ToDto(this UiModels.UserRecord ui) => new()
    {
        Id = ui.Id,
        Email = ui.Email,
        NormalizedEmail = ui.NormalizedEmail,
        PasswordHash = ui.PasswordHash,
        SecurityStamp = ui.SecurityStamp,
        PermissionLevel = (DataModels.PermissionLevel)(int)ui.PermissionLevel,
        InvitedBy = ui.InvitedBy,
        IsActive = ui.IsActive,
        TotpSecret = ui.TotpSecret,
        TotpEnabled = ui.TotpEnabled,
        TotpSetupStartedAt = ui.TotpSetupStartedAt,
        CreatedAt = ui.CreatedAt,
        LastLoginAt = ui.LastLoginAt,
        Status = (DataModels.UserStatus)(int)ui.Status,
        ModifiedBy = ui.ModifiedBy,
        ModifiedAt = ui.ModifiedAt,
        EmailVerified = ui.EmailVerified,
        EmailVerificationToken = ui.EmailVerificationToken,
        EmailVerificationTokenExpiresAt = ui.EmailVerificationTokenExpiresAt,
        PasswordResetToken = ui.PasswordResetToken,
        PasswordResetTokenExpiresAt = ui.PasswordResetTokenExpiresAt
    };

    // Recovery Code mappings
    public static UiModels.RecoveryCodeRecord ToModel(this DataModels.RecoveryCodeRecordDto data) => new(
        Id: data.Id,
        UserId: data.UserId,
        CodeHash: data.CodeHash,
        UsedAt: data.UsedAt
    );

    // Invite mappings
    public static UiModels.InviteRecord ToModel(this DataModels.InviteRecordDto data) => new(
        Token: data.Token,
        CreatedBy: data.CreatedBy,
        CreatedAt: data.CreatedAt,
        ExpiresAt: data.ExpiresAt,
        UsedBy: data.UsedBy,
        PermissionLevel: (UiModels.PermissionLevel)data.PermissionLevel,
        Status: (UiModels.InviteStatus)data.Status,
        ModifiedAt: data.ModifiedAt
    );

    public static UiModels.InviteWithCreator ToModel(this DataModels.InviteWithCreatorDto data) => new(
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
    public static UiModels.AuditLogRecord ToModel(this DataModels.AuditLogRecordDto data) => new(
        Id: data.Id,
        EventType: (UiModels.AuditEventType)data.EventType,
        Timestamp: data.Timestamp,
        ActorUserId: data.ActorUserId,
        TargetUserId: data.TargetUserId,
        Value: data.Value
    );

    // Message mappings (requires chat and user info from JOINs)
    public static UiModels.MessageRecord ToModel(
        this DataModels.MessageRecordDto data,
        string? chatName,
        string? chatIconPath,
        string? userName,
        string? firstName,
        string? userPhotoPath,
        string? replyToUser,
        string? replyToText) => new(
        MessageId: data.MessageId,
        UserId: data.UserId,
        UserName: userName,
        FirstName: firstName,
        ChatId: data.ChatId,
        Timestamp: data.Timestamp,
        MessageText: data.MessageText,
        PhotoFileId: data.PhotoFileId,
        PhotoFileSize: data.PhotoFileSize,
        Urls: data.Urls,
        EditDate: data.EditDate,
        ContentHash: data.ContentHash,
        ChatName: chatName,
        PhotoLocalPath: data.PhotoLocalPath,
        PhotoThumbnailPath: data.PhotoThumbnailPath,
        ChatIconPath: chatIconPath,
        UserPhotoPath: userPhotoPath,
        DeletedAt: data.DeletedAt,
        DeletionSource: data.DeletionSource,
        ReplyToMessageId: data.ReplyToMessageId,
        ReplyToUser: replyToUser,
        ReplyToText: replyToText
    );

    public static DataModels.MessageRecordDto ToDto(this UiModels.MessageRecord ui) => new()
    {
        MessageId = ui.MessageId,
        UserId = ui.UserId,
        ChatId = ui.ChatId,
        Timestamp = ui.Timestamp,
        MessageText = ui.MessageText,
        PhotoFileId = ui.PhotoFileId,
        PhotoFileSize = ui.PhotoFileSize,
        Urls = ui.Urls,
        EditDate = ui.EditDate,
        ContentHash = ui.ContentHash,
        PhotoLocalPath = ui.PhotoLocalPath,
        PhotoThumbnailPath = ui.PhotoThumbnailPath,
        DeletedAt = ui.DeletedAt,
        DeletionSource = ui.DeletionSource,
        ReplyToMessageId = ui.ReplyToMessageId
    };

    // Note: PhotoMessageRecord, HistoryStats, SpamCheckRecord are UI-only models
    // They are constructed directly in repositories (MessageHistoryRepository), not mapped from database entities

    public static UiModels.MessageEditRecord ToModel(this DataModels.MessageEditRecordDto data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        OldText: data.OldText,
        NewText: data.NewText,
        EditDate: data.EditDate,
        OldContentHash: data.OldContentHash,
        NewContentHash: data.NewContentHash
    );

    public static DataModels.MessageEditRecordDto ToDto(this UiModels.MessageEditRecord ui) => new()
    {
        Id = ui.Id,
        MessageId = ui.MessageId,
        EditDate = ui.EditDate,
        OldText = ui.OldText,
        NewText = ui.NewText,
        OldContentHash = ui.OldContentHash,
        NewContentHash = ui.NewContentHash
    };

    public static DataModels.InviteRecordDto ToDto(this UiModels.InviteRecord ui) => new()
    {
        Token = ui.Token,
        CreatedBy = ui.CreatedBy,
        CreatedAt = ui.CreatedAt,
        ExpiresAt = ui.ExpiresAt,
        UsedBy = ui.UsedBy,
        PermissionLevel = (DataModels.PermissionLevel)(int)ui.PermissionLevel,
        Status = (DataModels.InviteStatus)(int)ui.Status,
        ModifiedAt = ui.ModifiedAt
    };

    // Verification Token mappings
    public static UiModels.VerificationToken ToModel(this DataModels.VerificationTokenDto data) => new(
        Id: data.Id,
        UserId: data.UserId,
        TokenType: (UiModels.TokenType)data.TokenType,
        Token: data.Token,
        Value: data.Value,
        ExpiresAt: data.ExpiresAt,
        CreatedAt: data.CreatedAt,
        UsedAt: data.UsedAt
    );

    public static DataModels.VerificationTokenDto ToDto(this UiModels.VerificationToken ui) => new()
    {
        Id = ui.Id,
        UserId = ui.UserId,
        TokenType = (DataModels.TokenType)(int)ui.TokenType,
        Token = ui.Token,
        Value = ui.Value,
        ExpiresAt = ui.ExpiresAt,
        CreatedAt = ui.CreatedAt,
        UsedAt = ui.UsedAt
    };

    // Detection Result mappings (Phase 4.19: Actor conversion)
    public static UiModels.DetectionResultRecord ToModel(
        this DataModels.DetectionResultRecordDto data,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null)
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
            AddedBy = ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
            UsedForTraining = data.UsedForTraining,
            NetConfidence = data.NetConfidence,
            CheckResultsJson = data.CheckResultsJson,  // Phase 2.6
            EditVersion = data.EditVersion,             // Phase 2.6
            UserId = 0, // Will be populated by repository join
            MessageText = null // Will be populated by repository join
        };
    }

    public static DataModels.DetectionResultRecordDto ToDto(this UiModels.DetectionResultRecord ui)
    {
        SetActorColumns(ui.AddedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

        return new()
        {
            Id = ui.Id,
            MessageId = ui.MessageId,
            DetectedAt = ui.DetectedAt,
            DetectionSource = ui.DetectionSource,
            DetectionMethod = ui.DetectionMethod,
            IsSpam = ui.IsSpam,
            Confidence = ui.Confidence,
            Reason = ui.Reason,
            WebUserId = webUserId,
            TelegramUserId = telegramUserId,
            SystemIdentifier = systemIdentifier,
            UsedForTraining = ui.UsedForTraining,
            NetConfidence = ui.NetConfidence,
            CheckResultsJson = ui.CheckResultsJson,  // Phase 2.6
            EditVersion = ui.EditVersion              // Phase 2.6
        };
    }

    // User Action mappings (all actions are global now) - Phase 4.19: Actor conversion
    public static UiModels.UserActionRecord ToModel(
        this DataModels.UserActionRecordDto data,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null) => new(
        Id: data.Id,
        UserId: data.UserId,
        ActionType: (UiModels.UserActionType)data.ActionType,
        MessageId: data.MessageId,
        IssuedBy: ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
        IssuedAt: data.IssuedAt,
        ExpiresAt: data.ExpiresAt,
        Reason: data.Reason
    );

    public static DataModels.UserActionRecordDto ToDto(this UiModels.UserActionRecord ui)
    {
        SetActorColumns(ui.IssuedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

        return new()
        {
            Id = ui.Id,
            UserId = ui.UserId,
            ActionType = (DataModels.UserActionType)(int)ui.ActionType,
            MessageId = ui.MessageId,
            WebUserId = webUserId,
            TelegramUserId = telegramUserId,
            SystemIdentifier = systemIdentifier,
            IssuedAt = ui.IssuedAt,
            ExpiresAt = ui.ExpiresAt,
            Reason = ui.Reason
        };
    }

    // Managed Chat mappings
    public static UiModels.ManagedChatRecord ToModel(this DataModels.ManagedChatRecordDto data) => new(
        ChatId: data.ChatId,
        ChatName: data.ChatName,
        ChatType: (UiModels.ManagedChatType)data.ChatType,
        BotStatus: (UiModels.BotChatStatus)data.BotStatus,
        IsAdmin: data.IsAdmin,
        AddedAt: data.AddedAt,
        IsActive: data.IsActive,
        LastSeenAt: data.LastSeenAt,
        SettingsJson: data.SettingsJson,
        ChatIconPath: data.ChatIconPath
    );

    public static DataModels.ManagedChatRecordDto ToDto(this UiModels.ManagedChatRecord ui) => new()
    {
        ChatId = ui.ChatId,
        ChatName = ui.ChatName,
        ChatType = (DataModels.ManagedChatType)(int)ui.ChatType,
        BotStatus = (DataModels.BotChatStatus)(int)ui.BotStatus,
        IsAdmin = ui.IsAdmin,
        AddedAt = ui.AddedAt,
        IsActive = ui.IsActive,
        LastSeenAt = ui.LastSeenAt,
        SettingsJson = ui.SettingsJson,
        ChatIconPath = ui.ChatIconPath
    };

    // TelegramUserMapping mappings
    public static UiModels.TelegramUserMappingRecord ToModel(this DataModels.TelegramUserMappingRecordDto data) => new(
        Id: data.Id,
        TelegramId: data.TelegramId,
        TelegramUsername: data.TelegramUsername,
        UserId: data.UserId,
        LinkedAt: data.LinkedAt,
        IsActive: data.IsActive
    );

    public static DataModels.TelegramUserMappingRecordDto ToDto(this UiModels.TelegramUserMappingRecord ui) => new()
    {
        Id = ui.Id,
        TelegramId = ui.TelegramId,
        TelegramUsername = ui.TelegramUsername,
        UserId = ui.UserId,
        LinkedAt = ui.LinkedAt,
        IsActive = ui.IsActive
    };

    // TelegramLinkToken mappings
    public static UiModels.TelegramLinkTokenRecord ToModel(this DataModels.TelegramLinkTokenRecordDto data) => new(
        Token: data.Token,
        UserId: data.UserId,
        CreatedAt: data.CreatedAt,
        ExpiresAt: data.ExpiresAt,
        UsedAt: data.UsedAt,
        UsedByTelegramId: data.UsedByTelegramId
    );

    public static DataModels.TelegramLinkTokenRecordDto ToDto(this UiModels.TelegramLinkTokenRecord ui) => new()
    {
        Token = ui.Token,
        UserId = ui.UserId,
        CreatedAt = ui.CreatedAt,
        ExpiresAt = ui.ExpiresAt,
        UsedAt = ui.UsedAt,
        UsedByTelegramId = ui.UsedByTelegramId
    };

    // Report mappings
    public static UiModels.Report ToModel(this DataModels.ReportDto data) => new(
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

    // ============================================================================
    // Welcome Response Mappings (Phase 4.4)
    // ============================================================================

    public static UiModels.WelcomeResponse ToModel(this DataModels.WelcomeResponseDto data) => new(
        Id: data.Id,
        ChatId: data.ChatId,
        UserId: data.UserId,
        Username: data.Username,
        WelcomeMessageId: data.WelcomeMessageId,
        Response: (UiModels.WelcomeResponseType)Enum.Parse(typeof(UiModels.WelcomeResponseType), data.Response, ignoreCase: true),
        RespondedAt: data.RespondedAt,
        DmSent: data.DmSent,
        DmFallback: data.DmFallback,
        CreatedAt: data.CreatedAt,
        TimeoutJobId: data.TimeoutJobId
    );

    public static DataModels.WelcomeResponseDto ToDto(this UiModels.WelcomeResponse ui) => new()
    {
        Id = ui.Id,
        ChatId = ui.ChatId,
        UserId = ui.UserId,
        Username = ui.Username,
        WelcomeMessageId = ui.WelcomeMessageId,
        Response = ui.Response.ToString().ToLowerInvariant(),
        RespondedAt = ui.RespondedAt,
        DmSent = ui.DmSent,
        DmFallback = ui.DmFallback,
        CreatedAt = ui.CreatedAt,
        TimeoutJobId = ui.TimeoutJobId
    };

    // ============================================================================
    // TelegramUser Mappings (User photo centralization + future features)
    // ============================================================================

    public static UiModels.TelegramUser ToModel(this DataModels.TelegramUserDto data) => new(
        TelegramUserId: data.TelegramUserId,
        Username: data.Username,
        FirstName: data.FirstName,
        LastName: data.LastName,
        UserPhotoPath: data.UserPhotoPath,
        PhotoHash: data.PhotoHash,
        IsTrusted: data.IsTrusted,
        FirstSeenAt: data.FirstSeenAt,
        LastSeenAt: data.LastSeenAt,
        CreatedAt: data.CreatedAt,
        UpdatedAt: data.UpdatedAt
    );

    public static DataModels.TelegramUserDto ToDto(this UiModels.TelegramUser ui) => new()
    {
        TelegramUserId = ui.TelegramUserId,
        Username = ui.Username,
        FirstName = ui.FirstName,
        LastName = ui.LastName,
        UserPhotoPath = ui.UserPhotoPath,
        PhotoHash = ui.PhotoHash,
        IsTrusted = ui.IsTrusted,
        FirstSeenAt = ui.FirstSeenAt,
        LastSeenAt = ui.LastSeenAt,
        CreatedAt = ui.CreatedAt,
        UpdatedAt = ui.UpdatedAt
    };

    // ============================================================================
    // Admin Notes Mappings (Phase 4.12, Phase 4.19: Actor conversion)
    // ============================================================================

    public static UiModels.AdminNote ToModel(
        this DataModels.AdminNoteDto data,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null) => new()
    {
        Id = data.Id,
        TelegramUserId = data.TelegramUserId,
        NoteText = data.NoteText,
        CreatedBy = ToActor(data.ActorWebUserId, data.ActorTelegramUserId, data.ActorSystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
        CreatedAt = data.CreatedAt,
        UpdatedAt = data.UpdatedAt,
        IsPinned = data.IsPinned
    };

    public static DataModels.AdminNoteDto ToDto(this UiModels.AdminNote ui)
    {
        SetActorColumns(ui.CreatedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

        return new()
        {
            Id = ui.Id,
            TelegramUserId = ui.TelegramUserId,
            NoteText = ui.NoteText,
            ActorWebUserId = webUserId,
            ActorTelegramUserId = telegramUserId,
            ActorSystemIdentifier = systemIdentifier,
            CreatedAt = ui.CreatedAt,
            UpdatedAt = ui.UpdatedAt,
            IsPinned = ui.IsPinned
        };
    }

    // ============================================================================
    // User Tags Mappings (Phase 4.12: String-based tags with soft delete, Phase 4.19: Actor conversion)
    // ============================================================================

    public static UiModels.UserTag ToModel(
        this DataModels.UserTagDto data,
        string? webUserEmail = null,
        string? telegramUsername = null,
        string? telegramFirstName = null,
        string? removedByWebUserEmail = null,
        string? removedByTelegramUsername = null,
        string? removedByTelegramFirstName = null) => new()
    {
        Id = data.Id,
        TelegramUserId = data.TelegramUserId,
        TagName = data.TagName,
        AddedBy = ToActor(data.ActorWebUserId, data.ActorTelegramUserId, data.ActorSystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
        AddedAt = data.AddedAt,
        RemovedAt = data.RemovedAt,
        RemovedBy = data.RemovedAt.HasValue
            ? ToActor(data.RemovedByWebUserId, data.RemovedByTelegramUserId, data.RemovedBySystemIdentifier, removedByWebUserEmail, removedByTelegramUsername, removedByTelegramFirstName)
            : null,
        ConfidenceModifier = data.ConfidenceModifier
    };

    public static DataModels.UserTagDto ToDto(this UiModels.UserTag ui)
    {
        SetActorColumns(ui.AddedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

        string? removedByWebUserId = null;
        long? removedByTelegramUserId = null;
        string? removedBySystemIdentifier = null;

        if (ui.RemovedBy != null)
        {
            SetActorColumns(ui.RemovedBy, out removedByWebUserId, out removedByTelegramUserId, out removedBySystemIdentifier);
        }

        return new()
        {
            Id = ui.Id,
            TelegramUserId = ui.TelegramUserId,
            TagName = ui.TagName,
            ActorWebUserId = webUserId,
            ActorTelegramUserId = telegramUserId,
            ActorSystemIdentifier = systemIdentifier,
            AddedAt = ui.AddedAt,
            RemovedAt = ui.RemovedAt,
            RemovedByWebUserId = removedByWebUserId,
            RemovedByTelegramUserId = removedByTelegramUserId,
            RemovedBySystemIdentifier = removedBySystemIdentifier,
            ConfidenceModifier = ui.ConfidenceModifier
        };
    }

    // ============================================================================
    // Tag Definition Mappings (Phase 4.12: Tag color preferences)
    // ============================================================================

    public static UiModels.TagDefinition ToModel(this DataModels.TagDefinitionDto data) => new()
    {
        TagName = data.TagName,
        Color = data.Color,
        UsageCount = data.UsageCount,
        CreatedAt = data.CreatedAt
    };

    public static DataModels.TagDefinitionDto ToDto(this UiModels.TagDefinition ui) => new()
    {
        TagName = ui.TagName,
        Color = ui.Color,
        UsageCount = ui.UsageCount,
        CreatedAt = ui.CreatedAt
    };
}

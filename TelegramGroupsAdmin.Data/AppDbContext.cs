using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Data;

/// <summary>
/// EF Core DbContext for TelegramGroupsAdmin
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Core message tables
    public DbSet<MessageRecordDto> Messages => Set<MessageRecordDto>();
    public DbSet<MessageEditRecordDto> MessageEdits => Set<MessageEditRecordDto>();
    public DbSet<MessageTranslationDto> MessageTranslations => Set<MessageTranslationDto>();
    public DbSet<DetectionResultRecordDto> DetectionResults => Set<DetectionResultRecordDto>();

    // Views (read-only, query optimization)
    public DbSet<EnrichedMessageView> EnrichedMessages => Set<EnrichedMessageView>();
    public DbSet<EnrichedReportView> EnrichedReports => Set<EnrichedReportView>();

    // User and auth tables
    public DbSet<UserRecordDto> Users => Set<UserRecordDto>();
    public DbSet<InviteRecordDto> Invites => Set<InviteRecordDto>();
    public DbSet<RecoveryCodeRecordDto> RecoveryCodes => Set<RecoveryCodeRecordDto>();
    public DbSet<VerificationTokenDto> VerificationTokens => Set<VerificationTokenDto>();
    public DbSet<AuditLogRecordDto> AuditLogs => Set<AuditLogRecordDto>();
    public DbSet<NotificationPreferencesDto> NotificationPreferences => Set<NotificationPreferencesDto>();
    public DbSet<WebNotificationDto> WebNotifications => Set<WebNotificationDto>();

    // Telegram integration tables
    public DbSet<TelegramUserDto> TelegramUsers => Set<TelegramUserDto>();
    public DbSet<TelegramUserMappingRecordDto> TelegramUserMappings => Set<TelegramUserMappingRecordDto>();
    public DbSet<TelegramLinkTokenRecordDto> TelegramLinkTokens => Set<TelegramLinkTokenRecordDto>();
    public DbSet<ManagedChatRecordDto> ManagedChats => Set<ManagedChatRecordDto>();
    public DbSet<LinkedChannelRecordDto> LinkedChannels => Set<LinkedChannelRecordDto>();
    public DbSet<ChatAdminRecordDto> ChatAdmins => Set<ChatAdminRecordDto>();

    // User action tables
    public DbSet<UserActionRecordDto> UserActions => Set<UserActionRecordDto>();
    public DbSet<ReportDto> Reports => Set<ReportDto>();

    // Spam detection tables
    public DbSet<StopWordDto> StopWords => Set<StopWordDto>();
    // NOTE: TrainingSamples removed in Phase 2.2 - training data comes from detection_results.used_for_training
    public DbSet<TrainingLabelDto> TrainingLabels => Set<TrainingLabelDto>();
    public DbSet<ContentDetectionConfigRecordDto> ContentDetectionConfigs => Set<ContentDetectionConfigRecordDto>();
    public DbSet<PromptVersionDto> PromptVersions => Set<PromptVersionDto>();
    public DbSet<ThresholdRecommendationDto> ThresholdRecommendations => Set<ThresholdRecommendationDto>();
    public DbSet<ImageTrainingSampleDto> ImageTrainingSamples => Set<ImageTrainingSampleDto>();
    public DbSet<VideoTrainingSampleDto> VideoTrainingSamples => Set<VideoTrainingSampleDto>();

    // URL filtering tables (Phase 4.13)
    public DbSet<BlocklistSubscriptionDto> BlocklistSubscriptions => Set<BlocklistSubscriptionDto>();
    public DbSet<DomainFilterDto> DomainFilters => Set<DomainFilterDto>();
    public DbSet<CachedBlockedDomainDto> CachedBlockedDomains => Set<CachedBlockedDomainDto>();

    // Configuration table
    public DbSet<ConfigRecordDto> Configs => Set<ConfigRecordDto>();

    // Welcome system (Phase 4.4)
    public DbSet<WelcomeResponseDto> WelcomeResponses => Set<WelcomeResponseDto>();
    public DbSet<ExamSessionDto> ExamSessions => Set<ExamSessionDto>();

    // User notes and tags (Phase 4.12)
    public DbSet<AdminNoteDto> AdminNotes => Set<AdminNoteDto>();
    public DbSet<UserTagDto> UserTags => Set<UserTagDto>();
    public DbSet<TagDefinitionDto> TagDefinitions => Set<TagDefinitionDto>();

    // File scanning tables (Phase 4.17)
    public DbSet<FileScanResultRecord> FileScanResults => Set<FileScanResultRecord>();
    public DbSet<FileScanQuotaRecord> FileScanQuotas => Set<FileScanQuotaRecord>();

    // Notification tables
    public DbSet<PendingNotificationRecordDto> PendingNotifications => Set<PendingNotificationRecordDto>();
    public DbSet<PushSubscriptionDto> PushSubscriptions => Set<PushSubscriptionDto>();
    public DbSet<ReportCallbackContextDto> ReportCallbackContexts => Set<ReportCallbackContextDto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Add Quartz.NET schema (11 tables: qrtz_job_details, qrtz_triggers, etc.)
        modelBuilder.AddQuartz(builder => builder.UsePostgreSql());

        // Configure relationships
        ConfigureRelationships(modelBuilder);

        // Configure indexes
        ConfigureIndexes(modelBuilder);

        // Configure value conversions (enums, etc.)
        ConfigureValueConversions(modelBuilder);

        // Configure special entities
        ConfigureSpecialEntities(modelBuilder);
    }

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        // TelegramUsers → Warnings (JSONB embedded collection)
        // See WarningEntry class for architectural rationale (REFACTOR-5)
        modelBuilder.Entity<TelegramUserDto>()
            .OwnsMany(tu => tu.Warnings, warnings =>
            {
                warnings.ToJson("warnings"); // Use snake_case for consistency
            });

        // Messages → DetectionResults (one-to-many)
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasOne(d => d.Message)
            .WithMany(m => m.DetectionResults)
            .HasForeignKey(d => d.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure is_spam as computed column (PostgreSQL: net_confidence > 0)
        modelBuilder.Entity<DetectionResultRecordDto>()
            .Property(d => d.IsSpam)
            .HasComputedColumnSql("(net_confidence > 0)", stored: true);

        // Messages → MessageEdits (one-to-many)
        modelBuilder.Entity<MessageEditRecordDto>()
            .HasOne(e => e.Message)
            .WithMany(m => m.MessageEdits)
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messages → UserActions (one-to-many, nullable)
        modelBuilder.Entity<UserActionRecordDto>()
            .HasOne(ua => ua.Message)
            .WithMany(m => m.UserActions)
            .HasForeignKey(ua => ua.MessageId)
            .OnDelete(DeleteBehavior.SetNull);

        // Users → Invites created (one-to-many) - Creator navigation property
        modelBuilder.Entity<InviteRecordDto>()
            .HasOne(i => i.Creator)
            .WithMany()
            .HasForeignKey(i => i.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Users → Invites used (one-to-many) - UsedByUser navigation property
        modelBuilder.Entity<InviteRecordDto>()
            .HasOne(i => i.UsedByUser)
            .WithMany()
            .HasForeignKey(i => i.UsedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Users self-referencing (invited_by) - no navigation property needed, just FK
        modelBuilder.Entity<UserRecordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(u => u.InvitedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Users → TelegramUserMappings (one-to-many)
        modelBuilder.Entity<TelegramUserMappingRecordDto>()
            .HasOne(tum => tum.User)
            .WithMany(u => u.TelegramMappings)
            .HasForeignKey(tum => tum.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // TelegramUsers → TelegramUserMappings (one-to-many via TelegramId)
        // Navigation only - no FK constraint in database (just index exists)
        modelBuilder.Entity<TelegramUserMappingRecordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany(tu => tu.UserMappings)
            .HasForeignKey(tum => tum.TelegramId)
            .HasPrincipalKey(tu => tu.TelegramUserId)
            .OnDelete(DeleteBehavior.NoAction); // No cascade - we don't delete mappings when TG user deleted

        // Users → TelegramLinkTokens (one-to-many)
        modelBuilder.Entity<TelegramLinkTokenRecordDto>()
            .HasOne(tlt => tlt.User)
            .WithMany()
            .HasForeignKey(tlt => tlt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → VerificationTokens (one-to-many)
        modelBuilder.Entity<VerificationTokenDto>()
            .HasOne(vt => vt.User)
            .WithMany(u => u.VerificationTokens)
            .HasForeignKey(vt => vt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → RecoveryCodes (one-to-many)
        modelBuilder.Entity<RecoveryCodeRecordDto>()
            .HasOne(rc => rc.User)
            .WithMany(u => u.RecoveryCodes)
            .HasForeignKey(rc => rc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → Reports (one-to-many, via web_user_id)
        modelBuilder.Entity<ReportDto>()
            .HasOne(r => r.WebUser)
            .WithMany(u => u.Reports)
            .HasForeignKey(r => r.WebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ManagedChats → ChatAdmins (one-to-many)
        modelBuilder.Entity<ChatAdminRecordDto>()
            .HasOne(ca => ca.ManagedChat)
            .WithMany(mc => mc.ChatAdmins)
            .HasForeignKey(ca => ca.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        // ManagedChats → LinkedChannels (one-to-one)
        // Groups can only have one linked channel; cascade delete when chat is removed
        modelBuilder.Entity<LinkedChannelRecordDto>(entity =>
        {
            entity.HasOne(lc => lc.ManagedChat)
                .WithOne(mc => mc.LinkedChannel)
                .HasForeignKey<LinkedChannelRecordDto>(lc => lc.ManagedChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index on channel_id for lookups during impersonation checks
            entity.HasIndex(lc => lc.ChannelId);
        });

        // TelegramUsers → AdminNotes (one-to-many)
        modelBuilder.Entity<AdminNoteDto>()
            .HasOne(an => an.TelegramUser)
            .WithMany()
            .HasForeignKey(an => an.TelegramUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // TelegramUsers → UserTags (one-to-many)
        modelBuilder.Entity<UserTagDto>()
            .HasOne(ut => ut.TelegramUser)
            .WithMany()
            .HasForeignKey(ut => ut.TelegramUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================================
        // Actor System Foreign Keys (Phase 4.19)
        // Exclusive Arc pattern: Each table has 3 nullable FKs for actor attribution
        // ============================================================================

        // UserActions actor FKs
        modelBuilder.Entity<UserActionRecordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(ua => ua.WebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserActionRecordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(ua => ua.TelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // DetectionResults actor FKs
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(dr => dr.WebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(dr => dr.TelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // StopWords actor FKs
        modelBuilder.Entity<StopWordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(sw => sw.WebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<StopWordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(sw => sw.TelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // AdminNotes actor FKs
        modelBuilder.Entity<AdminNoteDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(an => an.ActorWebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AdminNoteDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(an => an.ActorTelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // UserTags actor FKs
        modelBuilder.Entity<UserTagDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(ut => ut.ActorWebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserTagDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(ut => ut.ActorTelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // AuditLog actor FKs (ARCH-2 migration)
        // CASCADE delete: When an actor is deleted, remove their audit log entries
        // (incompatible with SetNull due to CK_audit_log_exclusive_actor constraint)
        modelBuilder.Entity<AuditLogRecordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(al => al.ActorWebUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLogRecordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(al => al.ActorTelegramUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // AuditLog target FKs (ARCH-2 migration)
        // CASCADE delete: When a target is deleted, remove their audit log entries
        // (incompatible with SetNull due to CK_audit_log_exclusive_target constraint)
        modelBuilder.Entity<AuditLogRecordDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(al => al.TargetWebUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLogRecordDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(al => al.TargetTelegramUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================================
        // Actor System CHECK Constraints (Phase 4.19)
        // Enforce exclusive arc: exactly one actor column must be non-null
        // Added after data backfill to ensure constraint validity
        // ============================================================================

        // UserActions: Exactly one of (web_user_id, telegram_user_id, system_identifier) must be non-null
        modelBuilder.Entity<UserActionRecordDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_user_actions_exclusive_actor",
                "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1"));

        // DetectionResults: Exactly one actor must be non-null
        modelBuilder.Entity<DetectionResultRecordDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_detection_results_exclusive_actor",
                "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1"));

        // StopWords: Exactly one actor must be non-null
        modelBuilder.Entity<StopWordDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_stop_words_exclusive_actor",
                "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1"));

        // AdminNotes: Exactly one actor must be non-null
        modelBuilder.Entity<AdminNoteDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_admin_notes_exclusive_actor",
                "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1"));

        // UserTags: Exactly one actor must be non-null
        modelBuilder.Entity<UserTagDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_user_tags_exclusive_actor",
                "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1"));

        // AuditLog: Exactly one actor must be non-null (ARCH-2 migration)
        modelBuilder.Entity<AuditLogRecordDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_audit_log_exclusive_actor",
                "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1"));

        // AuditLog: Exactly one target must be non-null when target exists (ARCH-2 migration)
        // Note: All three can be NULL if event has no target (e.g., SystemConfigChanged)
        modelBuilder.Entity<AuditLogRecordDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_audit_log_exclusive_target",
                "(target_web_user_id IS NULL AND target_telegram_user_id IS NULL AND target_system_identifier IS NULL) OR " +
                "((target_web_user_id IS NOT NULL)::int + (target_telegram_user_id IS NOT NULL)::int + (target_system_identifier IS NOT NULL)::int = 1)"));

        // ImageTrainingSamples: Exactly one actor must be non-null
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_image_training_exclusive_actor",
                "(marked_by_web_user_id IS NOT NULL)::int + (marked_by_telegram_user_id IS NOT NULL)::int + (marked_by_system_identifier IS NOT NULL)::int = 1"));

        // MessageTranslations: Exactly one of (message_id, edit_id) must be non-null
        modelBuilder.Entity<MessageTranslationDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_message_translations_exclusive_source",
                "(message_id IS NOT NULL)::int + (edit_id IS NOT NULL)::int = 1"));

        // MessageTranslations: CASCADE delete when message or edit is deleted
        modelBuilder.Entity<MessageTranslationDto>()
            .HasOne(mt => mt.Message)
            .WithMany()
            .HasForeignKey(mt => mt.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageTranslationDto>()
            .HasOne(mt => mt.MessageEdit)
            .WithMany()
            .HasForeignKey(mt => mt.EditId)
            .OnDelete(DeleteBehavior.Cascade);

        // ImageTrainingSamples relationships
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .HasOne(its => its.Message)
            .WithMany()
            .HasForeignKey(its => its.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // VideoTrainingSamples: Exactly one actor must be non-null
        modelBuilder.Entity<VideoTrainingSampleDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_video_training_exclusive_actor",
                "(marked_by_web_user_id IS NOT NULL)::int + (marked_by_telegram_user_id IS NOT NULL)::int + (marked_by_system_identifier IS NOT NULL)::int = 1"));

        // VideoTrainingSamples relationships
        modelBuilder.Entity<VideoTrainingSampleDto>()
            .HasOne(vts => vts.Message)
            .WithMany()
            .HasForeignKey(vts => vts.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Actor System Foreign Keys (web user, telegram user, system identifier)
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(its => its.MarkedByWebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ImageTrainingSampleDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(its => its.MarkedByTelegramUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // PushSubscriptions → Users (one-to-many)
        modelBuilder.Entity<PushSubscriptionDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(ps => ps.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // WebNotifications → Users (one-to-many)
        modelBuilder.Entity<WebNotificationDto>()
            .HasOne<UserRecordDto>()
            .WithMany()
            .HasForeignKey(wn => wn.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================================
        // TrainingLabels Configuration (ML.NET SDCA Refactor)
        // Explicit spam/ham labels for ML training (separate from detection_results)
        // ============================================================================

        // TrainingLabels: label must be 0 (Spam) or 1 (Ham)
        modelBuilder.Entity<TrainingLabelDto>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_training_labels_label",
                "label IN (0, 1)"));

        // TrainingLabels → Messages (CASCADE delete when message is deleted)
        modelBuilder.Entity<TrainingLabelDto>()
            .HasOne(tl => tl.Message)
            .WithMany()
            .HasForeignKey(tl => tl.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // TrainingLabels → TelegramUsers (SET NULL when user is deleted)
        modelBuilder.Entity<TrainingLabelDto>()
            .HasOne(tl => tl.LabeledByUser)
            .WithMany()
            .HasForeignKey(tl => tl.LabeledByUserId)
            .OnDelete(DeleteBehavior.SetNull);

    }

    private static void ConfigureIndexes(ModelBuilder modelBuilder)
    {
        // Messages table indexes
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.ChatId);
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.UserId);
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.Timestamp);
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.ContentHash);
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.ReplyToMessageId);
        // Composite index for cleanup job (PERF-DATA-2: 50x faster cleanup queries)
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => new { m.Timestamp, m.DeletedAt });

        // MessageTranslations indexes (partial UNIQUE indexes for exclusive arc pattern)
        modelBuilder.Entity<MessageTranslationDto>()
            .HasIndex(mt => mt.MessageId)
            .IsUnique()
            .HasFilter("message_id IS NOT NULL");
        modelBuilder.Entity<MessageTranslationDto>()
            .HasIndex(mt => mt.EditId)
            .IsUnique()
            .HasFilter("edit_id IS NOT NULL");
        modelBuilder.Entity<MessageTranslationDto>()
            .HasIndex(mt => mt.DetectedLanguage);

        // DetectionResults indexes
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.MessageId);
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.DetectedAt);
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.UsedForTraining);

        // Performance indexes for veto queries and analytics
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.IsSpam)
            .HasDatabaseName("ix_detection_results_is_spam");
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => new { dr.IsSpam, dr.DetectedAt })
            .HasDatabaseName("ix_detection_results_is_spam_detected_at");
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.DetectionSource)
            .HasDatabaseName("ix_detection_results_detection_source");

        // GIN index for JSONB check_results_json column (ML-5 performance analytics)
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.CheckResultsJson)
            .HasMethod("gin")
            .HasDatabaseName("ix_detection_results_check_results_json_gin");

        // TrainingLabels indexes (ML.NET SDCA Refactor)
        modelBuilder.Entity<TrainingLabelDto>()
            .HasIndex(tl => tl.Label);
        modelBuilder.Entity<TrainingLabelDto>()
            .HasIndex(tl => new { tl.Label, tl.LabeledAt });

        // UserActions indexes
        modelBuilder.Entity<UserActionRecordDto>()
            .HasIndex(ua => ua.UserId);
        modelBuilder.Entity<UserActionRecordDto>()
            .HasIndex(ua => ua.IssuedAt);

        // Users table indexes
        modelBuilder.Entity<UserRecordDto>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique();

        // TelegramUsers indexes
        modelBuilder.Entity<TelegramUserDto>()
            .HasIndex(tu => tu.Username);
        modelBuilder.Entity<TelegramUserDto>()
            .HasIndex(tu => tu.IsTrusted);
        modelBuilder.Entity<TelegramUserDto>()
            .HasIndex(tu => tu.IsBanned);
        modelBuilder.Entity<TelegramUserDto>()
            .HasIndex(tu => tu.LastSeenAt);
        modelBuilder.Entity<TelegramUserDto>()
            .HasIndex(tu => tu.IsActive)
            .HasFilter("is_active = false")
            .HasDatabaseName("ix_telegram_users_is_active"); // Partial index for inactive users (UI filtering)

        // TelegramUserMappings indexes
        modelBuilder.Entity<TelegramUserMappingRecordDto>()
            .HasIndex(tum => tum.TelegramId)
            .IsUnique();

        // ChatAdmins indexes (already has PK on id, add indexes for queries)
        modelBuilder.Entity<ChatAdminRecordDto>()
            .HasIndex(ca => ca.ChatId);
        modelBuilder.Entity<ChatAdminRecordDto>()
            .HasIndex(ca => ca.TelegramId);

        // AdminNotes indexes
        modelBuilder.Entity<AdminNoteDto>()
            .HasIndex(an => an.TelegramUserId);
        modelBuilder.Entity<AdminNoteDto>()
            .HasIndex(an => an.CreatedAt);
        modelBuilder.Entity<AdminNoteDto>()
            .HasIndex(an => an.IsPinned);

        // UserTags indexes
        modelBuilder.Entity<UserTagDto>()
            .HasIndex(ut => ut.TelegramUserId);
        modelBuilder.Entity<UserTagDto>()
            .HasIndex(ut => ut.TagName);
        modelBuilder.Entity<UserTagDto>()
            .HasIndex(ut => ut.RemovedAt);

        // TagDefinitions indexes
        modelBuilder.Entity<TagDefinitionDto>()
            .HasIndex(td => td.UsageCount);

        // URL Filtering indexes (Phase 4.13)
        // BlocklistSubscriptions indexes
        modelBuilder.Entity<BlocklistSubscriptionDto>()
            .HasIndex(bs => bs.ChatId);
        modelBuilder.Entity<BlocklistSubscriptionDto>()
            .HasIndex(bs => bs.Enabled)
            .HasFilter("enabled = true");
        modelBuilder.Entity<BlocklistSubscriptionDto>()
            .HasIndex(bs => bs.BlockMode)
            .HasFilter("block_mode > 0");
        modelBuilder.Entity<BlocklistSubscriptionDto>()
            .HasIndex(bs => bs.Url);  // For duplicate detection

        // DomainFilters indexes
        modelBuilder.Entity<DomainFilterDto>()
            .HasIndex(df => df.ChatId);
        modelBuilder.Entity<DomainFilterDto>()
            .HasIndex(df => df.Domain);
        modelBuilder.Entity<DomainFilterDto>()
            .HasIndex(df => new { df.FilterType, df.BlockMode })
            .HasFilter("enabled = true");

        // CachedBlockedDomains indexes (CRITICAL for lookup performance)
        modelBuilder.Entity<CachedBlockedDomainDto>()
            .HasIndex(cbd => new { cbd.Domain, cbd.BlockMode, cbd.ChatId });  // Primary lookup index
        modelBuilder.Entity<CachedBlockedDomainDto>()
            .HasIndex(cbd => cbd.SourceSubscriptionId);  // For cleanup when subscription disabled
        modelBuilder.Entity<CachedBlockedDomainDto>()
            .HasIndex(cbd => cbd.LastVerified);  // For finding stale entries

        // Unique constraint on cached_blocked_domains to prevent duplicates
        modelBuilder.Entity<CachedBlockedDomainDto>()
            .HasIndex(cbd => new { cbd.Domain, cbd.BlockMode, cbd.ChatId })
            .IsUnique();

        // File scanning indexes (Phase 4.17)
        // FileScanResults indexes - hash lookup for caching
        modelBuilder.Entity<FileScanResultRecord>()
            .HasIndex(fsr => fsr.FileHash);  // Primary cache lookup
        modelBuilder.Entity<FileScanResultRecord>()
            .HasIndex(fsr => new { fsr.Scanner, fsr.ScannedAt });  // Analytics queries

        // FileScanQuota indexes - service + window queries for quota tracking
        modelBuilder.Entity<FileScanQuotaRecord>()
            .HasIndex(fsq => new { fsq.Service, fsq.QuotaType, fsq.QuotaWindowEnd });  // Find active quotas

        // Unique constraint on file_scan_quota to prevent overlapping quota windows for same service/type
        modelBuilder.Entity<FileScanQuotaRecord>()
            .HasIndex(fsq => new { fsq.Service, fsq.QuotaType, fsq.QuotaWindowStart })
            .IsUnique();

        // PendingNotifications indexes - lookup by user for delivery
        modelBuilder.Entity<PendingNotificationRecordDto>()
            .HasIndex(pn => pn.TelegramUserId);

        // Index for cleanup job (find expired notifications)
        modelBuilder.Entity<PendingNotificationRecordDto>()
            .HasIndex(pn => pn.ExpiresAt);

        // Index for analytics (notifications by type)
        modelBuilder.Entity<PendingNotificationRecordDto>()
            .HasIndex(pn => new { pn.NotificationType, pn.CreatedAt });

        // ThresholdRecommendations indexes
        modelBuilder.Entity<ThresholdRecommendationDto>()
            .HasIndex(tr => tr.Status);  // Filter by status (pending, applied, rejected)
        modelBuilder.Entity<ThresholdRecommendationDto>()
            .HasIndex(tr => tr.CreatedAt);  // Sort by date
        modelBuilder.Entity<ThresholdRecommendationDto>()
            .HasIndex(tr => new { tr.AlgorithmName, tr.Status });  // Filter by algorithm + status

        // ImageTrainingSamples indexes
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .HasIndex(its => its.MessageId)
            .IsUnique();  // One training sample per message
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .HasIndex(its => new { its.IsSpam, its.MarkedAt });  // Filter spam/ham + sort by date

        // VideoTrainingSamples indexes
        modelBuilder.Entity<VideoTrainingSampleDto>()
            .HasIndex(vts => vts.MessageId)
            .IsUnique();  // One training sample per message
        modelBuilder.Entity<VideoTrainingSampleDto>()
            .HasIndex(vts => new { vts.IsSpam, vts.MarkedAt });  // Filter spam/ham + sort by date
        // Note: No index on photo_hash - Hamming distance similarity requires full table scan anyway
        // Note: No simple is_spam index - low cardinality boolean, sequential scan is faster

        // PushSubscriptions indexes (Web Push API browser subscriptions)
        modelBuilder.Entity<PushSubscriptionDto>()
            .HasIndex(ps => ps.UserId);  // Get all subscriptions for a user
        modelBuilder.Entity<PushSubscriptionDto>()
            .HasIndex(ps => new { ps.UserId, ps.Endpoint })
            .IsUnique();  // Prevent duplicate subscriptions for same endpoint

        // WebNotifications indexes (in-app notification bell)
        modelBuilder.Entity<WebNotificationDto>()
            .HasIndex(wn => new { wn.UserId, wn.CreatedAt })
            .HasDatabaseName("ix_web_notifications_user_id_created_at");  // Primary query: recent notifications by user

        // SimHash indexes for O(1) training data deduplication
        // Partial indexes: only index rows with computed hashes (skip empty/null text)
        modelBuilder.Entity<MessageRecordDto>()
            .HasIndex(m => m.SimilarityHash)
            .HasFilter("similarity_hash IS NOT NULL")
            .HasDatabaseName("ix_messages_similarity_hash");

        modelBuilder.Entity<MessageTranslationDto>()
            .HasIndex(mt => mt.SimilarityHash)
            .HasFilter("similarity_hash IS NOT NULL")
            .HasDatabaseName("ix_message_translations_similarity_hash");

        // ReportCallbackContexts indexes (DM action button contexts)
        modelBuilder.Entity<ReportCallbackContextDto>()
            .HasIndex(rcc => rcc.ReportId)
            .HasDatabaseName("ix_report_callback_contexts_report_id");  // Cleanup when report is handled
        modelBuilder.Entity<ReportCallbackContextDto>()
            .HasIndex(rcc => rcc.CreatedAt)
            .HasDatabaseName("ix_report_callback_contexts_created_at");  // Expiry cleanup job
    }

    private static void ConfigureValueConversions(ModelBuilder modelBuilder)
    {
        // Store enums as integers in database
        modelBuilder.Entity<UserRecordDto>()
            .Property(u => u.Status)
            .HasConversion<int>();

        modelBuilder.Entity<InviteRecordDto>()
            .Property(i => i.Status)
            .HasConversion<int>();

        modelBuilder.Entity<InviteRecordDto>()
            .Property(i => i.PermissionLevel)
            .HasConversion<int>();

        modelBuilder.Entity<UserRecordDto>()
            .Property(u => u.PermissionLevel)
            .HasConversion<int>();

        modelBuilder.Entity<ManagedChatRecordDto>()
            .Property(mc => mc.BotStatus)
            .HasConversion<int>();

        modelBuilder.Entity<ManagedChatRecordDto>()
            .Property(mc => mc.ChatType)
            .HasConversion<int>();

        modelBuilder.Entity<UserActionRecordDto>()
            .Property(ua => ua.ActionType)
            .HasConversion<int>();

        modelBuilder.Entity<ReportDto>()
            .Property(r => r.Type)
            .HasDefaultValue((short)0);

        modelBuilder.Entity<ReportDto>()
            .Property(r => r.Context)
            .HasColumnType("jsonb");

        // Partial unique index: Only ONE pending ContentReport per message (prevents duplicate reports)
        // ExamFailures and ImpersonationAlerts don't have message IDs, so exclude them
        modelBuilder.Entity<ReportDto>()
            .HasIndex(r => new { r.MessageId, r.ChatId })
            .HasFilter("status = 0 AND type = 0")
            .IsUnique()
            .HasDatabaseName("IX_reports_unique_pending_per_message");

        // Index for filtering by report type
        modelBuilder.Entity<ReportDto>()
            .HasIndex(r => r.Type)
            .HasDatabaseName("IX_reports_type");

        modelBuilder.Entity<AuditLogRecordDto>()
            .Property(al => al.EventType)
            .HasConversion<int>();

        modelBuilder.Entity<TagDefinitionDto>()
            .Property(td => td.Color)
            .HasConversion<int>();

        modelBuilder.Entity<WelcomeResponseDto>()
            .Property(w => w.Response)
            .HasConversion<int>();

        // WelcomeResponseDto index for job cancellation queries
        modelBuilder.Entity<WelcomeResponseDto>()
            .HasIndex(w => w.TimeoutJobId)
            .HasFilter("timeout_job_id IS NOT NULL"); // Partial index for active jobs only

        // WelcomeResponseDto FK to TelegramUserDto (user must exist after backfill migration)
        modelBuilder.Entity<WelcomeResponseDto>()
            .HasOne<TelegramUserDto>()
            .WithMany()
            .HasForeignKey(wr => wr.UserId)
            .HasPrincipalKey(tu => tu.TelegramUserId)
            .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete users when welcome response deleted

        // ExamSessionDto configuration (entrance exam tracking)
        modelBuilder.Entity<ExamSessionDto>(entity =>
        {
            entity.Property(e => e.CurrentQuestionIndex).HasDefaultValue((short)0);
            entity.Property(e => e.McAnswers).HasColumnType("jsonb");
            entity.Property(e => e.ShuffleState).HasColumnType("jsonb");
            entity.Property(e => e.StartedAt).HasDefaultValueSql("now()");

            // Unique constraint: one active session per user per chat
            entity.HasIndex(e => new { e.ChatId, e.UserId }).IsUnique();

            // Index for cleanup job to find expired sessions
            entity.HasIndex(e => e.ExpiresAt);
        });

        // VerificationTokenDto stores token_type as string in DB but exposes as enum
        // The entity already handles this with TokenTypeString property
    }

    private static void ConfigureSpecialEntities(ModelBuilder modelBuilder)
    {
        // Messages: Set database default for content_check_skip_reason (0 = NotSkipped)
        // Required for raw SQL inserts in tests and data migrations
        modelBuilder.Entity<MessageRecordDto>()
            .Property(m => m.ContentCheckSkipReason)
            .HasDefaultValue(ContentCheckSkipReason.NotSkipped);

        // TelegramUsers: Set database defaults for boolean columns
        // Required for raw SQL inserts in tests and data migrations
        modelBuilder.Entity<TelegramUserDto>()
            .Property(u => u.IsBot)
            .HasDefaultValue(false);
        modelBuilder.Entity<TelegramUserDto>()
            .Property(u => u.IsActive)
            .HasDefaultValue(false);
        modelBuilder.Entity<TelegramUserDto>()
            .Property(u => u.IsBanned)
            .HasDefaultValue(false);

        // Users (web users): Set database defaults for columns added in later migrations
        modelBuilder.Entity<UserRecordDto>()
            .Property(u => u.IsActive)
            .HasDefaultValue(true);
        modelBuilder.Entity<UserRecordDto>()
            .Property(u => u.FailedLoginAttempts)
            .HasDefaultValue(0);

        // ManagedChats: Set database defaults for boolean columns
        modelBuilder.Entity<ManagedChatRecordDto>()
            .Property(c => c.IsDeleted)
            .HasDefaultValue(false);

        // Configure configs table - id is PK, chat_id = 0 for global config
        modelBuilder.Entity<ConfigRecordDto>()
            .HasKey(c => c.Id);

        // Set database default for created_at (dynamic, set by PostgreSQL)
        modelBuilder.Entity<ConfigRecordDto>()
            .Property(c => c.CreatedAt)
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd();

        // Set default value for chat_id (0 = global config)
        modelBuilder.Entity<ConfigRecordDto>()
            .Property(c => c.ChatId)
            .HasDefaultValue(0L);

        // Partial unique index: Only ONE global config allowed (chat_id = 0)
        modelBuilder.Entity<ConfigRecordDto>()
            .HasIndex(c => c.ChatId)
            .HasFilter("chat_id = 0")
            .IsUnique()
            .HasDatabaseName("idx_configs_single_global");

        // Partial unique index: Each chat can have only ONE chat-specific config
        modelBuilder.Entity<ConfigRecordDto>()
            .HasIndex(c => c.ChatId)
            .HasFilter("chat_id != 0")
            .IsUnique()
            .HasDatabaseName("idx_configs_chat_specific");

        // Configure image_training_samples table
        modelBuilder.Entity<ImageTrainingSampleDto>()
            .ToTable("image_training_samples");

        // Configure video_training_samples table
        modelBuilder.Entity<VideoTrainingSampleDto>()
            .ToTable("video_training_samples");

        // Configure RawAlgorithmPerformanceStatsDto as keyless entity for SqlQuery support (Phase 5)
        // This is a query-only DTO for algorithm performance analytics
        // Not mapped to any table/view - used only for raw SQL query results
        modelBuilder.Entity<RawAlgorithmPerformanceStatsDto>()
            .HasNoKey()
            .ToView(null);

        // Configure EnrichedMessageView as keyless entity mapping to enriched_messages view
        // Provides message enrichment (user, chat, reply, translation) for efficient queries
        modelBuilder.Entity<EnrichedMessageView>()
            .HasNoKey()
            .ToView("enriched_messages");

        // Configure EnrichedReportView as keyless entity mapping to enriched_reports view
        // Provides report enrichment (users from JSONB, chat, reviewer) for efficient queries
        modelBuilder.Entity<EnrichedReportView>()
            .HasNoKey()
            .ToView("enriched_reports");

        // ============================================================================
        // Content Detection Config JSON Mapping (Issue #252)
        // Uses EF Core OwnsOne().ToJson() for strongly-typed JSONB column mapping
        // ============================================================================
        modelBuilder.Entity<ContentDetectionConfigRecordDto>()
            .OwnsOne(c => c.Config, config =>
            {
                config.ToJson("config_json");

                // Map all nested sub-configs
                config.OwnsOne(x => x.StopWords);
                config.OwnsOne(x => x.Similarity);
                config.OwnsOne(x => x.Cas);
                config.OwnsOne(x => x.Bayes);
                config.OwnsOne(x => x.InvisibleChars);
                config.OwnsOne(x => x.Translation);
                config.OwnsOne(x => x.Spacing);
                config.OwnsOne(x => x.AIVeto);
                config.OwnsOne(x => x.UrlBlocklist);
                config.OwnsOne(x => x.ThreatIntel);
                config.OwnsOne(x => x.SeoScraping);
                config.OwnsOne(x => x.ImageSpam);
                config.OwnsOne(x => x.VideoSpam);
                config.OwnsOne(x => x.FileScanning);
            });

        // Partial unique index: Only ONE global content detection config (chat_id IS NULL)
        modelBuilder.Entity<ContentDetectionConfigRecordDto>()
            .HasIndex(c => c.ChatId)
            .HasFilter("chat_id IS NULL")
            .IsUnique()
            .HasDatabaseName("idx_content_detection_configs_global");

        // Partial unique index: Each chat can have only ONE content detection config
        modelBuilder.Entity<ContentDetectionConfigRecordDto>()
            .HasIndex(c => c.ChatId)
            .HasFilter("chat_id IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("idx_content_detection_configs_chat");
    }
}

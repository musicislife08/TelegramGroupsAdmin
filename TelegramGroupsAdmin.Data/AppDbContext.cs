using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;

namespace TelegramGroupsAdmin.Data;

/// <summary>
/// EF Core DbContext for TelegramGroupsAdmin
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Core message tables
    public DbSet<MessageRecordDto> Messages => Set<MessageRecordDto>();
    public DbSet<MessageEditRecordDto> MessageEdits => Set<MessageEditRecordDto>();
    public DbSet<DetectionResultRecordDto> DetectionResults => Set<DetectionResultRecordDto>();

    // User and auth tables
    public DbSet<UserRecordDto> Users => Set<UserRecordDto>();
    public DbSet<InviteRecordDto> Invites => Set<InviteRecordDto>();
    public DbSet<RecoveryCodeRecordDto> RecoveryCodes => Set<RecoveryCodeRecordDto>();
    public DbSet<VerificationTokenDto> VerificationTokens => Set<VerificationTokenDto>();
    public DbSet<AuditLogRecordDto> AuditLogs => Set<AuditLogRecordDto>();

    // Telegram integration tables
    public DbSet<TelegramUserDto> TelegramUsers => Set<TelegramUserDto>();
    public DbSet<TelegramUserMappingRecordDto> TelegramUserMappings => Set<TelegramUserMappingRecordDto>();
    public DbSet<TelegramLinkTokenRecordDto> TelegramLinkTokens => Set<TelegramLinkTokenRecordDto>();
    public DbSet<ManagedChatRecordDto> ManagedChats => Set<ManagedChatRecordDto>();
    public DbSet<ChatAdminRecordDto> ChatAdmins => Set<ChatAdminRecordDto>();
    public DbSet<ChatPromptRecordDto> ChatPrompts => Set<ChatPromptRecordDto>();

    // User action tables
    public DbSet<UserActionRecordDto> UserActions => Set<UserActionRecordDto>();
    public DbSet<ReportDto> Reports => Set<ReportDto>();
    public DbSet<ImpersonationAlertRecordDto> ImpersonationAlerts => Set<ImpersonationAlertRecordDto>();

    // Spam detection tables
    public DbSet<StopWordDto> StopWords => Set<StopWordDto>();
    // NOTE: TrainingSamples removed in Phase 2.2 - training data comes from detection_results.used_for_training
    public DbSet<SpamDetectionConfigRecordDto> SpamDetectionConfigs => Set<SpamDetectionConfigRecordDto>();
    public DbSet<SpamCheckConfigRecordDto> SpamCheckConfigs => Set<SpamCheckConfigRecordDto>();

    // Configuration table
    public DbSet<ConfigRecordDto> Configs => Set<ConfigRecordDto>();

    // Welcome system (Phase 4.4)
    public DbSet<WelcomeResponseDto> WelcomeResponses => Set<WelcomeResponseDto>();

    // User notes and tags (Phase 4.12)
    public DbSet<AdminNoteDto> AdminNotes => Set<AdminNoteDto>();
    public DbSet<UserTagDto> UserTags => Set<UserTagDto>();
    public DbSet<TagDefinitionDto> TagDefinitions => Set<TagDefinitionDto>();

    // TickerQ entities (background job system)
    public DbSet<TimeTickerEntity> TimeTickers => Set<TimeTickerEntity>();
    public DbSet<CronTickerEntity> CronTickers => Set<CronTickerEntity>();
    public DbSet<CronTickerOccurrenceEntity<CronTickerEntity>> CronTickerOccurrences => Set<CronTickerOccurrenceEntity<CronTickerEntity>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure composite keys
        ConfigureCompositeKeys(modelBuilder);

        // Configure relationships
        ConfigureRelationships(modelBuilder);

        // Configure indexes
        ConfigureIndexes(modelBuilder);

        // Configure value conversions (enums, etc.)
        ConfigureValueConversions(modelBuilder);

        // Configure special entities
        ConfigureSpecialEntities(modelBuilder);

        // Apply TickerQ entity configurations (needed for migrations)
        // Default schema is "ticker"
        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations());
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations());
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations());
    }

    private static void ConfigureCompositeKeys(ModelBuilder modelBuilder)
    {
        // chat_admins uses regular PK (id), has unique constraint on (chat_id, telegram_id)
        // No composite key configuration needed - handled by database constraint
    }

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
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

        // Impersonation Alerts relationships
        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasOne(ia => ia.SuspectedUser)
            .WithMany()
            .HasForeignKey(ia => ia.SuspectedUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasOne(ia => ia.TargetUser)
            .WithMany()
            .HasForeignKey(ia => ia.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasOne(ia => ia.ReviewedBy)
            .WithMany()
            .HasForeignKey(ia => ia.ReviewedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

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

        // DetectionResults indexes
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.MessageId);
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.DetectedAt);
        modelBuilder.Entity<DetectionResultRecordDto>()
            .HasIndex(dr => dr.UsedForTraining);

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
            .HasIndex(tu => tu.LastSeenAt);

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

        // ImpersonationAlerts indexes
        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasIndex(ia => new { ia.RiskLevel, ia.DetectedAt })
            .HasFilter("reviewed_at IS NULL");  // Pending alerts only
        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasIndex(ia => ia.ChatId);
        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .HasIndex(ia => ia.SuspectedUserId);
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
            .Property(r => r.Status)
            .HasConversion<int>();

        modelBuilder.Entity<AuditLogRecordDto>()
            .Property(al => al.EventType)
            .HasConversion<int>();

        modelBuilder.Entity<TagDefinitionDto>()
            .Property(td => td.Color)
            .HasConversion<int>();

        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .Property(ia => ia.RiskLevel)
            .HasConversion<int>();

        modelBuilder.Entity<ImpersonationAlertRecordDto>()
            .Property(ia => ia.Verdict)
            .HasConversion<int>();

        // VerificationTokenDto stores token_type as string in DB but exposes as enum
        // The entity already handles this with TokenTypeString property
    }

    private static void ConfigureSpecialEntities(ModelBuilder modelBuilder)
    {
        // Configure configs table - id is PK, chat_id is nullable (NULL = global config)
        modelBuilder.Entity<ConfigRecordDto>()
            .HasKey(c => c.Id);

        // Set database default for created_at (dynamic, set by PostgreSQL)
        modelBuilder.Entity<ConfigRecordDto>()
            .Property(c => c.CreatedAt)
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd();

        // Create unique index on chat_id (allows one NULL for global config)
        modelBuilder.Entity<ConfigRecordDto>()
            .HasIndex(c => c.ChatId)
            .IsUnique();
    }
}

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
    public DbSet<TelegramUserMappingRecordDto> TelegramUserMappings => Set<TelegramUserMappingRecordDto>();
    public DbSet<TelegramLinkTokenRecordDto> TelegramLinkTokens => Set<TelegramLinkTokenRecordDto>();
    public DbSet<ManagedChatRecordDto> ManagedChats => Set<ManagedChatRecordDto>();
    public DbSet<ChatAdminRecordDto> ChatAdmins => Set<ChatAdminRecordDto>();
    public DbSet<ChatPromptRecordDto> ChatPrompts => Set<ChatPromptRecordDto>();

    // User action tables
    public DbSet<UserActionRecordDto> UserActions => Set<UserActionRecordDto>();
    public DbSet<ReportDto> Reports => Set<ReportDto>();

    // Spam detection tables
    public DbSet<StopWordDto> StopWords => Set<StopWordDto>();
    public DbSet<TrainingSampleDto> TrainingSamples => Set<TrainingSampleDto>();
    public DbSet<SpamDetectionConfigRecordDto> SpamDetectionConfigs => Set<SpamDetectionConfigRecordDto>();
    public DbSet<SpamCheckConfigRecordDto> SpamCheckConfigs => Set<SpamCheckConfigRecordDto>();

    // Configuration table
    public DbSet<ConfigRecordDto> Configs => Set<ConfigRecordDto>();

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

        // TelegramUserMappings indexes
        modelBuilder.Entity<TelegramUserMappingRecordDto>()
            .HasIndex(tum => tum.TelegramId)
            .IsUnique();

        // ChatAdmins indexes (already has PK on id, add indexes for queries)
        modelBuilder.Entity<ChatAdminRecordDto>()
            .HasIndex(ca => ca.ChatId);
        modelBuilder.Entity<ChatAdminRecordDto>()
            .HasIndex(ca => ca.TelegramId);
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

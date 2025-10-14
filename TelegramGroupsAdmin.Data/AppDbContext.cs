using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data.Models;

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
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<MessageEditRecord> MessageEdits => Set<MessageEditRecord>();
    public DbSet<DetectionResultRecord> DetectionResults => Set<DetectionResultRecord>();

    // User and auth tables
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<InviteRecord> Invites => Set<InviteRecord>();
    public DbSet<RecoveryCodeRecord> RecoveryCodes => Set<RecoveryCodeRecord>();
    public DbSet<VerificationToken> VerificationTokens => Set<VerificationToken>();
    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    // Telegram integration tables
    public DbSet<TelegramUserMappingRecord> TelegramUserMappings => Set<TelegramUserMappingRecord>();
    public DbSet<TelegramLinkTokenRecord> TelegramLinkTokens => Set<TelegramLinkTokenRecord>();
    public DbSet<ManagedChatRecord> ManagedChats => Set<ManagedChatRecord>();
    public DbSet<ChatAdminRecord> ChatAdmins => Set<ChatAdminRecord>();
    public DbSet<ChatPromptRecord> ChatPrompts => Set<ChatPromptRecord>();

    // User action tables
    public DbSet<UserActionRecord> UserActions => Set<UserActionRecord>();
    public DbSet<Report> Reports => Set<Report>();

    // Spam detection tables
    public DbSet<StopWord> StopWords => Set<StopWord>();
    public DbSet<TrainingSample> TrainingSamples => Set<TrainingSample>();
    public DbSet<SpamDetectionConfigRecord> SpamDetectionConfigs => Set<SpamDetectionConfigRecord>();
    public DbSet<SpamCheckConfigRecord> SpamCheckConfigs => Set<SpamCheckConfigRecord>();

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
    }

    private static void ConfigureCompositeKeys(ModelBuilder modelBuilder)
    {
        // chat_admins uses regular PK (id), has unique constraint on (chat_id, telegram_id)
        // No composite key configuration needed - handled by database constraint
    }

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        // Messages → DetectionResults (one-to-many)
        modelBuilder.Entity<DetectionResultRecord>()
            .HasOne(d => d.Message)
            .WithMany(m => m.DetectionResults)
            .HasForeignKey(d => d.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messages → MessageEdits (one-to-many)
        modelBuilder.Entity<MessageEditRecord>()
            .HasOne(e => e.Message)
            .WithMany(m => m.MessageEdits)
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messages → UserActions (one-to-many, nullable)
        modelBuilder.Entity<UserActionRecord>()
            .HasOne(ua => ua.Message)
            .WithMany(m => m.UserActions)
            .HasForeignKey(ua => ua.MessageId)
            .OnDelete(DeleteBehavior.SetNull);

        // Users → Invites created (one-to-many) - no navigation property needed, just FK
        modelBuilder.Entity<InviteRecord>()
            .HasOne<UserRecord>()
            .WithMany()
            .HasForeignKey(i => i.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Users → Invites used (one-to-many) - no navigation property needed, just FK
        modelBuilder.Entity<InviteRecord>()
            .HasOne<UserRecord>()
            .WithMany()
            .HasForeignKey(i => i.UsedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Users self-referencing (invited_by) - no navigation property needed, just FK
        modelBuilder.Entity<UserRecord>()
            .HasOne<UserRecord>()
            .WithMany()
            .HasForeignKey(u => u.InvitedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Users → TelegramUserMappings (one-to-many)
        modelBuilder.Entity<TelegramUserMappingRecord>()
            .HasOne(tum => tum.User)
            .WithMany(u => u.TelegramMappings)
            .HasForeignKey(tum => tum.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → TelegramLinkTokens (one-to-many)
        modelBuilder.Entity<TelegramLinkTokenRecord>()
            .HasOne(tlt => tlt.User)
            .WithMany()
            .HasForeignKey(tlt => tlt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → VerificationTokens (one-to-many)
        modelBuilder.Entity<VerificationToken>()
            .HasOne(vt => vt.User)
            .WithMany(u => u.VerificationTokens)
            .HasForeignKey(vt => vt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → RecoveryCodes (one-to-many)
        modelBuilder.Entity<RecoveryCodeRecord>()
            .HasOne(rc => rc.User)
            .WithMany(u => u.RecoveryCodes)
            .HasForeignKey(rc => rc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Users → Reports (one-to-many, via web_user_id)
        modelBuilder.Entity<Report>()
            .HasOne(r => r.WebUser)
            .WithMany(u => u.Reports)
            .HasForeignKey(r => r.WebUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ManagedChats → ChatAdmins (one-to-many)
        modelBuilder.Entity<ChatAdminRecord>()
            .HasOne(ca => ca.ManagedChat)
            .WithMany(mc => mc.ChatAdmins)
            .HasForeignKey(ca => ca.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureIndexes(ModelBuilder modelBuilder)
    {
        // Messages table indexes
        modelBuilder.Entity<MessageRecord>()
            .HasIndex(m => m.ChatId);
        modelBuilder.Entity<MessageRecord>()
            .HasIndex(m => m.UserId);
        modelBuilder.Entity<MessageRecord>()
            .HasIndex(m => m.Timestamp);
        modelBuilder.Entity<MessageRecord>()
            .HasIndex(m => m.ContentHash);

        // DetectionResults indexes
        modelBuilder.Entity<DetectionResultRecord>()
            .HasIndex(dr => dr.MessageId);
        modelBuilder.Entity<DetectionResultRecord>()
            .HasIndex(dr => dr.DetectedAt);
        modelBuilder.Entity<DetectionResultRecord>()
            .HasIndex(dr => dr.UsedForTraining);

        // UserActions indexes
        modelBuilder.Entity<UserActionRecord>()
            .HasIndex(ua => ua.UserId);
        modelBuilder.Entity<UserActionRecord>()
            .HasIndex(ua => ua.IssuedAt);

        // Users table indexes
        modelBuilder.Entity<UserRecord>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique();

        // TelegramUserMappings indexes
        modelBuilder.Entity<TelegramUserMappingRecord>()
            .HasIndex(tum => tum.TelegramId)
            .IsUnique();

        // ChatAdmins indexes (already has PK on id, add indexes for queries)
        modelBuilder.Entity<ChatAdminRecord>()
            .HasIndex(ca => ca.ChatId);
        modelBuilder.Entity<ChatAdminRecord>()
            .HasIndex(ca => ca.TelegramId);
    }

    private static void ConfigureValueConversions(ModelBuilder modelBuilder)
    {
        // Store enums as integers in database
        modelBuilder.Entity<UserRecord>()
            .Property(u => u.Status)
            .HasConversion<int>();

        modelBuilder.Entity<InviteRecord>()
            .Property(i => i.Status)
            .HasConversion<int>();

        modelBuilder.Entity<InviteRecord>()
            .Property(i => i.PermissionLevel)
            .HasConversion<int>();

        modelBuilder.Entity<UserRecord>()
            .Property(u => u.PermissionLevel)
            .HasConversion<int>();

        modelBuilder.Entity<ManagedChatRecord>()
            .Property(mc => mc.BotStatus)
            .HasConversion<int>();

        modelBuilder.Entity<ManagedChatRecord>()
            .Property(mc => mc.ChatType)
            .HasConversion<int>();

        modelBuilder.Entity<UserActionRecord>()
            .Property(ua => ua.ActionType)
            .HasConversion<int>();

        modelBuilder.Entity<Report>()
            .Property(r => r.Status)
            .HasConversion<int>();

        modelBuilder.Entity<AuditLogRecord>()
            .Property(al => al.EventType)
            .HasConversion<int>();

        // VerificationToken stores token_type as string in DB but exposes as enum
        // The entity already handles this with TokenTypeString property
    }
}

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Integration tests for AuditHandler.
/// Tests FK constraint behavior for user_actions.user_id → telegram_users.telegram_user_id.
/// Uses Testcontainers PostgreSQL for realistic database constraint validation.
/// </summary>
/// <remarks>
/// BACKGROUND: These tests were added after an E2E test failure revealed that LogDeleteAsync
/// was creating audit records with userId=0, causing FK constraint violations.
/// See: session where E2E test "ModerationReport_DeleteAsSpam_ProcessesImmediately" failed.
/// </remarks>
[TestFixture]
public class AuditHandlerTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    // Test constants
    private const long ValidUserId = 123456789L;
    private const long NonExistentUserId = 999999999L;
    private const long ChatId = -1001234567890L;
    private const long TestMessageId = 99999L;  // Used in tests expecting FK failures

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Configure Data Protection with ephemeral keys
        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        // Register repositories and handlers
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IAuditHandler, AuditHandler>();

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper method to create a test user directly in the database (satisfies FK constraint).
    /// </summary>
    private async Task CreateTestUserAsync(long userId)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = new Data.Models.TelegramUserDto
        {
            TelegramUserId = userId,
            Username = $"testuser{userId}",
            FirstName = "Test",
            LastName = "User",
            IsBot = false,
            IsTrusted = false,
            BotDmEnabled = false,
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-7),
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.TelegramUsers.Add(user);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Helper method to create a test message directly in the database (satisfies FK constraint for user_actions.message_id).
    /// </summary>
    private async Task<long> CreateTestMessageAsync(long chatId, long userId)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var message = new Data.Models.MessageRecordDto
        {
            ChatId = chatId,
            UserId = userId,
            MessageText = "Test message",
            Timestamp = DateTimeOffset.UtcNow
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        return message.MessageId;
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region LogDeleteAsync FK Constraint Tests

    [Test]
    public async Task LogDeleteAsync_WithValidUserId_InsertsSuccessfully()
    {
        // Arrange - Create a telegram_user and message first (satisfies FK constraints)
        await CreateTestUserAsync(ValidUserId);
        var messageId = await CreateTestMessageAsync(ChatId, ValidUserId);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act - Log deletion for that user (create new scope)
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatId, ValidUserId, executor);
        }

        // Assert - Verify record was inserted with FK intact
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.MessageId == messageId && ua.UserId == ValidUserId)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null, "Audit record should be inserted");
        Assert.That(record!.UserId, Is.EqualTo(ValidUserId));
        Assert.That(record.MessageId, Is.EqualTo(messageId));
        Assert.That(record.ActionType, Is.EqualTo(Data.Models.UserActionType.Delete));
        Assert.That(record.SystemIdentifier, Is.EqualTo("IntegrationTest"));
    }

    [Test]
    public void LogDeleteAsync_WithNonExistentUserId_ThrowsDbUpdateException()
    {
        // Arrange - Don't create a telegram_user (FK constraint will fail)
        var executor = Actor.FromSystem("IntegrationTest");

        // Act & Assert - Should throw DbUpdateException due to FK constraint violation
        var ex = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(TestMessageId, ChatId, NonExistentUserId, executor);
        });

        // Verify it's specifically an FK constraint violation
        Assert.That(ex!.InnerException?.Message, Does.Contain("foreign key").Or.Contain("violates").IgnoreCase,
            "Exception should mention foreign key constraint violation");
    }

    [Test]
    public async Task LogDeleteAsync_WithServiceAccountUserId_WorksIfUserExists()
    {
        // Arrange - Create the service account user (ID 0 is special, but still needs to exist)
        // NOTE: Service account protection happens at orchestrator level, not in AuditHandler
        await CreateTestUserAsync(0L);
        var messageId = await CreateTestMessageAsync(ChatId, 0L);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act - Should succeed because user exists
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatId, 0L, executor);
        }

        // Assert - Verify record was inserted
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.MessageId == messageId && ua.UserId == 0L)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.UserId, Is.EqualTo(0L));
    }

    #endregion

    #region LogBanAsync FK Constraint Tests

    [Test]
    public async Task LogBanAsync_WithValidUserId_InsertsSuccessfully()
    {
        // Arrange - Create a telegram_user
        await CreateTestUserAsync(ValidUserId);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogBanAsync(ValidUserId, executor, "Test ban reason");
        }

        // Assert
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.UserId == ValidUserId && ua.ActionType == Data.Models.UserActionType.Ban)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.Reason, Is.EqualTo("Test ban reason"));
    }

    [Test]
    public void LogBanAsync_WithNonExistentUserId_ThrowsDbUpdateException()
    {
        // Arrange
        var executor = Actor.FromSystem("IntegrationTest");

        // Act & Assert
        var ex = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogBanAsync(NonExistentUserId, executor, "Test ban");
        });

        Assert.That(ex!.InnerException?.Message, Does.Contain("foreign key").Or.Contain("violates").IgnoreCase);
    }

    #endregion

    #region LogWarnAsync FK Constraint Tests

    [Test]
    public async Task LogWarnAsync_WithValidUserId_InsertsSuccessfully()
    {
        // Arrange
        await CreateTestUserAsync(ValidUserId);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogWarnAsync(ValidUserId, executor, "Test warning");
        }

        // Assert
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.UserId == ValidUserId && ua.ActionType == Data.Models.UserActionType.Warn)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.Reason, Is.EqualTo("Test warning"));
    }

    [Test]
    public void LogWarnAsync_WithNonExistentUserId_ThrowsDbUpdateException()
    {
        // Arrange
        var executor = Actor.FromSystem("IntegrationTest");

        // Act & Assert
        Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogWarnAsync(NonExistentUserId, executor, "Test warning");
        });
    }

    #endregion

    #region Actor Exclusive Arc Tests

    [Test]
    public async Task LogDeleteAsync_WithSystemActor_StoresSystemIdentifier()
    {
        // Arrange
        await CreateTestUserAsync(ValidUserId);
        var messageId = await CreateTestMessageAsync(ChatId, ValidUserId);

        var executor = Actor.FromSystem("AutoModerator");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatId, ValidUserId, executor);
        }

        // Assert - Verify exclusive arc: only system_identifier is set
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.MessageId == messageId)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.SystemIdentifier, Is.EqualTo("AutoModerator"));
        Assert.That(record.WebUserId, Is.Null, "WebUserId should be null for system actor");
        Assert.That(record.TelegramUserId, Is.Null, "TelegramUserId should be null for system actor");
    }

    #endregion
}

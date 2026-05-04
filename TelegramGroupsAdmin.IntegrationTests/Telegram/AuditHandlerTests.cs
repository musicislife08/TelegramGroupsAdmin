using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
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

    // Canonical anchors — telegram_user_id=9921676191756 (@unhelpfulgrab) exists in golden_template.
    // chat_id=-100026957614982 is MainChat in canonical. NonExistentUserId is outside the
    // canonical ID range [9_000_000_000_000, 10_000_000_000_000) so it will never collide.
    private const long CanonicalUserId = 9921676191756L;
    private const long NonExistentUserId = 999999999L;
    private const long MainChatId = -100026957614982L;
    private const int TestMessageId = 99999;  // Used in tests expecting FK failures

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider);

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
        });

        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IAuditHandler, AuditHandler>();

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Inserts a message seeded inline (satisfies user_actions.message_id FK constraint).
    /// The user and chat must already exist (canonical or previously seeded inline).
    /// </summary>
    private async Task<int> CreateTestMessageAsync(long chatId, long userId)
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

    /// <summary>
    /// Inserts a telegram_user inline. Use only for IDs that are not in canonical
    /// (e.g., the zero service-account edge case).
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
        // Arrange - CanonicalUserId (@unhelpfulgrab) already exists in golden_template;
        // seed a new message inline so we have a valid message_id FK target.
        var messageId = await CreateTestMessageAsync(MainChatId, CanonicalUserId);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act - Log deletion for that user (create new scope)
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatIdentity.FromId(MainChatId), UserIdentity.FromId(CanonicalUserId), executor);
        }

        // Assert - Verify record was inserted with FK intact
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.MessageId == messageId && ua.UserId == CanonicalUserId)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null, "Audit record should be inserted");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record!.UserId, Is.EqualTo(CanonicalUserId));
            Assert.That(record.MessageId, Is.EqualTo(messageId));
            Assert.That(record.ActionType, Is.EqualTo((int)UserActionType.Delete));
            Assert.That(record.SystemIdentifier, Is.EqualTo("IntegrationTest"));
        }
    }

    [Test]
    public void LogDeleteAsync_WithNonExistentUserId_ThrowsDbUpdateException()
    {
        // Arrange - NonExistentUserId is outside the canonical ID range so it is absent
        // from golden_template; no inline seed needed — FK constraint will fire.
        var executor = Actor.FromSystem("IntegrationTest");

        // Act & Assert - Should throw DbUpdateException due to FK constraint violation
        var ex = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var scope = _serviceProvider!.CreateScope();
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(TestMessageId, ChatIdentity.FromId(MainChatId), UserIdentity.FromId(NonExistentUserId), executor);
        });

        // Verify it's specifically an FK constraint violation
        Assert.That(ex!.InnerException?.Message, Does.Contain("foreign key").Or.Contain("violates").IgnoreCase,
            "Exception should mention foreign key constraint violation");
    }

    [Test]
    public async Task LogDeleteAsync_WithServiceAccountUserId_WorksIfUserExists()
    {
        // Arrange - telegram_user_id=0 is not in canonical (it is the chat_id=0 sentinel,
        // not a telegram_user row). Seed the zero service-account user inline.
        // NOTE: Service account protection happens at orchestrator level, not in AuditHandler.
        await CreateTestUserAsync(0L);
        var messageId = await CreateTestMessageAsync(MainChatId, 0L);

        var executor = Actor.FromSystem("IntegrationTest");

        // Act - Should succeed because user exists
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatIdentity.FromId(MainChatId), UserIdentity.FromId(0L), executor);
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
        // Arrange - CanonicalUserId (@unhelpfulgrab) already exists in golden_template.
        var executor = Actor.FromSystem("IntegrationTest");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogBanAsync(UserIdentity.FromId(CanonicalUserId), executor, "Test ban reason");
        }

        // Assert
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.UserId == CanonicalUserId && ua.ActionType == (int)UserActionType.Ban)
            .OrderByDescending(ua => ua.Id)
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
            await auditHandler.LogBanAsync(UserIdentity.FromId(NonExistentUserId), executor, "Test ban");
        });

        Assert.That(ex!.InnerException?.Message, Does.Contain("foreign key").Or.Contain("violates").IgnoreCase);
    }

    #endregion

    #region LogWarnAsync FK Constraint Tests

    [Test]
    public async Task LogWarnAsync_WithValidUserId_InsertsSuccessfully()
    {
        // Arrange - CanonicalUserId (@unhelpfulgrab) already exists in golden_template.
        var executor = Actor.FromSystem("IntegrationTest");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogWarnAsync(UserIdentity.FromId(CanonicalUserId), executor, "Test warning");
        }

        // Assert
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.UserId == CanonicalUserId && ua.ActionType == (int)UserActionType.Warn)
            .OrderByDescending(ua => ua.Id)
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
            await auditHandler.LogWarnAsync(UserIdentity.FromId(NonExistentUserId), executor, "Test warning");
        });
    }

    #endregion

    #region Actor Exclusive Arc Tests

    [Test]
    public async Task LogDeleteAsync_WithSystemActor_StoresSystemIdentifier()
    {
        // Arrange - CanonicalUserId (@unhelpfulgrab) exists in golden_template.
        var messageId = await CreateTestMessageAsync(MainChatId, CanonicalUserId);

        var executor = Actor.FromSystem("AutoModerator");

        // Act
        using (var scope = _serviceProvider!.CreateScope())
        {
            var auditHandler = scope.ServiceProvider.GetRequiredService<IAuditHandler>();
            await auditHandler.LogDeleteAsync(messageId, ChatIdentity.FromId(MainChatId), UserIdentity.FromId(CanonicalUserId), executor);
        }

        // Assert - Verify exclusive arc: only system_identifier is set
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var record = await context.UserActions
            .Where(ua => ua.MessageId == messageId)
            .FirstOrDefaultAsync();

        Assert.That(record, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record!.SystemIdentifier, Is.EqualTo("AutoModerator"));
            Assert.That(record.WebUserId, Is.Null, "WebUserId should be null for system actor");
            Assert.That(record.TelegramUserId, Is.Null, "TelegramUserId should be null for system actor");
        }
    }

    #endregion
}

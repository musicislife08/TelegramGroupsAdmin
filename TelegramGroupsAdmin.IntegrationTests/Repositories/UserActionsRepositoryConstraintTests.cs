using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Constraint tests for the user_actions CK constraint
/// CK_user_actions_message_chat_null_consistency:
///   (message_id IS NULL) OR (chat_id IS NOT NULL)
///
/// Migration: 20260421043129_RelaxUserActionsChatMessageConstraint
/// Old constraint: (message_id IS NULL) = (chat_id IS NULL) — rejected chat-scoped rows without message_id
/// New constraint: allows chat-scoped audit rows without message_id (WelcomeBypass, Kick, etc.)
///                 but still rejects orphan-message rows (message_id set, chat_id NULL).
/// </summary>
[TestFixture]
public class UserActionsRepositoryConstraintTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    private const long TestUserId = 555111222L;
    private const long TestChatId = -1009988776655L;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        var services = new ServiceCollection();

        services.AddDataProtection()
            .SetApplicationName("TelegramGroupsAdmin.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"test_keys_{Guid.NewGuid():N}")));

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

        _serviceProvider = services.BuildServiceProvider();

        await SeedTestUserAsync(TestUserId);
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Seeds a minimal telegram_user row to satisfy the user_id FK constraint on user_actions.
    /// </summary>
    private async Task SeedTestUserAsync(long userId)
    {
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var now = DateTimeOffset.UtcNow;
        context.TelegramUsers.Add(new TelegramUserDto
        {
            TelegramUserId = userId,
            Username = $"testuser_{userId}",
            FirstName = "Test",
            LastName = "User",
            IsBot = false,
            IsTrusted = false,
            BotDmEnabled = false,
            FirstSeenAt = now.AddDays(-1),
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Verifies that a chat-scoped audit row with no message_id (e.g. WelcomeBypass) inserts cleanly.
    /// This is the core use-case that the relaxed constraint enables.
    /// </summary>
    [Test]
    public async Task Insert_ChatScopedAuditRow_WithoutMessageId_Succeeds()
    {
        // Arrange
        var record = new UserActionRecord(
            Id: 0,
            UserId: TestUserId,
            ActionType: UserActionType.WelcomeBypass,
            MessageId: null,
            ChatId: TestChatId,
            IssuedBy: Actor.WelcomeBypass,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Reason: "Trusted user");

        // Act + Assert: insert should commit cleanly
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();

        Assert.DoesNotThrowAsync(async () =>
            await repo.InsertAsync(record, CancellationToken.None));
    }

    /// <summary>
    /// Verifies that a row with message_id set but chat_id NULL is rejected by the check constraint.
    /// This is the "orphan message" case the constraint guards against.
    /// </summary>
    [Test]
    public void Insert_OrphanMessageRow_MessageIdWithoutChatId_ThrowsConstraintViolation()
    {
        // Arrange
        var record = new UserActionRecord(
            Id: 0,
            UserId: TestUserId,
            ActionType: UserActionType.Delete,
            MessageId: 123456,
            ChatId: null,  // violates: message_id NOT NULL requires chat_id NOT NULL
            IssuedBy: Actor.Unknown,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Reason: null);

        // Act + Assert: Postgres check-constraint violation wraps as DbUpdateException
        var ex = Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var scope = _serviceProvider!.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IUserActionsRepository>();
            await repo.InsertAsync(record, CancellationToken.None);
        });

        Assert.That(ex!.InnerException?.Message,
            Does.Contain("user_actions").IgnoreCase
                .Or.Contain("check").IgnoreCase
                .Or.Contain("constraint").IgnoreCase);
    }
}

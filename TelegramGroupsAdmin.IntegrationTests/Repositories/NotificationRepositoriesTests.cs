using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Test suite for notification repositories: PushSubscriptionsRepository and WebNotificationRepository.
///
/// Tests validate:
/// - CRUD operations for push subscriptions (browser push endpoints)
/// - CRUD operations for web notifications (in-app notification bell)
/// - Upsert behavior for push subscriptions (user+endpoint uniqueness)
/// - Pagination and filtering for notifications
/// - Retention cleanup for old read notifications
/// </summary>
[TestFixture]
public class NotificationRepositoriesTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IPushSubscriptionsRepository? _pushRepo;
    private IWebNotificationRepository? _notificationRepo;
    private IDataProtectionProvider? _dataProtectionProvider;

    // Test user IDs from golden dataset
    private const string TestUserId1 = GoldenDataset.Users.User1_Id;
    private const string TestUserId2 = GoldenDataset.Users.User2_Id;

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

        // Register repositories
        services.AddScoped<IPushSubscriptionsRepository, PushSubscriptionsRepository>();
        services.AddScoped<IWebNotificationRepository, WebNotificationRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Seed golden dataset (creates test users we'll reference)
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            await GoldenDataset.SeedAsync(context, _dataProtectionProvider);
        }

        // Create repository instances
        var scope = _serviceProvider.CreateScope();
        _pushRepo = scope.ServiceProvider.GetRequiredService<IPushSubscriptionsRepository>();
        _notificationRepo = scope.ServiceProvider.GetRequiredService<IWebNotificationRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region PushSubscriptionsRepository Tests

    [Test]
    public async Task PushSubscriptions_UpsertAsync_ShouldCreateNew()
    {
        // Arrange
        var subscription = new PushSubscription
        {
            UserId = TestUserId1,
            Endpoint = "https://push.example.com/sub/abc123",
            P256dh = "BPubKey123",
            Auth = "AuthKey456",
            UserAgent = "Mozilla/5.0",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pushRepo!.UpsertAsync(subscription);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.GreaterThan(0), "Should assign ID on create");
        Assert.That(result.UserId, Is.EqualTo(TestUserId1));
        Assert.That(result.Endpoint, Is.EqualTo("https://push.example.com/sub/abc123"));
        Assert.That(result.P256dh, Is.EqualTo("BPubKey123"));
        Assert.That(result.Auth, Is.EqualTo("AuthKey456"));
    }

    [Test]
    public async Task PushSubscriptions_UpsertAsync_ShouldUpdateExisting()
    {
        // Arrange - Create initial subscription
        var subscription = new PushSubscription
        {
            UserId = TestUserId1,
            Endpoint = "https://push.example.com/sub/update-test",
            P256dh = "OriginalKey",
            Auth = "OriginalAuth",
            UserAgent = "Chrome/100",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _pushRepo!.UpsertAsync(subscription);

        // Act - Update with same user+endpoint but new keys
        var updated = new PushSubscription
        {
            UserId = TestUserId1,
            Endpoint = "https://push.example.com/sub/update-test",
            P256dh = "UpdatedKey",
            Auth = "UpdatedAuth",
            UserAgent = "Chrome/120"
        };
        var result = await _pushRepo.UpsertAsync(updated);

        // Assert
        Assert.That(result.P256dh, Is.EqualTo("UpdatedKey"), "Should update P256dh");
        Assert.That(result.Auth, Is.EqualTo("UpdatedAuth"), "Should update Auth");

        // Verify only one subscription exists
        var subscriptions = await _pushRepo.GetByUserIdAsync(TestUserId1);
        var matching = subscriptions.Where(s => s.Endpoint == "https://push.example.com/sub/update-test").ToList();
        Assert.That(matching.Count, Is.EqualTo(1), "Should have exactly one subscription (upsert)");
    }

    [Test]
    public async Task PushSubscriptions_GetByUserIdAsync_ShouldReturnAllUserSubscriptions()
    {
        // Arrange - Create multiple subscriptions for same user (different devices)
        var sub1 = new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/chrome", P256dh = "key1", Auth = "auth1", UserAgent = "Chrome" };
        var sub2 = new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/firefox", P256dh = "key2", Auth = "auth2", UserAgent = "Firefox" };
        var sub3 = new PushSubscription { UserId = TestUserId2, Endpoint = "https://push.example.com/other", P256dh = "key3", Auth = "auth3", UserAgent = "Safari" };

        await _pushRepo!.UpsertAsync(sub1);
        await _pushRepo.UpsertAsync(sub2);
        await _pushRepo.UpsertAsync(sub3);

        // Act
        var user1Subs = await _pushRepo.GetByUserIdAsync(TestUserId1);
        var user2Subs = await _pushRepo.GetByUserIdAsync(TestUserId2);

        // Assert
        Assert.That(user1Subs.Count, Is.EqualTo(2), "User1 should have 2 subscriptions");
        Assert.That(user2Subs.Count, Is.EqualTo(1), "User2 should have 1 subscription");
    }

    [Test]
    public async Task PushSubscriptions_GetByEndpointAsync_ShouldReturnSubscription()
    {
        // Arrange
        var subscription = new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/unique-endpoint", P256dh = "key", Auth = "auth", UserAgent = "Chrome" };
        await _pushRepo!.UpsertAsync(subscription);

        // Act
        var result = await _pushRepo.GetByEndpointAsync("https://push.example.com/unique-endpoint");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(TestUserId1));
    }

    [Test]
    public async Task PushSubscriptions_GetByEndpointAsync_NotExists_ShouldReturnNull()
    {
        // Act
        var result = await _pushRepo!.GetByEndpointAsync("https://nonexistent.endpoint.com/xyz");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task PushSubscriptions_DeleteByEndpointAsync_ShouldDelete()
    {
        // Arrange
        var subscription = new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/to-delete", P256dh = "key", Auth = "auth", UserAgent = "Chrome" };
        await _pushRepo!.UpsertAsync(subscription);

        // Verify it exists
        var before = await _pushRepo.GetByEndpointAsync("https://push.example.com/to-delete");
        Assert.That(before, Is.Not.Null, "Subscription should exist before delete");

        // Act
        var deleted = await _pushRepo.DeleteByEndpointAsync("https://push.example.com/to-delete");

        // Assert
        Assert.That(deleted, Is.True, "Should return true when subscription deleted");

        var after = await _pushRepo.GetByEndpointAsync("https://push.example.com/to-delete");
        Assert.That(after, Is.Null, "Subscription should not exist after delete");
    }

    [Test]
    public async Task PushSubscriptions_DeleteByEndpointAsync_NotExists_ShouldReturnFalse()
    {
        // Act
        var deleted = await _pushRepo!.DeleteByEndpointAsync("https://nonexistent.endpoint.com/xyz");

        // Assert
        Assert.That(deleted, Is.False, "Should return false when nothing to delete");
    }

    [Test]
    public async Task PushSubscriptions_DeleteByUserIdAsync_ShouldDeleteAllUserSubscriptions()
    {
        // Arrange - Create multiple subscriptions for user
        await _pushRepo!.UpsertAsync(new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/1", P256dh = "k1", Auth = "a1", UserAgent = "Chrome" });
        await _pushRepo.UpsertAsync(new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/2", P256dh = "k2", Auth = "a2", UserAgent = "Firefox" });
        await _pushRepo.UpsertAsync(new PushSubscription { UserId = TestUserId1, Endpoint = "https://push.example.com/3", P256dh = "k3", Auth = "a3", UserAgent = "Safari" });

        // Verify they exist
        var before = await _pushRepo.GetByUserIdAsync(TestUserId1);
        Assert.That(before.Count, Is.EqualTo(3), "Should have 3 subscriptions before delete");

        // Act
        var deletedCount = await _pushRepo.DeleteByUserIdAsync(TestUserId1);

        // Assert
        Assert.That(deletedCount, Is.EqualTo(3), "Should delete 3 subscriptions");

        var after = await _pushRepo.GetByUserIdAsync(TestUserId1);
        Assert.That(after.Count, Is.EqualTo(0), "Should have 0 subscriptions after delete");
    }

    #endregion

    #region WebNotificationRepository Tests

    [Test]
    public async Task WebNotifications_CreateAsync_ShouldCreate()
    {
        // Arrange
        var notification = new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.MessageReported,
            Subject = "New Report",
            Message = "A new spam report has been submitted",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _notificationRepo!.CreateAsync(notification);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.GreaterThan(0), "Should assign ID on create");
        Assert.That(result.UserId, Is.EqualTo(TestUserId1));
        Assert.That(result.EventType, Is.EqualTo(NotificationEventType.MessageReported));
        Assert.That(result.Subject, Is.EqualTo("New Report"));
        Assert.That(result.IsRead, Is.False);
    }

    [Test]
    public async Task WebNotifications_GetRecentAsync_ShouldReturnOrderedByCreatedAtDesc()
    {
        // Arrange - Create notifications at different times
        var notification1 = new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.MessageReported,
            Subject = "First",
            Message = "First notification",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        var notification2 = new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.SpamDetected,
            Subject = "Second",
            Message = "Second notification",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        var notification3 = new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.BackupFailed,
            Subject = "Third",
            Message = "Third notification (most recent)",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await _notificationRepo!.CreateAsync(notification1);
        await _notificationRepo.CreateAsync(notification2);
        await _notificationRepo.CreateAsync(notification3);

        // Act
        var notifications = await _notificationRepo.GetRecentAsync(TestUserId1);

        // Assert
        Assert.That(notifications.Count, Is.GreaterThanOrEqualTo(3));

        // Verify ordered by CreatedAt DESC (most recent first)
        for (int i = 0; i < notifications.Count - 1; i++)
        {
            Assert.That(notifications[i].CreatedAt, Is.GreaterThanOrEqualTo(notifications[i + 1].CreatedAt),
                "Notifications should be ordered by CreatedAt DESC");
        }
    }

    [Test]
    public async Task WebNotifications_GetRecentAsync_ShouldRespectLimitAndOffset()
    {
        // Arrange - Create 10 notifications
        for (int i = 0; i < 10; i++)
        {
            await _notificationRepo!.CreateAsync(new WebNotification
            {
                UserId = TestUserId1,
                EventType = NotificationEventType.SpamDetected,
                Subject = $"Notification {i}",
                Message = $"Message {i}",
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
        }

        // Act - Get page 1 (first 5)
        var page1 = await _notificationRepo!.GetRecentAsync(TestUserId1, limit: 5, offset: 0);

        // Act - Get page 2 (next 5)
        var page2 = await _notificationRepo.GetRecentAsync(TestUserId1, limit: 5, offset: 5);

        // Assert
        Assert.That(page1.Count, Is.EqualTo(5), "Page 1 should have 5 notifications");
        Assert.That(page2.Count, Is.EqualTo(5), "Page 2 should have 5 notifications");

        // Ensure no overlap
        var page1Ids = page1.Select(n => n.Id).ToHashSet();
        var page2Ids = page2.Select(n => n.Id).ToHashSet();
        Assert.That(page1Ids.Intersect(page2Ids).Count(), Is.EqualTo(0), "Pages should not overlap");
    }

    [Test]
    public async Task WebNotifications_GetRecentAsync_ShouldFilterByUserId()
    {
        // Arrange - Create notifications for different users
        await _notificationRepo!.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.UserBanned,
            Subject = "User1 notification",
            Message = "For user 1",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _notificationRepo.CreateAsync(new WebNotification
        {
            UserId = TestUserId2,
            EventType = NotificationEventType.UserBanned,
            Subject = "User2 notification",
            Message = "For user 2",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Act
        var user1Notifications = await _notificationRepo.GetRecentAsync(TestUserId1);
        var user2Notifications = await _notificationRepo.GetRecentAsync(TestUserId2);

        // Assert
        Assert.That(user1Notifications.All(n => n.UserId == TestUserId1), Is.True, "Should only return User1's notifications");
        Assert.That(user2Notifications.All(n => n.UserId == TestUserId2), Is.True, "Should only return User2's notifications");
    }

    [Test]
    public async Task WebNotifications_GetUnreadCountAsync_ShouldCountUnreadOnly()
    {
        // Arrange - Create mix of read and unread notifications
        var n1 = await _notificationRepo!.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.SpamDetected,
            Subject = "Unread 1",
            Message = "Unread",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _notificationRepo.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.SpamDetected,
            Subject = "Unread 2",
            Message = "Unread",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _notificationRepo.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.SpamDetected,
            Subject = "Unread 3",
            Message = "Unread",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Mark one as read
        await _notificationRepo.MarkAsReadAsync(n1.Id);

        // Act
        var unreadCount = await _notificationRepo.GetUnreadCountAsync(TestUserId1);

        // Assert
        Assert.That(unreadCount, Is.EqualTo(2), "Should count only unread notifications");
    }

    [Test]
    public async Task WebNotifications_MarkAsReadAsync_ShouldMarkRead()
    {
        // Arrange
        var notification = await _notificationRepo!.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.MalwareDetected,
            Subject = "To Mark Read",
            Message = "This will be marked as read",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        Assert.That(notification.IsRead, Is.False, "Should start as unread");

        // Act
        await _notificationRepo.MarkAsReadAsync(notification.Id);

        // Assert - Retrieve and verify
        var recent = await _notificationRepo.GetRecentAsync(TestUserId1);
        var marked = recent.First(n => n.Id == notification.Id);
        Assert.That(marked.IsRead, Is.True, "Should be marked as read");
        Assert.That(marked.ReadAt, Is.Not.Null, "ReadAt should be set");
    }

    [Test]
    public async Task WebNotifications_MarkAllAsReadAsync_ShouldMarkAllUserNotificationsRead()
    {
        // Arrange - Create multiple unread notifications
        await _notificationRepo!.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.ChatHealthWarning,
            Subject = "Unread 1",
            Message = "Unread",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _notificationRepo.CreateAsync(new WebNotification
        {
            UserId = TestUserId1,
            EventType = NotificationEventType.ChatHealthWarning,
            Subject = "Unread 2",
            Message = "Unread",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Verify unread count before
        var countBefore = await _notificationRepo.GetUnreadCountAsync(TestUserId1);
        Assert.That(countBefore, Is.GreaterThanOrEqualTo(2), "Should have unread notifications");

        // Act
        await _notificationRepo.MarkAllAsReadAsync(TestUserId1);

        // Assert
        var countAfter = await _notificationRepo.GetUnreadCountAsync(TestUserId1);
        Assert.That(countAfter, Is.EqualTo(0), "All notifications should be marked read");
    }

    [Test]
    public async Task WebNotifications_DeleteOldReadNotificationsAsync_ShouldDeleteOldRead()
    {
        // Arrange - Create old read and new unread notifications
        // Use raw SQL to insert with specific timestamps (repository doesn't allow setting CreatedAt directly)
        var contextFactory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            // Old read notification (should be deleted)
            context.WebNotifications.Add(new WebNotificationDto
            {
                UserId = TestUserId1,
                EventType = (int)NotificationEventType.SpamDetected,
                Subject = "Old Read",
                Message = "Should be deleted",
                IsRead = true,
                ReadAt = DateTimeOffset.UtcNow.AddDays(-10),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });

            // New read notification (should NOT be deleted)
            context.WebNotifications.Add(new WebNotificationDto
            {
                UserId = TestUserId1,
                EventType = (int)NotificationEventType.SpamDetected,
                Subject = "New Read",
                Message = "Should NOT be deleted",
                IsRead = true,
                ReadAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Old unread notification (should NOT be deleted - only read ones are deleted)
            context.WebNotifications.Add(new WebNotificationDto
            {
                UserId = TestUserId1,
                EventType = (int)NotificationEventType.SpamDetected,
                Subject = "Old Unread",
                Message = "Should NOT be deleted (unread)",
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            });

            await context.SaveChangesAsync();
        }

        // Act - Delete read notifications older than 7 days
        var deletedCount = await _notificationRepo!.DeleteOldReadNotificationsAsync(TimeSpan.FromDays(7));

        // Assert
        Assert.That(deletedCount, Is.GreaterThanOrEqualTo(1), "Should delete at least the old read notification");

        // Verify the correct notifications remain
        var remaining = await _notificationRepo.GetRecentAsync(TestUserId1, limit: 100);
        Assert.That(remaining.Any(n => n.Subject == "Old Read"), Is.False, "Old read notification should be deleted");
        Assert.That(remaining.Any(n => n.Subject == "New Read"), Is.True, "New read notification should remain");
        Assert.That(remaining.Any(n => n.Subject == "Old Unread"), Is.True, "Old unread notification should remain");
    }

    #endregion
}

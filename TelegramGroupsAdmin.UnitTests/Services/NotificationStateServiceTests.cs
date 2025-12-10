using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Pure unit tests for NotificationStateService
/// Uses NSubstitute to mock dependencies - no database required
/// </summary>
[TestFixture]
public class NotificationStateServiceTests
{
    private IWebPushNotificationService _mockNotificationService = null!;
    private ILogger<NotificationStateService> _mockLogger = null!;
    private NotificationStateService _stateService = null!;

    private const string TestUserId = "test-user-123";

    [SetUp]
    public void SetUp()
    {
        _mockNotificationService = Substitute.For<IWebPushNotificationService>();
        _mockLogger = Substitute.For<ILogger<NotificationStateService>>();
        _stateService = new NotificationStateService(_mockNotificationService, _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _stateService.Dispose();
    }

    #region InitializeAsync Tests

    [Test]
    public async Task InitializeAsync_LoadsNotificationsFromService()
    {
        // Arrange
        var notifications = new List<WebNotification>
        {
            CreateNotification(1, "Test 1", false),
            CreateNotification(2, "Test 2", true)
        };

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(1);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(notifications.AsReadOnly());

        // Act
        await _stateService.InitializeAsync(TestUserId);

        // Assert
        Assert.That(_stateService.IsLoaded, Is.True);
        Assert.That(_stateService.UnreadCount, Is.EqualTo(1));
        Assert.That(_stateService.Notifications.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task InitializeAsync_SameUser_DoesNotReload()
    {
        // Arrange
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(5);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        // Act - Initialize twice
        await _stateService.InitializeAsync(TestUserId);
        await _stateService.InitializeAsync(TestUserId);

        // Assert - Service should only be called once
        await _mockNotificationService.Received(1).GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_DifferentUser_ReloadsData()
    {
        // Arrange
        _mockNotificationService.GetUnreadCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(Arg.Any<string>(), 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        // Act
        await _stateService.InitializeAsync("user-1");
        await _stateService.InitializeAsync("user-2");

        // Assert - Service called twice (once per user)
        await _mockNotificationService.Received(2).GetUnreadCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region RefreshAsync Tests

    [Test]
    public async Task RefreshAsync_WithoutInitialize_DoesNothing()
    {
        // Act - Refresh without initialize
        await _stateService.RefreshAsync();

        // Assert - No service calls
        await _mockNotificationService.DidNotReceive().GetUnreadCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshAsync_AfterInitialize_ReloadsData()
    {
        // Arrange - First load
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(2);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Update mock to return different count
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(5);

        // Act
        await _stateService.RefreshAsync();

        // Assert
        Assert.That(_stateService.UnreadCount, Is.EqualTo(5));
    }

    #endregion

    #region MarkAsReadAsync Tests

    [Test]
    public async Task MarkAsReadAsync_UpdatesLocalState()
    {
        // Arrange
        var notification = CreateNotification(1, "Test", false);
        var notifications = new List<WebNotification> { notification };

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(1);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(notifications.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);
        Assert.That(_stateService.UnreadCount, Is.EqualTo(1));

        // Act
        await _stateService.MarkAsReadAsync(1);

        // Assert
        Assert.That(_stateService.UnreadCount, Is.EqualTo(0));
        Assert.That(_stateService.Notifications[0].IsRead, Is.True);
        Assert.That(_stateService.Notifications[0].ReadAt, Is.Not.Null);
    }

    [Test]
    public async Task MarkAsReadAsync_AlreadyRead_NoCountChange()
    {
        // Arrange
        var notification = CreateNotification(1, "Test", true); // Already read
        var notifications = new List<WebNotification> { notification };

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(notifications.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act
        await _stateService.MarkAsReadAsync(1);

        // Assert - Count should still be 0 (not -1)
        Assert.That(_stateService.UnreadCount, Is.EqualTo(0));
    }

    [Test]
    public async Task MarkAsReadAsync_NotificationNotFound_NoChange()
    {
        // Arrange
        var notification = CreateNotification(1, "Test", false);
        var notifications = new List<WebNotification> { notification };

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(1);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(notifications.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act - Try to mark non-existent notification
        await _stateService.MarkAsReadAsync(999);

        // Assert - State unchanged
        Assert.That(_stateService.UnreadCount, Is.EqualTo(1));
        Assert.That(_stateService.Notifications[0].IsRead, Is.False);
    }

    #endregion

    #region MarkAllAsReadAsync Tests

    [Test]
    public async Task MarkAllAsReadAsync_MarksAllNotificationsRead()
    {
        // Arrange
        var notifications = new List<WebNotification>
        {
            CreateNotification(1, "Test 1", false),
            CreateNotification(2, "Test 2", false),
            CreateNotification(3, "Test 3", true) // Already read
        };

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(2);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(notifications.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act
        await _stateService.MarkAllAsReadAsync();

        // Assert
        Assert.That(_stateService.UnreadCount, Is.EqualTo(0));
        Assert.That(_stateService.Notifications.All(n => n.IsRead), Is.True);
    }

    [Test]
    public async Task MarkAllAsReadAsync_NoUser_DoesNothing()
    {
        // Act - No user initialized
        await _stateService.MarkAllAsReadAsync();

        // Assert - No service call
        await _mockNotificationService.DidNotReceive()
            .MarkAllAsReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region AddNotificationAsync Tests

    [Test]
    public async Task AddNotificationAsync_InsertsAtBeginning()
    {
        // Arrange
        var existing = CreateNotification(1, "Old", true);
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification> { existing }.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act
        var newNotification = CreateNotification(2, "New", false);
        await _stateService.AddNotificationAsync(newNotification);

        // Assert - New notification at index 0
        Assert.That(_stateService.Notifications[0].Subject, Is.EqualTo("New"));
        Assert.That(_stateService.Notifications[1].Subject, Is.EqualTo("Old"));
    }

    [Test]
    public async Task AddNotificationAsync_IncrementsUnreadCount()
    {
        // Arrange
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(1);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);
        Assert.That(_stateService.UnreadCount, Is.EqualTo(1));

        // Act
        await _stateService.AddNotificationAsync(CreateNotification(1, "New", false));

        // Assert
        Assert.That(_stateService.UnreadCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AddNotificationAsync_ReadNotification_NoCountIncrease()
    {
        // Arrange
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act - Add read notification
        await _stateService.AddNotificationAsync(CreateNotification(1, "New", true));

        // Assert - Count should not increase
        Assert.That(_stateService.UnreadCount, Is.EqualTo(0));
        Assert.That(_stateService.Notifications.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task AddNotificationAsync_TrimsList_AtMaxSize()
    {
        // Arrange
        var existingNotifications = Enumerable.Range(1, 50)
            .Select(i => CreateNotification(i, $"Notification {i}", true))
            .ToList();

        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(existingNotifications.AsReadOnly());

        await _stateService.InitializeAsync(TestUserId);

        // Act - Add one more (should trigger trim to 50)
        await _stateService.AddNotificationAsync(CreateNotification(100, "New", false));

        // Assert - Should be exactly 50 (trimmed)
        Assert.That(_stateService.Notifications.Count, Is.EqualTo(50));
        Assert.That(_stateService.Notifications[0].Subject, Is.EqualTo("New"));
    }

    #endregion

    #region OnChange Event Tests

    [Test]
    public async Task OnChange_FiresWhenStateChanges()
    {
        // Arrange
        _mockNotificationService.GetUnreadCountAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(0);
        _mockNotificationService.GetRecentAsync(TestUserId, 20, 0, Arg.Any<CancellationToken>())
            .Returns(new List<WebNotification>().AsReadOnly());

        var changeCount = 0;
        _stateService.OnChange += () =>
        {
            changeCount++;
            return Task.CompletedTask;
        };

        // Act
        await _stateService.InitializeAsync(TestUserId);

        // Assert
        Assert.That(changeCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_ClearsOnChangeHandler()
    {
        // Arrange
        _stateService.OnChange += () => Task.CompletedTask;

        // Act
        _stateService.Dispose();

        // Assert - Ensure no exception on double dispose
        Assert.DoesNotThrow(() => _stateService.Dispose());
    }

    #endregion

    #region Helper Methods

    private static WebNotification CreateNotification(long id, string subject, bool isRead)
    {
        return new WebNotification
        {
            Id = id,
            UserId = TestUserId,
            EventType = NotificationEventType.SpamDetected,
            Subject = subject,
            Message = $"Message for {subject}",
            IsRead = isRead,
            ReadAt = isRead ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}

using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating web notifications for E2E testing.
/// Creates notifications directly via IWebNotificationRepository.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// var notification = await new TestWebNotificationBuilder(Factory.Services)
///     .ForUser(testUser.Id)
///     .WithSubject("Spam Detected")
///     .WithMessage("User xyz posted spam in Test Chat")
///     .AsUnread()
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestWebNotificationBuilder
{
    private readonly IServiceProvider _services;
    private string _userId = string.Empty;
    private string _subject = "Test Notification";
    private string _message = "This is a test notification message";
    private NotificationEventType _eventType = NotificationEventType.SpamDetected;
    private bool _isRead;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public TestWebNotificationBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the user ID this notification belongs to.
    /// </summary>
    public TestWebNotificationBuilder ForUser(string userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>
    /// Sets the notification subject/title.
    /// </summary>
    public TestWebNotificationBuilder WithSubject(string subject)
    {
        _subject = subject;
        return this;
    }

    /// <summary>
    /// Sets the notification message body.
    /// </summary>
    public TestWebNotificationBuilder WithMessage(string message)
    {
        _message = message;
        return this;
    }

    /// <summary>
    /// Sets the notification event type.
    /// </summary>
    public TestWebNotificationBuilder WithEventType(NotificationEventType eventType)
    {
        _eventType = eventType;
        return this;
    }

    /// <summary>
    /// Marks the notification as unread (default).
    /// </summary>
    public TestWebNotificationBuilder AsUnread()
    {
        _isRead = false;
        return this;
    }

    /// <summary>
    /// Marks the notification as already read.
    /// </summary>
    public TestWebNotificationBuilder AsRead()
    {
        _isRead = true;
        return this;
    }

    /// <summary>
    /// Sets the creation timestamp.
    /// </summary>
    public TestWebNotificationBuilder CreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    /// <summary>
    /// Creates the notification with a relative age.
    /// </summary>
    public TestWebNotificationBuilder CreatedAgo(TimeSpan age)
    {
        _createdAt = DateTimeOffset.UtcNow - age;
        return this;
    }

    /// <summary>
    /// Builds and persists the notification.
    /// </summary>
    public async Task<WebNotification> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId))
        {
            throw new InvalidOperationException("User ID must be set via ForUser()");
        }

        using var scope = _services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebNotificationRepository>();

        var notification = new WebNotification
        {
            UserId = _userId,
            Subject = _subject,
            Message = _message,
            EventType = _eventType,
            IsRead = _isRead,
            CreatedAt = _createdAt,
            ReadAt = _isRead ? DateTimeOffset.UtcNow : null
        };

        return await repository.CreateAsync(notification, cancellationToken);
    }

    #region Common Notification Shortcuts

    /// <summary>
    /// Creates a spam detected notification.
    /// </summary>
    public TestWebNotificationBuilder AsSpamDetected(string chatName = "Test Chat", string userName = "spammer")
    {
        return WithEventType(NotificationEventType.SpamDetected)
            .WithSubject("Spam Detected")
            .WithMessage($"Spam message from {userName} in {chatName}");
    }

    /// <summary>
    /// Creates a message reported notification.
    /// </summary>
    public TestWebNotificationBuilder AsMessageReported(string chatName = "Test Chat", string reporterName = "reporter")
    {
        return WithEventType(NotificationEventType.MessageReported)
            .WithSubject("Message Reported")
            .WithMessage($"{reporterName} reported a message in {chatName}");
    }

    /// <summary>
    /// Creates a user banned notification.
    /// </summary>
    public TestWebNotificationBuilder AsUserBanned(string userName = "banned_user", int chatsAffected = 1)
    {
        return WithEventType(NotificationEventType.UserBanned)
            .WithSubject("User Banned")
            .WithMessage($"{userName} was banned from {chatsAffected} chat(s)");
    }

    /// <summary>
    /// Creates a malware detected notification.
    /// </summary>
    public TestWebNotificationBuilder AsMalwareDetected(string fileName = "malware.exe")
    {
        return WithEventType(NotificationEventType.MalwareDetected)
            .WithSubject("Malware Detected")
            .WithMessage($"Malicious file detected: {fileName}");
    }

    /// <summary>
    /// Creates a backup failed notification.
    /// </summary>
    public TestWebNotificationBuilder AsBackupFailed(string reason = "Disk full")
    {
        return WithEventType(NotificationEventType.BackupFailed)
            .WithSubject("Backup Failed")
            .WithMessage($"Database backup failed: {reason}");
    }

    #endregion
}

using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

/// <summary>
/// Pure unit tests for NotificationConfig and ChannelPreference classes
/// No database or mocks required - tests pure business logic
/// </summary>
[TestFixture]
public class NotificationConfigTests
{
    #region NotificationConfig.IsEnabled Tests

    [Test]
    public void IsEnabled_NoChannelsConfigured_ReturnsFalse()
    {
        // Arrange
        var config = new NotificationConfig();

        // Act & Assert
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.SpamDetected), Is.False);
        Assert.That(config.IsEnabled(NotificationChannel.Email, NotificationEventType.UserBanned), Is.False);
        Assert.That(config.IsEnabled(NotificationChannel.WebPush, NotificationEventType.MalwareDetected), Is.False);
    }

    [Test]
    public void IsEnabled_ChannelExistsButEventNotEnabled_ReturnsFalse()
    {
        // Arrange
        var config = new NotificationConfig
        {
            Channels =
            [
                new ChannelPreference
                {
                    Channel = NotificationChannel.TelegramDm,
                    EnabledEvents = [NotificationEventType.SpamDetected]
                }
            ]
        };

        // Act & Assert - UserBanned not in enabled list
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.UserBanned), Is.False);
    }

    [Test]
    public void IsEnabled_ChannelAndEventEnabled_ReturnsTrue()
    {
        // Arrange
        var config = new NotificationConfig
        {
            Channels =
            [
                new ChannelPreference
                {
                    Channel = NotificationChannel.Email,
                    EnabledEvents = [NotificationEventType.BackupFailed, NotificationEventType.MalwareDetected]
                }
            ]
        };

        // Act & Assert
        Assert.That(config.IsEnabled(NotificationChannel.Email, NotificationEventType.BackupFailed), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.Email, NotificationEventType.MalwareDetected), Is.True);
    }

    [Test]
    public void IsEnabled_DifferentChannelHasEvent_ReturnsFalse()
    {
        // Arrange - Email has SpamDetected, TelegramDm does not
        var config = new NotificationConfig
        {
            Channels =
            [
                new ChannelPreference
                {
                    Channel = NotificationChannel.Email,
                    EnabledEvents = [NotificationEventType.SpamDetected]
                }
            ]
        };

        // Act & Assert - Checking TelegramDm channel which doesn't have SpamDetected
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.SpamDetected), Is.False);
    }

    [Test]
    public void IsEnabled_MultipleChannelsIndependent()
    {
        // Arrange - Each channel has different events enabled
        var config = new NotificationConfig
        {
            Channels =
            [
                new ChannelPreference
                {
                    Channel = NotificationChannel.TelegramDm,
                    EnabledEvents = [NotificationEventType.SpamDetected, NotificationEventType.UserBanned]
                },
                new ChannelPreference
                {
                    Channel = NotificationChannel.Email,
                    EnabledEvents = [NotificationEventType.BackupFailed]
                },
                new ChannelPreference
                {
                    Channel = NotificationChannel.WebPush,
                    EnabledEvents = [NotificationEventType.SpamDetected, NotificationEventType.MessageReported]
                }
            ]
        };

        // Act & Assert - TelegramDm
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.SpamDetected), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.UserBanned), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.TelegramDm, NotificationEventType.BackupFailed), Is.False);

        // Act & Assert - Email
        Assert.That(config.IsEnabled(NotificationChannel.Email, NotificationEventType.BackupFailed), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.Email, NotificationEventType.SpamDetected), Is.False);

        // Act & Assert - WebPush
        Assert.That(config.IsEnabled(NotificationChannel.WebPush, NotificationEventType.SpamDetected), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.WebPush, NotificationEventType.MessageReported), Is.True);
        Assert.That(config.IsEnabled(NotificationChannel.WebPush, NotificationEventType.UserBanned), Is.False);
    }

    #endregion

    #region NotificationConfig.GetOrCreateChannel Tests

    [Test]
    public void GetOrCreateChannel_ChannelDoesNotExist_CreatesNew()
    {
        // Arrange
        var config = new NotificationConfig();
        Assert.That(config.Channels.Count, Is.EqualTo(0), "Should start empty");

        // Act
        var channel = config.GetOrCreateChannel(NotificationChannel.Email);

        // Assert
        Assert.That(channel, Is.Not.Null);
        Assert.That(channel.Channel, Is.EqualTo(NotificationChannel.Email));
        Assert.That(config.Channels.Count, Is.EqualTo(1), "Should have created one channel");
    }

    [Test]
    public void GetOrCreateChannel_ChannelExists_ReturnsExisting()
    {
        // Arrange
        var existingPref = new ChannelPreference
        {
            Channel = NotificationChannel.TelegramDm,
            EnabledEvents = [NotificationEventType.SpamDetected],
            DigestMinutes = 15
        };
        var config = new NotificationConfig { Channels = [existingPref] };

        // Act
        var channel = config.GetOrCreateChannel(NotificationChannel.TelegramDm);

        // Assert
        Assert.That(channel, Is.SameAs(existingPref), "Should return same instance");
        Assert.That(config.Channels.Count, Is.EqualTo(1), "Should not create duplicate");
        Assert.That(channel.EnabledEvents, Contains.Item(NotificationEventType.SpamDetected));
    }

    [Test]
    public void GetOrCreateChannel_MultipleCallsSameChannel_ReturnsSameInstance()
    {
        // Arrange
        var config = new NotificationConfig();

        // Act
        var first = config.GetOrCreateChannel(NotificationChannel.WebPush);
        var second = config.GetOrCreateChannel(NotificationChannel.WebPush);
        var third = config.GetOrCreateChannel(NotificationChannel.WebPush);

        // Assert
        Assert.That(second, Is.SameAs(first), "Second call should return same instance");
        Assert.That(third, Is.SameAs(first), "Third call should return same instance");
        Assert.That(config.Channels.Count, Is.EqualTo(1), "Should only have one channel");
    }

    [Test]
    public void GetOrCreateChannel_DifferentChannels_CreatesSeparateInstances()
    {
        // Arrange
        var config = new NotificationConfig();

        // Act
        var telegram = config.GetOrCreateChannel(NotificationChannel.TelegramDm);
        var email = config.GetOrCreateChannel(NotificationChannel.Email);
        var webPush = config.GetOrCreateChannel(NotificationChannel.WebPush);

        // Assert
        Assert.That(config.Channels.Count, Is.EqualTo(3));
        Assert.That(telegram.Channel, Is.EqualTo(NotificationChannel.TelegramDm));
        Assert.That(email.Channel, Is.EqualTo(NotificationChannel.Email));
        Assert.That(webPush.Channel, Is.EqualTo(NotificationChannel.WebPush));
    }

    #endregion

    #region ChannelPreference Tests

    [Test]
    public void ChannelPreference_DefaultValues()
    {
        // Arrange & Act
        var pref = new ChannelPreference();

        // Assert
        Assert.That(pref.EnabledEvents, Is.Not.Null);
        Assert.That(pref.EnabledEvents.Count, Is.EqualTo(0));
        Assert.That(pref.DigestMinutes, Is.EqualTo(0), "Default digest should be 0 (immediate)");
    }

    [Test]
    public void ChannelPreference_EnabledEventsCanBeModified()
    {
        // Arrange
        var pref = new ChannelPreference { Channel = NotificationChannel.Email };

        // Act
        pref.EnabledEvents.Add(NotificationEventType.SpamDetected);
        pref.EnabledEvents.Add(NotificationEventType.BackupFailed);

        // Assert
        Assert.That(pref.EnabledEvents.Count, Is.EqualTo(2));
        Assert.That(pref.EnabledEvents, Contains.Item(NotificationEventType.SpamDetected));
        Assert.That(pref.EnabledEvents, Contains.Item(NotificationEventType.BackupFailed));
    }

    #endregion

    #region All NotificationEventType Coverage

    [Test]
    public void IsEnabled_AllEventTypes_CanBeEnabledIndividually()
    {
        // Arrange - Enable ALL event types for WebPush
        var allEvents = Enum.GetValues<NotificationEventType>().ToList();
        var config = new NotificationConfig
        {
            Channels =
            [
                new ChannelPreference
                {
                    Channel = NotificationChannel.WebPush,
                    EnabledEvents = allEvents
                }
            ]
        };

        // Act & Assert - All events should be enabled
        foreach (var eventType in allEvents)
        {
            Assert.That(config.IsEnabled(NotificationChannel.WebPush, eventType), Is.True,
                $"Event {eventType} should be enabled for WebPush");
        }
    }

    [Test]
    public void NotificationEventType_HasExpectedValues()
    {
        // Verify all expected event types exist (catches accidental enum changes)
        var allEvents = Enum.GetValues<NotificationEventType>().ToList();

        Assert.That(allEvents, Contains.Item(NotificationEventType.SpamDetected));
        Assert.That(allEvents, Contains.Item(NotificationEventType.SpamAutoDeleted));
        Assert.That(allEvents, Contains.Item(NotificationEventType.UserBanned));
        Assert.That(allEvents, Contains.Item(NotificationEventType.MessageReported));
        Assert.That(allEvents, Contains.Item(NotificationEventType.ChatHealthWarning));
        Assert.That(allEvents, Contains.Item(NotificationEventType.BackupFailed));
        Assert.That(allEvents, Contains.Item(NotificationEventType.MalwareDetected));
        Assert.That(allEvents, Contains.Item(NotificationEventType.ChatAdminChanged));
    }

    #endregion
}

using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for MessageBubbleTelegram.razor
/// Tests various display states based on message properties.
/// </summary>
[TestFixture]
public class MessageBubbleTelegramTests : MudBlazorTestContext
{
    /// <summary>
    /// Creates a minimal MessageRecord for testing.
    /// </summary>
    private static MessageRecord CreateMessage(
        string? firstName = "John",
        string? lastName = "Doe",
        string? userName = null,
        string? messageText = "Hello world",
        DateTimeOffset? editDate = null,
        DateTimeOffset? deletedAt = null,
        string? deletionSource = null,
        MessageTranslation? translation = null)
    {
        return new MessageRecord(
            MessageId: 123,
            User: new UserIdentity(456, firstName, lastName, userName),
            Chat: new ChatIdentity(789, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: messageText,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: editDate,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: deletedAt,
            DeletionSource: deletionSource,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: translation,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );
    }

    /// <summary>
    /// Creates a ContentCheckRecord for testing.
    /// </summary>
    private static ContentCheckRecord CreateContentCheck(bool isSpam, int confidence)
    {
        return new ContentCheckRecord(
            Id: 1,
            CheckTimestamp: DateTimeOffset.UtcNow,
            UserId: 456,
            ContentHash: "abc123",
            IsSpam: isSpam,
            Confidence: confidence,
            Reason: isSpam ? "Test spam" : "Test ham",
            CheckType: "test",
            MatchedMessageId: null
        );
    }

    /// <summary>
    /// Creates a MessageTranslation for testing.
    /// </summary>
    private static MessageTranslation CreateTranslation(string translatedText, string language)
    {
        return new MessageTranslation(
            Id: 1,
            MessageId: 123,
            EditId: null,
            TranslatedText: translatedText,
            DetectedLanguage: language,
            Confidence: 0.95m,
            TranslatedAt: DateTimeOffset.UtcNow
        );
    }

    #region User Display Name Tests

    [Test]
    public void DisplaysFullName_WhenFirstAndLastNameProvided()
    {
        // Arrange
        var message = CreateMessage(firstName: "John", lastName: "Doe");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var userName = cut.Find(".tg-user-name");
        Assert.That(userName.TextContent, Is.EqualTo("John Doe"));
    }

    [Test]
    public void DisplaysFirstNameOnly_WhenLastNameNull()
    {
        // Arrange
        var message = CreateMessage(firstName: "John", lastName: null);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var userName = cut.Find(".tg-user-name");
        Assert.That(userName.TextContent, Is.EqualTo("John"));
    }

    [Test]
    public void DisplaysUsername_WhenNoFirstOrLastName()
    {
        // Arrange
        var message = CreateMessage(firstName: null, lastName: null, userName: "johndoe");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert - Username displayed without @ prefix (per TelegramDisplayName.Format() design)
        var userName = cut.Find(".tg-user-name");
        Assert.That(userName.TextContent, Is.EqualTo("johndoe"));
    }

    [Test]
    public void DisplaysUserId_WhenNoNameOrUsername()
    {
        // Arrange
        var message = CreateMessage(firstName: null, lastName: null, userName: null);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var userName = cut.Find(".tg-user-name");
        Assert.That(userName.TextContent, Is.EqualTo("User 456"));
    }

    #endregion

    #region Spam/Ham Badge Tests

    [Test]
    public void DisplaysSpamBadge_WhenContentCheckIsSpam()
    {
        // Arrange
        var message = CreateMessage();
        var contentCheck = CreateContentCheck(isSpam: true, confidence: 95);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.ContentCheck, contentCheck));

        // Assert
        var badge = cut.Find(".tg-badge-spam");
        Assert.That(badge.TextContent, Does.Contain("spam"));
        Assert.That(badge.TextContent, Does.Contain("95%"));
    }

    [Test]
    public void DisplaysHamBadge_WhenContentCheckIsHam()
    {
        // Arrange
        var message = CreateMessage();
        var contentCheck = CreateContentCheck(isSpam: false, confidence: 85);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.ContentCheck, contentCheck));

        // Assert
        var badge = cut.Find(".tg-badge-ham");
        Assert.That(badge.TextContent, Does.Contain("ham"));
        Assert.That(badge.TextContent, Does.Contain("85%"));
    }

    [Test]
    public void NoBadge_WhenNoContentCheck()
    {
        // Arrange
        var message = CreateMessage();

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(cut.FindAll(".tg-badge-spam"), Is.Empty);
            Assert.That(cut.FindAll(".tg-badge-ham"), Is.Empty);
        }
    }

    #endregion

    #region Message State Tests

    [Test]
    public void DisplaysDeletedBadge_WhenMessageDeleted()
    {
        // Arrange
        var message = CreateMessage(
            deletedAt: DateTimeOffset.UtcNow,
            deletionSource: "admin");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var badge = cut.Find(".tg-badge-deleted");
        Assert.That(badge.TextContent, Does.Contain("DELETED"));
    }

    [Test]
    public void DisplaysEditedLabel_WhenMessageEdited()
    {
        // Arrange
        var message = CreateMessage(editDate: DateTimeOffset.UtcNow);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var edited = cut.Find(".tg-edited");
        Assert.That(edited.TextContent, Is.EqualTo("edited"));
    }

    [Test]
    public void NoEditedLabel_WhenMessageNotEdited()
    {
        // Arrange
        var message = CreateMessage(editDate: null);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        Assert.That(cut.FindAll(".tg-edited"), Is.Empty);
    }

    #endregion

    #region Styling Tests

    [Test]
    public void AppliesSpamClass_WhenContentCheckIsSpam()
    {
        // Arrange
        var message = CreateMessage();
        var contentCheck = CreateContentCheck(isSpam: true, confidence: 90);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.ContentCheck, contentCheck));

        // Assert
        var bubble = cut.Find(".tg-message");
        Assert.That(bubble.ClassList, Does.Contain("tg-message-spam"));
    }

    [Test]
    public void AppliesHamClass_WhenContentCheckIsHam()
    {
        // Arrange
        var message = CreateMessage();
        var contentCheck = CreateContentCheck(isSpam: false, confidence: 90);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.ContentCheck, contentCheck));

        // Assert
        var bubble = cut.Find(".tg-message");
        Assert.That(bubble.ClassList, Does.Contain("tg-message-ham"));
    }

    [Test]
    public void AppliesCurrentUserClass_WhenIsCurrentUser()
    {
        // Arrange
        var message = CreateMessage();

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.IsCurrentUser, true));

        // Assert
        var bubble = cut.Find(".tg-message");
        Assert.That(bubble.ClassList, Does.Contain("tg-message-current-user"));
    }

    #endregion

    #region Translation Tests

    [Test]
    public void DisplaysTranslationBadge_WhenTranslationAvailable()
    {
        // Arrange
        var translation = CreateTranslation("Hello world", "es");
        var message = CreateMessage(messageText: "Hola mundo", translation: translation);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var badge = cut.Find(".tg-badge-translation");
        Assert.That(badge.TextContent, Does.Contain("ES→EN"));
    }

    [Test]
    public void ShowsTranslatedText_ByDefault()
    {
        // Arrange
        var translation = CreateTranslation("Hello world", "es");
        var message = CreateMessage(messageText: "Hola mundo", translation: translation);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var textDiv = cut.Find(".tg-text-translated");
        Assert.That(textDiv.TextContent, Is.EqualTo("Hello world"));
    }

    #endregion

    #region Message Text Tests

    [Test]
    public void DisplaysMessageText()
    {
        // Arrange
        var message = CreateMessage(messageText: "This is a test message");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert
        var text = cut.Find(".tg-text");
        Assert.That(text.TextContent, Does.Contain("This is a test message"));
    }

    [Test]
    public void HandlesEmptyMessageText()
    {
        // Arrange
        var message = CreateMessage(messageText: null);

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert - should not throw, just not display text
        Assert.That(cut.FindAll(".tg-text"), Is.Empty);
    }

    #endregion

    #region OnViewUserDetail Tests

    [Test]
    public void InvokesOnViewUserDetail_WhenUsernameClicked()
    {
        // Arrange
        var message = CreateMessage(firstName: "John");
        long? capturedUserId = null;

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.OnViewUserDetail, EventCallback.Factory.Create<long>(this, id => capturedUserId = id)));

        cut.Find(".tg-user-name").Click();

        // Assert
        Assert.That(capturedUserId, Is.EqualTo(456L)); // 456 is the UserId from CreateMessage helper
    }

    [Test]
    public void InvokesOnViewUserDetail_WhenAvatarClicked()
    {
        // Arrange
        var message = CreateMessage(firstName: "John");
        long? capturedUserId = null;

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message)
            .Add(x => x.OnViewUserDetail, EventCallback.Factory.Create<long>(this, id => capturedUserId = id)));

        cut.Find(".tg-avatar-container").Click();

        // Assert
        Assert.That(capturedUserId, Is.EqualTo(456L));
    }

    [Test]
    public void DoesNotThrow_WhenOnViewUserDetailNotProvided()
    {
        // Arrange - no callback provided (optional EventCallback parameter)
        var message = CreateMessage(firstName: "John");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert - clicking should not throw when callback not bound
        Assert.DoesNotThrow(() => cut.Find(".tg-user-name").Click());
    }

    [Test]
    public void DoesNotThrow_WhenAvatarClickedWithoutCallback()
    {
        // Arrange - no callback provided
        var message = CreateMessage(firstName: "John");

        // Act
        var cut = Render<MessageBubbleTelegram>(p => p
            .Add(x => x.Message, message));

        // Assert - avatar click should also not throw
        Assert.DoesNotThrow(() => cut.Find(".tg-avatar-container").Click());
    }

    #endregion
}

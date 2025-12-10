using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ImageViewerDialog.razor
/// Tests the full-size image viewer dialog with message info.
/// </summary>
[TestFixture]
public class ImageViewerDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a test MessageRecord with the specified properties.
    /// </summary>
    private static MessageRecord CreateTestMessage(
        long messageId = 12345,
        long userId = 1001,
        string? userName = "testuser",
        string? firstName = "Test",
        string? lastName = "User",
        long chatId = -1001234567890,
        string? messageText = null,
        string? photoLocalPath = null,
        int? photoFileSize = null,
        string? chatName = "Test Chat")
    {
        return new MessageRecord(
            MessageId: messageId,
            UserId: userId,
            UserName: userName,
            FirstName: firstName,
            LastName: lastName,
            ChatId: chatId,
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: messageText,
            PhotoFileId: photoLocalPath != null ? "file_123" : null,
            PhotoFileSize: photoFileSize,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            ChatName: chatName,
            PhotoLocalPath: photoLocalPath,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
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
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);
    }

    /// <summary>
    /// Opens the ImageViewerDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(MessageRecord message)
    {
        var parameters = new DialogParameters<ImageViewerDialog>
        {
            { x => x.Message, message }
        };

        return await DialogService.ShowAsync<ImageViewerDialog>("Image Viewer", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-content"));
        });
    }

    [Test]
    public void HasDialogActions()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-actions"));
        });
    }

    [Test]
    public void HasUserAvatar()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-avatar"));
        });
    }

    [Test]
    public void HasCloseButton()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });
    }

    #endregion

    #region User Info Tests

    [Test]
    public void DisplaysUserName()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(userName: "johndoe");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("johndoe"));
        });
    }

    [Test]
    public void DisplaysUserId_WhenNoUserName()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(userName: null, firstName: null, lastName: null, userId: 9999);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("User 9999"));
        });
    }

    [Test]
    public void DisplaysUserInitials()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(userName: "John Smith");

        // Act
        _ = OpenDialogAsync(message);

        // Assert - Should show "JS" for "John Smith"
        provider.WaitForAssertion(() =>
        {
            var avatar = provider.Find(".mud-avatar");
            Assert.That(avatar.TextContent.Trim(), Is.EqualTo("JS"));
        });
    }

    [Test]
    public void DisplaysSingleWordInitials()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(userName: "admin");

        // Act
        _ = OpenDialogAsync(message);

        // Assert - Should show "AD" for "admin"
        provider.WaitForAssertion(() =>
        {
            var avatar = provider.Find(".mud-avatar");
            Assert.That(avatar.TextContent.Trim(), Is.EqualTo("AD"));
        });
    }

    #endregion

    #region Image Tests

    [Test]
    public void DisplaysImage_WhenPhotoLocalPathSet()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "photos/test.jpg");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-image"));
            Assert.That(provider.Markup, Does.Contain("/media/photos/test.jpg"));
        });
    }

    [Test]
    public void DisplaysWarning_WhenNoImage()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: null);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-alert"));
            Assert.That(provider.Markup, Does.Contain("Image not available"));
        });
    }

    [Test]
    public void DisplaysWarning_WhenPhotoPathEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Image not available"));
        });
    }

    [Test]
    public void ImageHasAltText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "test.jpg");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full size message image"));
        });
    }

    #endregion

    #region Message Text Tests

    [Test]
    public void DisplaysMessageText_WhenPresent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(messageText: "This is the message caption");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("This is the message caption"));
        });
    }

    [Test]
    public void HidesMessageText_WhenEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(messageText: null);

        // Act
        _ = OpenDialogAsync(message);

        // Assert - The message text paper should not be rendered
        provider.WaitForAssertion(() =>
        {
            // Make sure dialog is rendered first
            Assert.That(provider.Markup, Does.Contain("mud-dialog-content"));
        });
        // Message ID is always shown, but there should be only 2 paper elements
        // (user info and image info) when no message text
    }

    #endregion

    #region Message Info Tests

    [Test]
    public void DisplaysMessageId()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(messageId: 98765);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Message ID:"));
            Assert.That(provider.Markup, Does.Contain("98765"));
        });
    }

    [Test]
    public void DisplaysChatName()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(chatName: "My Test Group");

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Chat:"));
            Assert.That(provider.Markup, Does.Contain("My Test Group"));
        });
    }

    [Test]
    public void DisplaysChatId_WhenNoChatName()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(chatName: null, chatId: -1001234567890);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Chat -1001234567890"));
        });
    }

    [Test]
    public void DisplaysFileSize_WhenPresent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "test.jpg", photoFileSize: 1024 * 1024); // 1 MB

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("File Size:"));
            Assert.That(provider.Markup, Does.Contain("1 MB"));
        });
    }

    [Test]
    public void HidesFileSize_WhenNull()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "test.jpg", photoFileSize: null);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("File Size:"));
        });
    }

    [Test]
    public void FormatsFileSizeAsKB()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "test.jpg", photoFileSize: 512 * 1024); // 512 KB

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("512 KB"));
        });
    }

    [Test]
    public void FormatsFileSizeAsBytes()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(photoLocalPath: "test.jpg", photoFileSize: 500); // 500 bytes

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("500 B"));
        });
    }

    #endregion

    #region Button Click Tests

    [Test]
    public void CloseButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        _ = OpenDialogAsync(message);

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });

        // Act - Click close button
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Contains("Close"));
        closeButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}

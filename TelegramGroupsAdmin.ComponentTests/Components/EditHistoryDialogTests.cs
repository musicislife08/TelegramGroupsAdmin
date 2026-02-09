using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for EditHistoryDialog tests.
/// Registers mocked IMessageEditService and IMessageTranslationService.
/// </summary>
public class EditHistoryDialogTestContext : BunitContext
{
    protected IMessageEditService MessageEditService { get; }
    protected IMessageTranslationService MessageTranslationService { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected EditHistoryDialogTestContext()
    {
        // Create mocks
        MessageEditService = Substitute.For<IMessageEditService>();
        MessageTranslationService = Substitute.For<IMessageTranslationService>();
        var logger = Substitute.For<ILogger<EditHistoryDialog>>();

        // Default: return empty edit list
        MessageEditService.GetEditsForMessageAsync(Arg.Any<long>()).Returns([]);
        MessageTranslationService.GetTranslationForEditAsync(Arg.Any<long>()).Returns((MessageTranslation?)null);

        // Register mocks
        Services.AddSingleton(MessageEditService);
        Services.AddSingleton(MessageTranslationService);
        Services.AddSingleton(logger);

        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Setup JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for EditHistoryDialog.razor
/// Tests the dialog that displays message edit history with diff view.
/// </summary>
[TestFixture]
public class EditHistoryDialogTests : EditHistoryDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        MessageEditService.ClearReceivedCalls();
        MessageTranslationService.ClearReceivedCalls();
        MessageEditService.GetEditsForMessageAsync(Arg.Any<long>()).Returns([]);
    }

    #region Helper Methods

    private static MessageRecord CreateTestMessage(
        long messageId = 83973,
        long userId = 1234567890,
        string userName = "johndoe42",
        string messageText = "This is a test message for the edit history dialog.")
    {
        return new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(userId, "John", "Doe", userName),
            Chat: new ChatIdentity(-1001234567890, "Test Community Group"),
            Timestamp: DateTimeOffset.UtcNow.AddHours(-1),
            MessageText: messageText,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: "CCF638619BB83D56E788526C226A2C529318570431F2D285840E4DE1AE300D58",
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: "chat_icons/1001234567890.jpg",
            UserPhotoPath: "user_photos/1234567890.jpg",
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
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );
    }

    private static MessageEditRecord CreateTestEdit(
        long id = 355,
        long messageId = 83973,
        string oldText = "Aww,,, my starlink speeds are back to normal today",
        string newText = "Aww... my starlink speeds are back to normal today")
    {
        return new MessageEditRecord(
            Id: id,
            MessageId: messageId,
            OldText: oldText,
            NewText: newText,
            EditDate: DateTimeOffset.UtcNow.AddMinutes(-30),
            OldContentHash: "C4594CA899B1856621674544A15AE6CD680ED92269CD2159BC38D3DFAE7A3B37",
            NewContentHash: "8C4265F520F4E2EE52A6FBAD839697DDA03F3A9F572C85D2F09112F1C5525CE6"
        );
    }

    private async Task<IDialogReference> OpenDialogAsync(MessageRecord message)
    {
        var parameters = new DialogParameters<EditHistoryDialog>
        {
            { x => x.Message, message }
        };
        return await DialogService.ShowAsync<EditHistoryDialog>("Edit History", parameters);
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

    #region Empty State Tests

    [Test]
    public void ShowsNoHistoryAlert_WhenNoEdits()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns([]);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No edit history found"));
        });
    }

    #endregion

    #region Message Info Tests

    [Test]
    public void DisplaysUserInfo_WhenEditsExist()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(userName: "testuser");
        List<MessageEditRecord> edits = [CreateTestEdit()];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert - DisplayName prioritizes FirstName+LastName over Username (matches Telegram UI behavior)
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("John Doe"));
        });
    }

    [Test]
    public void DisplaysMessageId_WhenEditsExist()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage(messageId: 12345);
        List<MessageEditRecord> edits = [CreateTestEdit(messageId: 12345)];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("12345"));
        });
    }

    #endregion

    #region Edit Count Tests

    [Test]
    public void DisplaysEditCount_Singular()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        List<MessageEditRecord> edits = [CreateTestEdit()];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("1 Edit"));
        });
    }

    [Test]
    public void DisplaysEditCount_Plural()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        List<MessageEditRecord> edits =
        [
            CreateTestEdit(id: 1),
            CreateTestEdit(id: 2),
            CreateTestEdit(id: 3)
        ];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("3 Edits"));
        });
    }

    #endregion

    #region Diff Display Tests

    [Test]
    public void DisplaysDiffContainer_WhenEditsExist()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        List<MessageEditRecord> edits = [CreateTestEdit()];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("diff-container"));
        });
    }

    [Test]
    public void DisplaysDiffSummaryChips()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var message = CreateTestMessage();
        List<MessageEditRecord> edits = [CreateTestEdit()];
        MessageEditService.GetEditsForMessageAsync(message.MessageId).Returns(edits);

        // Act
        _ = OpenDialogAsync(message);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("removed"));
            Assert.That(provider.Markup, Does.Contain("added"));
            Assert.That(provider.Markup, Does.Contain("unchanged"));
        });
    }

    #endregion

    #region Button Tests

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
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Close");
        closeButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}

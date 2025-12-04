using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Reports;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core;
using TelegramModels = TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Base context for ModerationReportCard tests that registers mocks before MudBlazor services.
/// bUnit 2.x requires all services to be registered before the first Render() call.
/// </summary>
public class ModerationReportCardTestContext : BunitContext
{
    protected IMessageHistoryRepository MessageRepository { get; }
    protected ITelegramUserRepository UserRepository { get; }

    protected ModerationReportCardTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        MessageRepository = Substitute.For<IMessageHistoryRepository>();
        UserRepository = Substitute.For<ITelegramUserRepository>();

        // Register mocks
        Services.AddSingleton(MessageRepository);
        Services.AddSingleton(UserRepository);

        // THEN add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Set up JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true);
        JSInterop.SetupVoid("mudPopover.connect", _ => true);
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>
/// Component tests for ModerationReportCard.razor
/// Tests report display, action buttons, and mocked repository interactions.
/// Uses realistic data patterns based on production spam reports.
/// </summary>
[TestFixture]
public class ModerationReportCardTests : ModerationReportCardTestContext
{
    /// <summary>
    /// Clear mock received calls before each test to ensure test isolation.
    /// NSubstitute tracks all calls, so we must reset between tests.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        MessageRepository.ClearReceivedCalls();
        UserRepository.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Creates a Report with realistic data patterns.
    /// </summary>
    private static Report CreateReport(
        long id = 1,
        int messageId = 212408,
        long chatId = -1001329174109, // Supergroup format
        long? reportedByUserId = 935157741,
        string? reportedByUserName = "TestReporter",
        ReportStatus status = ReportStatus.Pending,
        string? actionTaken = null,
        string? webUserId = null)
    {
        return new Report(
            Id: id,
            MessageId: messageId,
            ChatId: chatId,
            ReportCommandMessageId: reportedByUserId.HasValue ? 212409 : null,
            ReportedByUserId: reportedByUserId,
            ReportedByUserName: reportedByUserName,
            ReportedAt: DateTimeOffset.UtcNow.AddHours(-2),
            Status: status,
            ReviewedBy: status != ReportStatus.Pending ? "admin@test.com" : null,
            ReviewedAt: status != ReportStatus.Pending ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            ActionTaken: actionTaken,
            AdminNotes: null,
            WebUserId: webUserId);
    }

    /// <summary>
    /// Creates a MessageRecord with realistic spam message pattern.
    /// </summary>
    private static MessageRecord CreateSpamMessage(
        long messageId = 212408,
        long userId = 8119068862,
        string? messageText = "Good evening everyone!\nI'm buying cryptocurrency (USDT) for cash.\nAmounts up to $120,000.\nIf interested, message me.",
        string? photoFileId = null,
        DateTimeOffset? deletedAt = null,
        string? deletionSource = null)
    {
        return new MessageRecord(
            MessageId: messageId,
            UserId: userId,
            UserName: "crypto_spam_user",
            FirstName: "Crypto",
            LastName: "Spammer",
            ChatId: -1001329174109,
            Timestamp: DateTimeOffset.UtcNow.AddHours(-3),
            MessageText: messageText,
            PhotoFileId: photoFileId,
            PhotoFileSize: photoFileId != null ? 50000 : null,
            Urls: null,
            EditDate: null,
            ContentHash: "abc123",
            ChatName: "Test Group",
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
            Translation: null,
            ContentCheckSkipReason: TelegramModels.ContentCheckSkipReason.NotSkipped);
    }

    /// <summary>
    /// Creates a TelegramUser with realistic data.
    /// </summary>
    private static TelegramUser CreateTelegramUser(
        long telegramUserId = 8119068862,
        string? username = "spam_account",
        string? firstName = "Spam",
        string? lastName = "Account")
    {
        return new TelegramUser(
            TelegramUserId: telegramUserId,
            Username: username,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-7),
            LastSeenAt: DateTimeOffset.UtcNow.AddHours(-3),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt: DateTimeOffset.UtcNow.AddHours(-3));
    }

    #endregion

    #region Basic Rendering Tests

    [Test]
    public async Task DisplaysReportStatus_Pending()
    {
        // Arrange
        var report = CreateReport(status: ReportStatus.Pending);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        // Wait for async operations
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Pending"));
    }

    [Test]
    public async Task DisplaysReportStatus_Reviewed()
    {
        // Arrange
        var report = CreateReport(status: ReportStatus.Reviewed, actionTaken: "spam");
        var message = CreateSpamMessage(deletedAt: DateTimeOffset.UtcNow, deletionSource: "spam_action");

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Reviewed"));
        Assert.That(cut.Markup, Does.Contain("Action taken: spam"));
    }

    [Test]
    public async Task DisplaysMessageText()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(messageText: "Buy crypto now! Great rates!");

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Buy crypto now!"));
    }

    [Test]
    public async Task DisplaysNoText_WhenMessageTextNull()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(messageText: null);

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("[No text]"));
    }

    #endregion

    #region Message Not Found Tests

    [Test]
    public async Task DisplaysWarning_WhenMessageNotFound()
    {
        // Arrange
        var report = CreateReport();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Message not found in cache"));
    }

    #endregion

    #region Photo and Deleted Badge Tests

    [Test]
    public async Task DisplaysHasImageChip_WhenPhotoAttached()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(photoFileId: "AgACAgIAAxkBAAI...");

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Has Image"));
    }

    [Test]
    public async Task DisplaysDeletedChip_WhenMessageDeleted()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(
            deletedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            deletionSource: "spam_action");

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Deleted"));
    }

    #endregion

    #region Action Buttons Tests

    [Test]
    public async Task ShowsActionButtons_WhenStatusPending()
    {
        // Arrange
        var report = CreateReport(status: ReportStatus.Pending);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - should show action buttons
        Assert.That(cut.Markup, Does.Contain("Delete as Spam"));
        Assert.That(cut.Markup, Does.Contain("Ban User"));
        Assert.That(cut.Markup, Does.Contain("Warn"));
        Assert.That(cut.Markup, Does.Contain("Dismiss"));
    }

    [Test]
    public async Task HidesActionButtons_WhenStatusReviewed()
    {
        // Arrange
        var report = CreateReport(status: ReportStatus.Reviewed, actionTaken: "spam");
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - should NOT show action buttons
        Assert.That(cut.Markup, Does.Not.Contain("Delete as Spam"));
        Assert.That(cut.Markup, Does.Not.Contain("Ban User"));
    }

    #endregion

    #region Reporter Display Tests

    [Test]
    public async Task DisplaysTelegramReporter()
    {
        // Arrange
        var report = CreateReport(
            reportedByUserId: 935157741,
            reportedByUserName: "ReporterUser");
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("ReporterUser"));
        Assert.That(cut.Markup, Does.Contain("ID: 935157741"));
    }

    [Test]
    public async Task DisplaysSystemReporter_ForAutoDetection()
    {
        // Arrange - Auto-detection report (no user ID)
        const long specificReporterId = 935157741;
        var report = CreateReport(
            reportedByUserId: null,
            reportedByUserName: "Auto-Detection");
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - Auto-Detection reporter should NOT show a Telegram user ID
        // Note: The spammer's user ID is still shown, so we check specifically for reporter ID format
        Assert.That(cut.Markup, Does.Contain("Auto-Detection"));
        Assert.That(cut.Markup, Does.Not.Contain($"ID: {specificReporterId}")); // No reporter ID shown
    }

    [Test]
    public async Task DisplaysWebReporter()
    {
        // Arrange - Web UI report
        var report = CreateReport(
            reportedByUserId: null,
            reportedByUserName: "WebAdmin",
            webUserId: "user-guid-123");
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("WebAdmin"));
        Assert.That(cut.Markup, Does.Contain("Web User"));
    }

    #endregion

    #region Telegram Link Tests

    [Test]
    public async Task ShowsTelegramLink_ForSupergroup()
    {
        // Arrange - Supergroup chat ID starts with -100
        var report = CreateReport(chatId: -1001329174109, messageId: 212408);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - should have Telegram link (https://t.me/c/{numericId}/{messageId})
        Assert.That(cut.Markup, Does.Contain("Open in Telegram"));
        Assert.That(cut.Markup, Does.Contain("t.me/c/1329174109/212408"));
    }

    [Test]
    public async Task HidesTelegramLink_ForNonSupergroup()
    {
        // Arrange - Regular group (doesn't start with -100)
        var report = CreateReport(chatId: -123456789);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - should NOT have Telegram link
        Assert.That(cut.Markup, Does.Not.Contain("Open in Telegram"));
    }

    #endregion

    #region User Display Tests

    [Test]
    public async Task DisplaysUserFromRepository()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage();
        var user = CreateTelegramUser(firstName: "John", lastName: "Spammer");

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - uses TelegramDisplayName.Format which prefers full name
        Assert.That(cut.Markup, Does.Contain("John Spammer"));
    }

    [Test]
    public async Task DisplaysUserFromMessage_WhenRepositoryReturnsNull()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(); // Has FirstName="Crypto", LastName="Spammer"

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns((TelegramUser?)null);

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);
        cut.Render();

        // Assert - falls back to message data
        Assert.That(cut.Markup, Does.Contain("Crypto Spammer"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnAction_WhenSpamButtonClicked()
    {
        // Arrange
        (Report report, string action)? receivedAction = null;
        var report = CreateReport(status: ReportStatus.Pending);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(Report, string)>(
                this, args => receivedAction = args)));

        await Task.Delay(50);
        cut.Render();

        // Act - find and click the "Delete as Spam" button
        var spamButton = cut.FindAll("button").First(b => b.TextContent.Contains("Delete as Spam"));
        await spamButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("spam"));
        Assert.That(receivedAction!.Value.report.Id, Is.EqualTo(report.Id));
    }

    [Test]
    public async Task InvokesOnAction_WhenDismissButtonClicked()
    {
        // Arrange
        (Report report, string action)? receivedAction = null;
        var report = CreateReport(status: ReportStatus.Pending);
        var message = CreateSpamMessage();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        UserRepository.GetByTelegramIdAsync(message.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateTelegramUser());

        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(Report, string)>(
                this, args => receivedAction = args)));

        await Task.Delay(50);
        cut.Render();

        // Act
        var dismissButton = cut.FindAll("button").First(b => b.TextContent.Contains("Dismiss"));
        await dismissButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("dismiss"));
    }

    #endregion

    #region Repository Interaction Tests

    [Test]
    public async Task CallsMessageRepository_OnRender()
    {
        // Arrange
        var report = CreateReport(messageId: 12345);

        MessageRepository.GetMessageAsync(12345, Arg.Any<CancellationToken>())
            .Returns(CreateSpamMessage(messageId: 12345));

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);

        // Assert
        await MessageRepository.Received(1).GetMessageAsync(12345, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CallsUserRepository_WhenMessageFound()
    {
        // Arrange
        var report = CreateReport();
        var message = CreateSpamMessage(userId: 999888777);

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);

        // Assert
        await UserRepository.Received(1).GetByTelegramIdAsync(999888777, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DoesNotCallUserRepository_WhenMessageNotFound()
    {
        // Arrange
        var report = CreateReport();

        MessageRepository.GetMessageAsync(report.MessageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act
        var cut = Render<ModerationReportCard>(p => p
            .Add(x => x.Report, report));

        await Task.Delay(50);

        // Assert
        await UserRepository.DidNotReceive().GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    #endregion
}

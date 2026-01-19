using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Reports;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Custom test context for ImpersonationAlertCard with mocked dependencies.
/// </summary>
public abstract class ImpersonationAlertCardTestContext : BunitContext
{
    protected ITelegramUserRepository TelegramUserRepository { get; }
    protected IMessageHistoryRepository MessageRepository { get; }
    protected IManagedChatsRepository ManagedChatsRepository { get; }

    protected ImpersonationAlertCardTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        TelegramUserRepository = Substitute.For<ITelegramUserRepository>();
        MessageRepository = Substitute.For<IMessageHistoryRepository>();
        ManagedChatsRepository = Substitute.For<IManagedChatsRepository>();

        // Configure default mock behavior
        TelegramUserRepository.GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((TelegramUser?)null);
        MessageRepository.GetMessageCountAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(0);
        MessageRepository.GetUserMessagesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserMessageInfo>());
        ManagedChatsRepository.GetActiveChatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        // Register mocks
        Services.AddSingleton(TelegramUserRepository);
        Services.AddSingleton(MessageRepository);
        Services.AddSingleton(ManagedChatsRepository);

        // THEN add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Set up JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>
/// Component tests for ImpersonationAlertCard.razor
/// Tests impersonation alert display, risk levels, match indicators, and action buttons.
/// </summary>
[TestFixture]
public class ImpersonationAlertCardTests : ImpersonationAlertCardTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates an ImpersonationAlertRecord with realistic data patterns.
    /// </summary>
    private static ImpersonationAlertRecord CreateAlert(
        int id = 1,
        long suspectedUserId = 7123456789,
        long targetUserId = 935157741,
        long chatId = -1001329174109,
        int totalScore = 100,
        ImpersonationRiskLevel riskLevel = ImpersonationRiskLevel.Critical,
        bool nameMatch = true,
        bool photoMatch = true,
        double? photoSimilarityScore = 0.95,
        bool autoBanned = true,
        string? reviewedByUserId = null,
        DateTimeOffset? reviewedAt = null,
        ImpersonationVerdict? verdict = null,
        string? suspectedUserName = "fake_admin",
        string? suspectedFirstName = "John",
        string? suspectedLastName = "Admin",
        string? suspectedPhotoPath = null,
        string? targetUserName = "real_admin",
        string? targetFirstName = "John",
        string? targetLastName = "Admin",
        string? targetPhotoPath = null,
        string? chatName = "Test Crypto Group")
    {
        return new ImpersonationAlertRecord
        {
            Id = id,
            SuspectedUserId = suspectedUserId,
            TargetUserId = targetUserId,
            ChatId = chatId,
            TotalScore = totalScore,
            RiskLevel = riskLevel,
            NameMatch = nameMatch,
            PhotoMatch = photoMatch,
            PhotoSimilarityScore = photoSimilarityScore,
            DetectedAt = DateTimeOffset.UtcNow.AddHours(-2),
            AutoBanned = autoBanned,
            ReviewedByUserId = reviewedByUserId,
            ReviewedAt = reviewedAt,
            Verdict = verdict,
            SuspectedUserName = suspectedUserName,
            SuspectedFirstName = suspectedFirstName,
            SuspectedLastName = suspectedLastName,
            SuspectedPhotoPath = suspectedPhotoPath,
            TargetUserName = targetUserName,
            TargetFirstName = targetFirstName,
            TargetLastName = targetLastName,
            TargetPhotoPath = targetPhotoPath,
            ChatName = chatName
        };
    }

    #endregion

    #region Basic Rendering Tests

    [Test]
    public void DisplaysImpersonationAlertTitle()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Impersonation Alert"));
    }

    [Test]
    public void DisplaysTotalScore()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(totalScore: 85)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("85/100"));
    }

    [Test]
    public void DisplaysChatName()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(chatName: "My Test Group")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("My Test Group"));
    }

    #endregion

    #region Risk Level Tests

    [Test]
    public void DisplaysCriticalRiskLevel()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(riskLevel: ImpersonationRiskLevel.Critical)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Critical"));
    }

    [Test]
    public void DisplaysMediumRiskLevel()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                riskLevel: ImpersonationRiskLevel.Medium,
                totalScore: 50,
                nameMatch: true,
                photoMatch: false)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Medium"));
    }

    #endregion

    #region Match Indicator Tests

    [Test]
    public void DisplaysNameMatchIndicator()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(nameMatch: true)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Name Match"));
    }

    [Test]
    public void HidesNameMatchIndicator_WhenNoMatch()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(nameMatch: false)));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("Name Match"));
    }

    [Test]
    public void DisplaysPhotoMatchIndicator()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(photoMatch: true, photoSimilarityScore: 0.87)));

        // Assert - Component shows "Photo {percentage}" (e.g., "Photo 87%")
        Assert.That(cut.Markup, Does.Match(@"Photo\s+87\s?%"));
    }

    [Test]
    public void HidesPhotoMatchIndicator_WhenNoMatch()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(photoMatch: false, photoSimilarityScore: null)));

        // Assert - No photo chip when photoMatch is false
        Assert.That(cut.Markup, Does.Not.Match(@"Photo\s+\d+\s?%"));
    }

    #endregion

    #region User Display Tests

    [Test]
    public void DisplaysSuspectedUserInfo()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                suspectedFirstName: "Scammer",
                suspectedLastName: "Guy",
                suspectedUserName: "scammer123",
                suspectedUserId: 7123456789)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Suspected Impersonator"));
        Assert.That(cut.Markup, Does.Contain("Scammer Guy")); // Display name
        Assert.That(cut.Markup, Does.Contain("scammer123")); // Username
        Assert.That(cut.Markup, Does.Contain("ID: 7123456789"));
    }

    [Test]
    public void DisplaysTargetUserInfo()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                targetFirstName: "Real",
                targetLastName: "Admin",
                targetUserName: "realadmin",
                targetUserId: 935157741)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Target Being Copied"));
        Assert.That(cut.Markup, Does.Contain("Real Admin")); // Display name
        Assert.That(cut.Markup, Does.Contain("realadmin")); // Username
        Assert.That(cut.Markup, Does.Contain("ID: 935157741"));
    }

    [Test]
    public void DisplaysNoUsername_WhenUsernameIsNull()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                suspectedUserName: null,
                targetUserName: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("No username").IgnoreCase);
    }

    #endregion

    #region Action Button Tests

    [Test]
    public void ShowsActionButtons_WhenNotReviewed()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(reviewedAt: null)));

        // Assert - Component shows: Confirm Scam, False Positive, Trust
        Assert.That(cut.Markup, Does.Contain("Confirm Scam"));
        Assert.That(cut.Markup, Does.Contain("False Positive"));
        Assert.That(cut.Markup, Does.Contain("Trust"));
    }

    [Test]
    public void HidesActionButtons_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                reviewedAt: DateTimeOffset.UtcNow.AddHours(-1),
                reviewedByUserId: "admin@test.com",
                verdict: ImpersonationVerdict.ConfirmedScam)));

        // Assert - Action buttons hidden when already reviewed
        Assert.That(cut.Markup, Does.Not.Contain("Confirm Scam"));
        Assert.That(cut.Markup, Does.Not.Contain(">False Positive<")); // Not in button form
        Assert.That(cut.Markup, Does.Not.Contain(">Trust<")); // Not in button form
    }

    [Test]
    public void DisplaysVerdict_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                reviewedAt: DateTimeOffset.UtcNow.AddHours(-1),
                reviewedByUserId: "admin@test.com",
                verdict: ImpersonationVerdict.ConfirmedScam)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Verdict:"));
        Assert.That(cut.Markup, Does.Contain("ConfirmedScam"));
    }

    [Test]
    public void DisplaysReviewedInfo_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                reviewedAt: DateTimeOffset.UtcNow.AddHours(-1),
                reviewedByUserId: "admin@test.com",
                verdict: ImpersonationVerdict.FalsePositive)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Reviewed"));
        Assert.That(cut.Markup, Does.Contain("FalsePositive"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnAction_WhenConfirmScamClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var confirmButton = cut.FindAll("button").First(b => b.TextContent.Contains("Confirm Scam"));
        await confirmButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("confirm"));
        Assert.That(receivedAction!.Value.alert.Id, Is.EqualTo(alert.Id));
    }

    [Test]
    public async Task InvokesOnAction_WhenTrustClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var trustButton = cut.FindAll("button").First(b => b.TextContent.Contains("Trust"));
        await trustButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("trust"));
    }

    [Test]
    public async Task InvokesOnAction_WhenFalsePositiveClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var falsePositiveButton = cut.FindAll("button").First(b => b.TextContent.Contains("False Positive"));
        await falsePositiveButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("dismiss"));
    }

    #endregion

    #region Card Styling Tests

    [Test]
    public void AppliesCriticalBorderStyle()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(riskLevel: ImpersonationRiskLevel.Critical)));

        // Assert - Critical risk level adds border-left style
        var card = cut.Find(".mud-card");
        Assert.That(card.GetAttribute("style"), Does.Contain("border-left"));
    }

    [Test]
    public void DoesNotApplyCriticalBorderStyle_ForMediumRisk()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                riskLevel: ImpersonationRiskLevel.Medium,
                totalScore: 50)));

        // Assert - Medium risk level should not have the critical border
        var card = cut.Find(".mud-card");
        var style = card.GetAttribute("style") ?? "";
        Assert.That(style, Does.Not.Contain("border-left"));
    }

    #endregion

    #region Photo Display Tests

    [Test]
    public void DisplaysPhotoPlaceholder_WhenNoPhoto()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                suspectedPhotoPath: null,
                targetPhotoPath: null)));

        // Assert - Should show MudIcon placeholders instead of images
        // When no photo path, MudIcon is rendered instead of MudImage
        var images = cut.FindAll("img");
        Assert.That(images.Count, Is.EqualTo(0)); // No actual images

        // MudIcon renders SVGs - component shows person icons as placeholders
        var svgIcons = cut.FindAll("svg.mud-icon-root");
        Assert.That(svgIcons.Count, Is.GreaterThanOrEqualTo(2)); // At least 2 placeholder icons
    }

    [Test]
    public void DisplaysPhotoImages_WhenPhotoPathsProvided()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                suspectedPhotoPath: "/data/photos/suspected.jpg",
                targetPhotoPath: "/data/photos/target.jpg")));

        // Assert
        var images = cut.FindAll("img");
        Assert.That(images.Count, Is.EqualTo(2));
        Assert.That(images[0].GetAttribute("alt"), Does.Contain("photo").IgnoreCase);
    }

    #endregion
}

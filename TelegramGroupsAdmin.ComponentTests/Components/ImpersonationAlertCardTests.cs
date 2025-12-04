using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Reports;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ImpersonationAlertCard.razor
/// Tests impersonation alert display, risk levels, match indicators, and action buttons.
/// </summary>
[TestFixture]
public class ImpersonationAlertCardTests : MudBlazorTestContext
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

    [Test]
    public void DisplaysAutoBannedChip_WhenAutoBanned()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(autoBanned: true)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("AUTO-BANNED"));
    }

    [Test]
    public void HidesAutoBannedChip_WhenNotAutoBanned()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(autoBanned: false)));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("AUTO-BANNED"));
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
        Assert.That(cut.Markup, Does.Contain("50 points"));
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

        // Assert
        Assert.That(cut.Markup, Does.Contain("Photo Match"));
        // PhotoSimilarityScore formatted as percentage (locale-agnostic: may be "87%" or "87 %")
        Assert.That(cut.Markup, Does.Match(@"87\s?%"));
    }

    [Test]
    public void HidesPhotoMatchIndicator_WhenNoMatch()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(photoMatch: false, photoSimilarityScore: null)));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("Photo Match"));
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
        Assert.That(cut.Markup, Does.Contain("Target Admin"));
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

        // Assert
        Assert.That(cut.Markup, Does.Contain("Confirm Ban"));
        Assert.That(cut.Markup, Does.Contain("Dismiss"));
    }

    [Test]
    public void ShowsUnbanButton_WhenAutoBannedAndNotReviewed()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(autoBanned: true, reviewedAt: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Unban (False Positive)"));
    }

    [Test]
    public void HidesUnbanButton_WhenNotAutoBanned()
    {
        // Arrange & Act
        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(autoBanned: false, reviewedAt: null)));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("Unban"));
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

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("Confirm Ban"));
        Assert.That(cut.Markup, Does.Not.Contain(">Dismiss<")); // Not in button form
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
    public async Task InvokesOnAction_WhenConfirmBanClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var confirmButton = cut.FindAll("button").First(b => b.TextContent.Contains("Confirm Ban"));
        await confirmButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("confirm"));
        Assert.That(receivedAction!.Value.alert.Id, Is.EqualTo(alert.Id));
    }

    [Test]
    public async Task InvokesOnAction_WhenUnbanClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(autoBanned: true, reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var unbanButton = cut.FindAll("button").First(b => b.TextContent.Contains("Unban"));
        await unbanButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo("unban"));
    }

    [Test]
    public async Task InvokesOnAction_WhenDismissClicked()
    {
        // Arrange
        (ImpersonationAlertRecord alert, string action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ImpersonationAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ImpersonationAlertRecord, string)>(
                this, args => receivedAction = args)));

        // Act
        var dismissButton = cut.FindAll("button").First(b => b.TextContent.Contains("Dismiss"));
        await dismissButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

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

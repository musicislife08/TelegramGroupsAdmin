using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Reports;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Custom test context for ProfileScanAlertCard with mocked dependencies.
/// </summary>
public abstract class ProfileScanAlertCardTestContext : BunitContext
{
    protected ITelegramUserRepository TelegramUserRepository { get; }
    protected IManagedChatsRepository ManagedChatsRepository { get; }

    protected ProfileScanAlertCardTestContext()
    {
        TelegramUserRepository = Substitute.For<ITelegramUserRepository>();
        ManagedChatsRepository = Substitute.For<IManagedChatsRepository>();

        // Default mock: user not found (no photo, no enrichment)
        TelegramUserRepository.GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((TelegramUser?)null);
        ManagedChatsRepository.GetActiveChatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        Services.AddSingleton(TelegramUserRepository);
        Services.AddSingleton(ManagedChatsRepository);

        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>
/// Component tests for ProfileScanAlertCard.razor
/// Tests profile scan alert display, score levels, flag indicators, and action buttons.
/// </summary>
[TestFixture]
public class ProfileScanAlertCardTests : ProfileScanAlertCardTestContext
{
    #region Helper Methods

    private static ProfileScanAlertRecord CreateAlert(
        long id = 1,
        long userId = 7123456789,
        long chatId = -1001329174109,
        decimal score = 3.5m,
        ProfileScanOutcome outcome = ProfileScanOutcome.HeldForReview,
        string? aiReason = "Suspicious bio with crypto promotion links",
        string[]? aiSignalsDetected = null,
        string? bio = "Buy crypto now! DM me for investment tips",
        string? personalChannelTitle = null,
        bool hasPinnedStories = false,
        bool isScam = false,
        bool isFake = false,
        string? reviewedByUserId = null,
        DateTimeOffset? reviewedAt = null,
        string? reviewedByEmail = null,
        string? actionTaken = null,
        string? userName = "crypto_promo",
        string? firstName = "John",
        string? lastName = "Doe",
        string? chatName = "Test Crypto Group")
    {
        return new ProfileScanAlertRecord
        {
            Id = id,
            User = new UserIdentity(userId, firstName, lastName, userName),
            Chat = new ChatIdentity(chatId, chatName),
            Score = score,
            Outcome = outcome,
            AiReason = aiReason,
            AiSignalsDetected = aiSignalsDetected ?? ["crypto promotion", "spam bio"],
            Bio = bio,
            PersonalChannelTitle = personalChannelTitle,
            HasPinnedStories = hasPinnedStories,
            IsScam = isScam,
            IsFake = isFake,
            DetectedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ReviewedByUserId = reviewedByUserId,
            ReviewedAt = reviewedAt,
            ReviewedByEmail = reviewedByEmail,
            ActionTaken = actionTaken
        };
    }

    private static TelegramUser CreateUser(
        long telegramUserId = 7123456789,
        string? firstName = "John",
        string? lastName = "Doe",
        string? userPhotoPath = null,
        bool isTrusted = false,
        bool isBanned = false)
    {
        return new TelegramUser(
            TelegramUserId: telegramUserId,
            Username: null,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: userPhotoPath,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: isTrusted,
            IsBanned: isBanned,
            KickCount: 0,
            BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-30),
            LastSeenAt: DateTimeOffset.UtcNow.AddHours(-3),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt: DateTimeOffset.UtcNow.AddHours(-3));
    }

    #endregion

    #region Basic Rendering Tests

    [Test]
    public void DisplaysProfileScanAlertTitle()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert()));

        Assert.That(cut.Markup, Does.Contain("Profile Scan Alert"));
    }

    [Test]
    public void DisplaysScore()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(score: 3.5m)));

        Assert.That(cut.Markup, Does.Contain("3.5/5.0"));
    }

    [Test]
    public void DisplaysChatName()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(chatName: "My Test Group")));

        Assert.That(cut.Markup, Does.Contain("My Test Group"));
    }

    [Test]
    public void DisplaysGlobalChat_WhenChatIdIsZero()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(chatId: 0, chatName: null)));

        Assert.That(cut.Markup, Does.Contain("Global"));
    }

    #endregion

    #region Outcome Chip Tests

    [Test]
    public void DisplaysHeldForReviewOutcome()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(outcome: ProfileScanOutcome.HeldForReview)));

        Assert.That(cut.Markup, Does.Contain("HeldForReview"));
    }

    [Test]
    public void DisplaysBannedOutcome()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(outcome: ProfileScanOutcome.Banned, score: 4.5m)));

        Assert.That(cut.Markup, Does.Contain("Banned"));
    }

    #endregion

    #region Flag Indicator Tests

    [Test]
    public void DisplaysScamChip_WhenIsScam()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(isScam: true)));

        Assert.That(cut.Markup, Does.Contain("Scam"));
    }

    [Test]
    public void HidesScamChip_WhenNotScam()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(isScam: false)));

        // "Scam" should not appear as a standalone chip (it will appear in "Profile Scan" title)
        var chips = cut.FindAll(".mud-chip");
        Assert.That(chips.Select(c => c.TextContent.Trim()).ToList(), Does.Not.Contain("Scam"));
    }

    [Test]
    public void DisplaysFakeChip_WhenIsFake()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(isFake: true)));

        Assert.That(cut.Markup, Does.Contain("Fake"));
    }

    [Test]
    public void HidesFakeChip_WhenNotFake()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(isFake: false)));

        var chips = cut.FindAll(".mud-chip");
        Assert.That(chips.Select(c => c.TextContent.Trim()).ToList(), Does.Not.Contain("Fake"));
    }

    [Test]
    public void DisplaysPinnedStoriesChip_WhenHasPinnedStories()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(hasPinnedStories: true)));

        Assert.That(cut.Markup, Does.Contain("Pinned Stories"));
    }

    [Test]
    public void HidesPinnedStoriesChip_WhenNoPinnedStories()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(hasPinnedStories: false)));

        Assert.That(cut.Markup, Does.Not.Contain("Pinned Stories"));
    }

    #endregion

    #region Scan Detail Tests

    [Test]
    public void DisplaysAiReason()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(aiReason: "Suspicious crypto spam")));

        Assert.That(cut.Markup, Does.Contain("AI Reason"));
        Assert.That(cut.Markup, Does.Contain("Suspicious crypto spam"));
    }

    [Test]
    public void HidesAiReason_WhenNull()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(aiReason: null)));

        Assert.That(cut.Markup, Does.Not.Contain("AI Reason"));
    }

    [Test]
    public void DisplaysSignalsDetected()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(aiSignalsDetected: ["crypto promotion", "explicit content"])));

        Assert.That(cut.Markup, Does.Contain("Signals Detected"));
        Assert.That(cut.Markup, Does.Contain("crypto promotion"));
        Assert.That(cut.Markup, Does.Contain("explicit content"));
    }

    [Test]
    public void HidesSignalsDetected_WhenEmpty()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(aiSignalsDetected: [])));

        Assert.That(cut.Markup, Does.Not.Contain("Signals Detected"));
    }

    [Test]
    public void DisplaysBio()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(bio: "Buy crypto now!")));

        Assert.That(cut.Markup, Does.Contain("Bio"));
        Assert.That(cut.Markup, Does.Contain("Buy crypto now!"));
    }

    [Test]
    public void HidesBio_WhenNull()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(bio: null)));

        // "Bio" label should not appear when bio is null
        // Note: need to be specific since "Bio" could appear in other contexts
        var scanDetails = cut.FindAll(".mud-paper");
        var bioSection = scanDetails.Where(p => p.InnerHtml.Contains("Bio</")).ToList();
        Assert.That(bioSection, Is.Empty);
    }

    [Test]
    public void DisplaysPersonalChannel()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(personalChannelTitle: "Crypto Signals VIP")));

        Assert.That(cut.Markup, Does.Contain("Channel:"));
        Assert.That(cut.Markup, Does.Contain("Crypto Signals VIP"));
    }

    [Test]
    public void HidesPersonalChannel_WhenNull()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(personalChannelTitle: null)));

        Assert.That(cut.Markup, Does.Not.Contain("Channel:"));
    }

    #endregion

    #region User Display Tests

    [Test]
    public void DisplaysUserInfo()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                firstName: "Scammer",
                lastName: "Guy",
                userName: "scammer123",
                userId: 7123456789)));

        Assert.That(cut.Markup, Does.Contain("Flagged User"));
        Assert.That(cut.Markup, Does.Contain("Scammer Guy"));
        Assert.That(cut.Markup, Does.Contain("@scammer123"));
        Assert.That(cut.Markup, Does.Contain("ID: 7123456789"));
    }

    [Test]
    public void DisplaysNoUsername_WhenNull()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(userName: null)));

        Assert.That(cut.Markup, Does.Contain("No username"));
    }

    [Test]
    public void DisplaysTrustedChip_WhenUserIsTrusted()
    {
        var trustedUser = CreateUser(telegramUserId: 7123456789, isTrusted: true);
        TelegramUserRepository.GetByTelegramIdAsync(7123456789, Arg.Any<CancellationToken>())
            .Returns(trustedUser);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(userId: 7123456789)));

        Assert.That(cut.Markup, Does.Contain("Trusted"));
    }

    [Test]
    public void DisplaysBannedChip_WhenUserIsBanned()
    {
        var bannedUser = CreateUser(telegramUserId: 7123456789, isBanned: true);
        TelegramUserRepository.GetByTelegramIdAsync(7123456789, Arg.Any<CancellationToken>())
            .Returns(bannedUser);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(userId: 7123456789)));

        Assert.That(cut.Markup, Does.Contain("Banned"));
    }

    #endregion

    #region Photo Display Tests

    [Test]
    public void DisplaysPhotoPlaceholder_WhenNoPhoto()
    {
        // Use a distinct userId to avoid mock contamination from other tests
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(userId: 9999999999)));

        // No user returned from repo → no photo → shows MudAvatar with person icon
        var avatars = cut.FindAll(".mud-avatar");
        Assert.That(avatars.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void DisplaysPhoto_WhenUserHasPhoto()
    {
        var userWithPhoto = CreateUser(telegramUserId: 7123456789, userPhotoPath: "photos/7123456789.jpg");
        TelegramUserRepository.GetByTelegramIdAsync(7123456789, Arg.Any<CancellationToken>())
            .Returns(userWithPhoto);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(userId: 7123456789)));

        var images = cut.FindAll("img");
        Assert.That(images.Count, Is.EqualTo(1));
        Assert.That(images[0].GetAttribute("alt"), Does.Contain("photo").IgnoreCase);
    }

    #endregion

    #region Action Button Tests

    [Test]
    public void ShowsActionButtons_WhenNotReviewed()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(reviewedAt: null)));

        Assert.That(cut.Markup, Does.Contain("Allow"));
        Assert.That(cut.Markup, Does.Contain("Ban"));
        Assert.That(cut.Markup, Does.Contain("Kick"));
    }

    [Test]
    public void HidesActionButtons_WhenReviewed()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                reviewedAt: DateTimeOffset.UtcNow.AddHours(-1),
                reviewedByUserId: "admin@test.com",
                actionTaken: "Banned")));

        Assert.That(cut.Markup, Does.Not.Contain(">Allow<"));
        Assert.That(cut.Markup, Does.Not.Contain(">Ban<"));
        Assert.That(cut.Markup, Does.Not.Contain(">Kick<"));
    }

    [Test]
    public void DisplaysActionTaken_WhenReviewed()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(
                reviewedAt: DateTimeOffset.UtcNow.AddHours(-1),
                reviewedByUserId: "admin@test.com",
                reviewedByEmail: "admin@test.com",
                actionTaken: "Banned")));

        Assert.That(cut.Markup, Does.Contain("Reviewed"));
        Assert.That(cut.Markup, Does.Contain("Banned"));
        Assert.That(cut.Markup, Does.Contain("admin@test.com"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnAction_WhenAllowClicked()
    {
        (ProfileScanAlertRecord alert, ProfileScanAction action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ProfileScanAlertRecord, ProfileScanAction)>(
                this, args => receivedAction = args)));

        var allowButton = cut.FindAll("button").First(b => b.TextContent.Contains("Allow"));
        await allowButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.That(receivedAction, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(receivedAction!.Value.action, Is.EqualTo(ProfileScanAction.Allow));
            Assert.That(receivedAction!.Value.alert.Id, Is.EqualTo(alert.Id));
        }
    }

    [Test]
    public async Task InvokesOnAction_WhenBanClicked()
    {
        (ProfileScanAlertRecord alert, ProfileScanAction action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ProfileScanAlertRecord, ProfileScanAction)>(
                this, args => receivedAction = args)));

        var banButton = cut.FindAll("button").First(b => b.TextContent.Contains("Ban"));
        await banButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo(ProfileScanAction.Ban));
    }

    [Test]
    public async Task InvokesOnAction_WhenKickClicked()
    {
        (ProfileScanAlertRecord alert, ProfileScanAction action)? receivedAction = null;
        var alert = CreateAlert(reviewedAt: null);

        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, alert)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ProfileScanAlertRecord, ProfileScanAction)>(
                this, args => receivedAction = args)));

        var kickButton = cut.FindAll("button").First(b => b.TextContent.Contains("Kick"));
        await kickButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo(ProfileScanAction.Kick));
    }

    #endregion

    #region Card Styling Tests

    [Test]
    public void AppliesErrorBorderStyle_ForBannedOutcome()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(outcome: ProfileScanOutcome.Banned, score: 4.5m)));

        var card = cut.Find(".mud-card");
        Assert.That(card.GetAttribute("style"), Does.Contain("border-left"));
        Assert.That(card.GetAttribute("style"), Does.Contain("error"));
    }

    [Test]
    public void AppliesWarningBorderStyle_ForHeldForReviewOutcome()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(outcome: ProfileScanOutcome.HeldForReview)));

        var card = cut.Find(".mud-card");
        Assert.That(card.GetAttribute("style"), Does.Contain("border-left"));
        Assert.That(card.GetAttribute("style"), Does.Contain("warning"));
    }

    [Test]
    public void NoBorderStyle_ForCleanOutcome()
    {
        var cut = Render<ProfileScanAlertCard>(p => p
            .Add(x => x.Alert, CreateAlert(outcome: ProfileScanOutcome.Clean, score: 1.0m)));

        var card = cut.Find(".mud-card");
        var style = card.GetAttribute("style") ?? "";
        Assert.That(style, Does.Not.Contain("border-left"));
    }

    #endregion
}

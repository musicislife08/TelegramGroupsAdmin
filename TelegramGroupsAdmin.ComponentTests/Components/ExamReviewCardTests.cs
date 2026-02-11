using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Reports;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Custom test context for ExamReviewCard with mocked dependencies.
/// </summary>
public abstract class ExamReviewCardTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }
    protected ITelegramUserRepository TelegramUserRepository { get; }
    protected IMessageHistoryRepository MessageRepository { get; }
    protected IManagedChatsRepository ManagedChatsRepository { get; }

    protected ExamReviewCardTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        ConfigService = Substitute.For<IConfigService>();
        TelegramUserRepository = Substitute.For<ITelegramUserRepository>();
        MessageRepository = Substitute.For<IMessageHistoryRepository>();
        ManagedChatsRepository = Substitute.For<IManagedChatsRepository>();

        // Configure default mock behavior
        ConfigService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(CreateDefaultWelcomeConfig());

        TelegramUserRepository.GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((TelegramUser?)null);

        MessageRepository.GetUserMessagesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserMessageInfo>());

        ManagedChatsRepository.GetActiveChatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        // Register mocks
        Services.AddSingleton(ConfigService);
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

    private static WelcomeConfig CreateDefaultWelcomeConfig()
    {
        return new WelcomeConfig
        {
            Enabled = true,
            Mode = WelcomeMode.EntranceExam,
            ExamConfig = new ExamConfig
            {
                McQuestions =
                [
                    new ExamMcQuestion
                    {
                        Question = "What is this group about?",
                        Answers = ["Technology discussion", "Sports", "Gaming", "Music"]
                    },
                    new ExamMcQuestion
                    {
                        Question = "How should you behave?",
                        Answers = ["Be respectful", "Spam links", "Promote scams", "Troll members"]
                    }
                ],
                McPassingThreshold = 80,
                OpenEndedQuestion = "Why do you want to join this group?"
            }
        };
    }
}

/// <summary>
/// Component tests for ExamReviewCard.razor
/// Tests exam failure review UI including MC answers, open-ended responses, and action buttons.
/// </summary>
[TestFixture]
public class ExamReviewCardTests : ExamReviewCardTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates an ExamFailureRecord with realistic test data.
    /// </summary>
    private static ExamFailureRecord CreateExamFailure(
        long id = 1,
        long chatId = -1001234567890,
        long userId = 7123456789,
        Dictionary<int, string>? mcAnswers = null,
        Dictionary<int, int[]>? shuffleState = null,
        string? openEndedAnswer = null,
        int score = 50,
        int passingThreshold = 80,
        string? aiEvaluation = null,
        string? reviewedBy = null,
        DateTimeOffset? reviewedAt = null,
        string? actionTaken = null,
        string? userName = "testuser",
        string? userFirstName = "Test",
        string? userLastName = "User",
        string? chatName = "Test Group")
    {
        return new ExamFailureRecord
        {
            Id = id,
            McAnswers = mcAnswers ?? new Dictionary<int, string> { { 0, "A" }, { 1, "B" } },
            ShuffleState = shuffleState ?? new Dictionary<int, int[]>
            {
                { 0, [0, 1, 2, 3] }, // First question: A=0 (correct), B=1, C=2, D=3
                { 1, [1, 0, 2, 3] }  // Second question: A=1, B=0 (correct), C=2, D=3
            },
            OpenEndedAnswer = openEndedAnswer,
            Score = score,
            PassingThreshold = passingThreshold,
            AiEvaluation = aiEvaluation,
            FailedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ReviewedBy = reviewedBy,
            ReviewedAt = reviewedAt,
            ActionTaken = actionTaken,
            User = new UserIdentity(userId, userFirstName, userLastName, userName),
            Chat = new ChatIdentity(chatId, chatName)
        };
    }

    #endregion

    #region Basic Rendering Tests

    [Test]
    public void DisplaysExamReviewTitle()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Exam Review"));
    }

    [Test]
    public void DisplaysUserName()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                userFirstName: "John",
                userLastName: "Doe")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("John Doe"));
    }

    [Test]
    public void DisplaysUserHandle()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(userName: "johndoe")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("@johndoe"));
    }

    [Test]
    public void DisplaysNoUsername_WhenUsernameIsNull()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(userName: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("No username").IgnoreCase);
    }

    [Test]
    public void DisplaysUserId()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(userId: 123456789)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("ID: 123456789"));
    }

    [Test]
    public void DisplaysChatName()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(chatName: "My Test Group")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("My Test Group"));
    }

    #endregion

    #region Status Chip Tests

    [Test]
    public void DisplaysPendingChip_WhenNotReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(reviewedAt: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Pending"));
    }

    [Test]
    public void DisplaysReviewedChip_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                reviewedAt: DateTimeOffset.UtcNow,
                reviewedBy: "admin@test.com",
                actionTaken: "approved")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Reviewed"));
    }

    #endregion

    #region MC Answers Display Tests

    [Test]
    public void DisplaysMultipleChoiceSection_WhenMcAnswersExist()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Multiple Choice"));
    }

    [Test]
    public void DisplaysScore_WithPercentage()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(score: 75)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("75%"));
    }

    [Test]
    public void DisplaysPassingThreshold()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(passingThreshold: 80)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("80%"));
    }

    [Test]
    public void DisplaysPassedChip_WhenScoreAboveThreshold()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(score: 85, passingThreshold: 80)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Passed"));
    }

    [Test]
    public void DisplaysFailedChip_WhenScoreBelowThreshold()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(score: 50, passingThreshold: 80)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Failed"));
    }

    #endregion

    #region Open-Ended Answer Display Tests

    [Test]
    public void DisplaysOpenEndedSection_WhenAnswerExists()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "I want to learn about technology")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Open-Ended Question"));
    }

    [Test]
    public void DisplaysUserAnswer()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "I am interested in tech discussions")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("I am interested in tech discussions"));
    }

    #endregion

    #region AI Evaluation Display Tests

    [Test]
    public void DisplaysAiEvaluation_WhenProvided()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "I want to join",
                aiEvaluation: "The answer shows genuine interest in the topic")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("AI Evaluation"));
        Assert.That(cut.Markup, Does.Contain("genuine interest"));
    }

    [Test]
    public void DisplaysAiPassedChip_WhenEvaluationContainsPass()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "I want to join",
                aiEvaluation: "PASS - The answer demonstrates understanding")));

        // Assert
        // Component parses "pass" from the evaluation text
        Assert.That(cut.Markup, Does.Contain("Passed").Or.Contain("Pass"));
    }

    [Test]
    public void DisplaysAiFailedChip_WhenEvaluationContainsFail()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "idk",
                aiEvaluation: "FAIL - Response is too short and generic")));

        // Assert
        // Component parses "fail" from the evaluation text
        Assert.That(cut.Markup, Does.Contain("Failed").Or.Contain("Fail"));
    }

    [Test]
    public void DisplaysManualReviewWarning_WhenNoAiEvaluation()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                openEndedAnswer: "I want to join",
                aiEvaluation: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("manual review"));
    }

    #endregion

    #region Action Button Tests

    [Test]
    public void ShowsActionButtons_WhenNotReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(reviewedAt: null)));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Approve"));
        Assert.That(cut.Markup, Does.Contain("Deny"));
        Assert.That(cut.Markup, Does.Contain("Deny + Ban"));
    }

    [Test]
    public void HidesActionButtons_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                reviewedAt: DateTimeOffset.UtcNow,
                reviewedBy: "admin@test.com",
                actionTaken: "approved")));

        // Assert - Action buttons hidden when already reviewed
        var buttons = cut.FindAll("button");
        Assert.That(buttons.Any(b => b.TextContent.Contains("Approve")), Is.False);
        Assert.That(buttons.Any(b => b.TextContent.Contains("Deny")), Is.False);
    }

    [Test]
    public void DisplaysActionTaken_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                reviewedAt: DateTimeOffset.UtcNow,
                reviewedBy: "admin@test.com",
                actionTaken: "approved")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("approved"));
    }

    [Test]
    public void DisplaysReviewedByInfo_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                reviewedAt: DateTimeOffset.UtcNow,
                reviewedBy: "admin@test.com",
                actionTaken: "denied")));

        // Assert
        Assert.That(cut.Markup, Does.Contain("admin@test.com"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnAction_WhenApproveClicked()
    {
        // Arrange
        (ExamFailureRecord failure, ExamAction action)? receivedAction = null;
        var failure = CreateExamFailure(reviewedAt: null);

        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ExamFailureRecord, ExamAction)>(
                this, args => receivedAction = args)));

        // Act
        var approveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Approve"));
        await approveButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo(ExamAction.Approve));
        Assert.That(receivedAction!.Value.failure.Id, Is.EqualTo(failure.Id));
    }

    [Test]
    public async Task InvokesOnAction_WhenDenyClicked()
    {
        // Arrange
        (ExamFailureRecord failure, ExamAction action)? receivedAction = null;
        var failure = CreateExamFailure(reviewedAt: null);

        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ExamFailureRecord, ExamAction)>(
                this, args => receivedAction = args)));

        // Act
        var denyButton = cut.FindAll("button").First(b =>
            b.TextContent.Contains("Deny") && !b.TextContent.Contains("Ban"));
        await denyButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo(ExamAction.Deny));
    }

    [Test]
    public async Task InvokesOnAction_WhenDenyAndBanClicked()
    {
        // Arrange
        (ExamFailureRecord failure, ExamAction action)? receivedAction = null;
        var failure = CreateExamFailure(reviewedAt: null);

        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure)
            .Add(x => x.OnAction, EventCallback.Factory.Create<(ExamFailureRecord, ExamAction)>(
                this, args => receivedAction = args)));

        // Act
        var banButton = cut.FindAll("button").First(b => b.TextContent.Contains("Deny + Ban"));
        await banButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(receivedAction, Is.Not.Null);
        Assert.That(receivedAction!.Value.action, Is.EqualTo(ExamAction.DenyAndBan));
    }

    #endregion

    #region Context Section Tests

    [Test]
    public void DisplaysContextSection()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Context"));
    }

    [Test]
    public void DisplaysTrustedStatus_WhenUserIsTrusted()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var user = new TelegramUser(
            TelegramUserId: 7123456789,
            Username: "testuser",
            FirstName: "Test",
            LastName: "User",
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: true,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now.AddDays(-30),
            LastSeenAt: now,
            CreatedAt: now.AddDays(-30),
            UpdatedAt: now
        );
        TelegramUserRepository.GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("trusted").IgnoreCase);
    }

    [Test]
    public void DisplaysNotTrusted_WhenUserIsNotTrusted()
    {
        // Arrange - Default mock returns null user, which is not trusted
        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Not trusted").Or.Contain("not trusted"));
    }

    [Test]
    public void DisplaysOtherManagedChats_WhenUserIsInOtherChats()
    {
        // Arrange
        var messages = new List<UserMessageInfo>
        {
            new() { ChatId = -1001111111111, MessageId = 1 }
        };
        MessageRepository.GetUserMessagesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(messages);

        var managedChats = new List<ManagedChatRecord>
        {
            new(
                Identity: new ChatIdentity(-1001111111111, "Other Group"),
                ChatType: Telegram.Models.ManagedChatType.Group,
                BotStatus: Telegram.Models.BotChatStatus.Administrator,
                IsAdmin: true,
                AddedAt: DateTimeOffset.UtcNow,
                IsActive: true,
                IsDeleted: false,
                LastSeenAt: DateTimeOffset.UtcNow,
                SettingsJson: null,
                ChatIconPath: null
            )
        };
        ManagedChatsRepository.GetActiveChatsAsync(Arg.Any<CancellationToken>())
            .Returns(managedChats);

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Member of:").Or.Contain("Other Group"));
    }

    [Test]
    public void DisplaysNotInOtherGroups_WhenUserIsNotInOtherChats()
    {
        // Arrange - Default mock returns empty list
        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Not in other managed groups"));
    }

    #endregion

    #region Card Styling Tests

    [Test]
    public void AppliesPendingBorderStyle_WhenNotReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(reviewedAt: null)));

        // Assert - Pending cards have warning border-left
        var card = cut.Find(".mud-card");
        Assert.That(card.GetAttribute("style"), Does.Contain("border-left"));
    }

    [Test]
    public void NoPendingBorderStyle_WhenReviewed()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure(
                reviewedAt: DateTimeOffset.UtcNow,
                reviewedBy: "admin",
                actionTaken: "approved")));

        // Assert - Reviewed cards don't have the warning border
        var card = cut.Find(".mud-card");
        var style = card.GetAttribute("style") ?? "";
        Assert.That(style, Does.Not.Contain("border-left"));
    }

    #endregion

    #region Photo Display Tests

    [Test]
    public void DisplaysUserPhotoPlaceholder_WhenNoPhoto()
    {
        // Arrange & Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, CreateExamFailure()));

        // Assert - Should show MudAvatar with person icon placeholder
        var avatars = cut.FindAll(".mud-avatar");
        Assert.That(avatars.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasCardContainer()
    {
        // Arrange
        var failure = CreateExamFailure();

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-card"));
    }

    [Test]
    public void HasCardHeader()
    {
        // Arrange
        var failure = CreateExamFailure();

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-card-header"));
    }

    [Test]
    public void HasCardContent()
    {
        // Arrange
        var failure = CreateExamFailure();

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-card-content"));
    }

    [Test]
    public void HasCardActions()
    {
        // Arrange
        var failure = CreateExamFailure();

        // Act
        var cut = Render<ExamReviewCard>(p => p
            .Add(x => x.ExamFailure, failure));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-card-actions"));
    }

    #endregion
}

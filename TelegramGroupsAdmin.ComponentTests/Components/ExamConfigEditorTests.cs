using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Custom test context for ExamConfigEditor with mocked dependencies.
/// </summary>
public abstract class ExamConfigEditorTestContext : BunitContext
{
    protected IDialogService DialogService { get; }
    protected ISnackbar Snackbar { get; }

    protected ExamConfigEditorTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        DialogService = Substitute.For<IDialogService>();
        Snackbar = Substitute.For<ISnackbar>();

        // Register mocks
        Services.AddSingleton(DialogService);
        Services.AddSingleton(Snackbar);

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
/// Component tests for ExamConfigEditor.razor
/// Tests exam configuration UI including MC questions, open-ended questions, and validation.
/// </summary>
[TestFixture]
public class ExamConfigEditorTests : ExamConfigEditorTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates an ExamConfig for testing with optional parameters.
    /// </summary>
    private static ExamConfig CreateConfig(
        List<ExamMcQuestion>? mcQuestions = null,
        int mcPassingThreshold = 80,
        string? openEndedQuestion = null,
        string? groupTopic = null,
        string? evaluationCriteria = null)
    {
        return new ExamConfig
        {
            McQuestions = mcQuestions ?? [],
            McPassingThreshold = mcPassingThreshold,
            OpenEndedQuestion = openEndedQuestion,
            GroupTopic = groupTopic,
            EvaluationCriteria = evaluationCriteria
        };
    }

    /// <summary>
    /// Creates an ExamMcQuestion for testing.
    /// </summary>
    private static ExamMcQuestion CreateQuestion(
        string question = "What is this group about?",
        params string[] answers)
    {
        return new ExamMcQuestion
        {
            Question = question,
            Answers = answers.Length > 0 ? [..answers] : ["Correct answer", "Wrong answer"]
        };
    }

    /// <summary>
    /// Creates a ManagedChatRecord for testing.
    /// </summary>
    private static ManagedChatRecord CreateChat(
        long chatId = -1001234567890,
        string chatName = "Test Chat")
    {
        return new ManagedChatRecord(
            Chat: new ChatIdentity(chatId, chatName),
            ChatType: ManagedChatType.Group,
            BotStatus: BotChatStatus.Administrator,
            IsAdmin: true,
            AddedAt: DateTimeOffset.UtcNow.AddDays(-30),
            IsActive: true,
            IsDeleted: false,
            LastSeenAt: DateTimeOffset.UtcNow,
            SettingsJson: null,
            ChatIconPath: null
        );
    }

    #endregion

    #region Basic Rendering Tests

    [Test]
    public void RendersWithNullConfig_CreatesDefaultConfig()
    {
        // Arrange
        ExamConfig? receivedConfig = null;

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, null)
            .Add(x => x.ConfigChanged, EventCallback.Factory.Create<ExamConfig?>(
                this, config => receivedConfig = config)));

        // Assert - Should create a default config
        Assert.That(receivedConfig, Is.Not.Null, "ConfigChanged should be invoked with new config");
    }

    [Test]
    public void DisplaysMultipleChoiceQuestionsSection()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Multiple Choice Questions"));
    }

    [Test]
    public void DisplaysOpenEndedQuestionSection()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Open-Ended Question"));
    }

    [Test]
    public void DisplaysOptionalChip_WhenNoMcQuestions()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Optional"));
    }

    [Test]
    public void DisplaysQuestionCountChip_WhenMcQuestionsExist()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1"),
            CreateQuestion("Q2")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("2 question(s)"));
    }

    [Test]
    public void DisplaysConfiguredChip_WhenOpenEndedQuestionExists()
    {
        // Arrange
        var config = CreateConfig(openEndedQuestion: "Why do you want to join?");

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Configured"));
    }

    #endregion

    #region MC Question Tests

    [Test]
    public void DisplaysAddQuestionButton_WhenLessThan4Questions()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Add Question"));
    }

    [Test]
    public void HidesAddQuestionButton_WhenAtMaxQuestions()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1"),
            CreateQuestion("Q2"),
            CreateQuestion("Q3"),
            CreateQuestion("Q4")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Should not have "Add Question" button when at max (4)
        var addButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Add Question"));
        Assert.That(addButtons.Any(), Is.False);
    }

    [Test]
    public async Task AddQuestion_AddsNewQuestionToConfig()
    {
        // Arrange
        var config = CreateConfig();
        ExamConfig? updatedConfig = null;

        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config)
            .Add(x => x.ConfigChanged, EventCallback.Factory.Create<ExamConfig?>(
                this, c => updatedConfig = c)));

        // Act
        var addButton = cut.FindAll("button").First(b => b.TextContent.Contains("Add Question"));
        await cut.InvokeAsync(() => addButton.Click());

        // Assert
        Assert.That(config.McQuestions.Count, Is.EqualTo(1));
    }

    [Test]
    public void DisplaysQuestionNumber_ForEachQuestion()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("First question"),
            CreateQuestion("Second question")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Question 1"));
        Assert.That(cut.Markup, Does.Contain("Question 2"));
    }

    [Test]
    public void DisplaysRemoveQuestionButton_ForEachQuestion()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [CreateQuestion("Q1")]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - MudIconButton with Color.Error exists (remove question button)
        // MudBlazor renders the icon button with mud-icon-button class and Color.Error adds error color
        var iconButtons = cut.FindAll(".mud-icon-button");
        Assert.That(iconButtons.Any(), Is.True, "Should have at least one icon button for removing questions");
    }

    [Test]
    public async Task RemoveQuestion_RemovesQuestionFromConfig()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1"),
            CreateQuestion("Q2")
        ]);

        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config)
            .Add(x => x.ConfigChanged, EventCallback.Factory.Create<ExamConfig?>(this, _ => { })));

        // Act - Click the first icon button (remove question button uses MudIconButton)
        var iconButtons = cut.FindAll(".mud-icon-button").ToList();
        Assert.That(iconButtons.Count, Is.GreaterThan(0), "Should have icon buttons");
        await cut.InvokeAsync(() => iconButtons[0].Click());

        // Assert
        Assert.That(config.McQuestions.Count, Is.EqualTo(1));
    }

    #endregion

    #region Answer Tests

    [Test]
    public void DisplaysCorrectAnswerLabel_ForFirstAnswer()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1", "Wrong 2")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Correct Answer"));
    }

    [Test]
    public void DisplaysWrongAnswerLabels_ForOtherAnswers()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1", "Wrong 2")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Wrong Answer"));
    }

    [Test]
    public void DisplaysAddWrongAnswerButton_WhenLessThan4Answers()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Add Wrong Answer"));
    }

    [Test]
    public void HidesAddWrongAnswerButton_WhenAtMaxAnswers()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1", "Wrong 2", "Wrong 3")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Add Wrong Answer should not appear when at max (4)
        var addButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Add Wrong Answer"));
        Assert.That(addButtons.Any(), Is.False);
    }

    [Test]
    public void DisplaysRemoveLastAnswerButton_WhenMoreThan2Answers()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1", "Wrong 2")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Remove Last Answer"));
    }

    [Test]
    public void HidesRemoveLastAnswerButton_WhenOnly2Answers()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("Q1", "Correct", "Wrong 1")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Remove Last Answer should not appear when at minimum (2)
        var removeButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Remove Last Answer"));
        Assert.That(removeButtons.Any(), Is.False);
    }

    #endregion

    #region Passing Threshold Tests

    [Test]
    public void DisplaysPassingThresholdField_WhenMcQuestionsExist()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [CreateQuestion("Q1")]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Passing Threshold"));
    }

    [Test]
    public void HidesPassingThresholdField_WhenNoMcQuestions()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Should not show threshold when no MC questions
        Assert.That(cut.Markup, Does.Not.Contain("Passing Threshold"));
    }

    #endregion

    #region Open-Ended Question Tests

    [Test]
    public void DisplaysOpenEndedQuestionField()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Why do you want to join"));
    }

    [Test]
    public void DisplaysGroupTopicField()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Group Topic"));
    }

    [Test]
    public void DisplaysEvaluationCriteriaField()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Evaluation Criteria"));
    }

    [Test]
    public void DisplaysGenerateCriteriaButton_WhenQuestionAndTopicProvided()
    {
        // Arrange
        var config = CreateConfig(
            openEndedQuestion: "Why do you want to join?",
            groupTopic: "Technology discussion"
        );

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Generate Criteria with AI"));
    }

    [Test]
    public void DisplaysEditCriteriaButton_WhenCriteriaAlreadyExists()
    {
        // Arrange
        var config = CreateConfig(
            openEndedQuestion: "Why do you want to join?",
            groupTopic: "Technology discussion",
            evaluationCriteria: "PASS if genuine interest"
        );

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Edit Criteria with AI"));
    }

    [Test]
    public void HidesGenerateCriteriaButton_WhenNoQuestionOrTopic()
    {
        // Arrange
        var config = CreateConfig(
            openEndedQuestion: "Why do you want to join?",
            groupTopic: null // Missing topic
        );

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        var genButton = cut.FindAll("button").Where(b => b.TextContent.Contains("Generate Criteria"));
        Assert.That(genButton.Any(), Is.False);
    }

    #endregion

    #region Validation Tests

    [Test]
    public void DisplaysValidationWarning_WhenNoQuestionsConfigured()
    {
        // Arrange
        var config = CreateConfig(); // No MC or open-ended questions

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("At least one question type"));
    }

    [Test]
    public void HidesValidationWarning_WhenMcQuestionsConfigured()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [CreateQuestion("Q1")]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("At least one question type"));
    }

    [Test]
    public void HidesValidationWarning_WhenOpenEndedQuestionConfigured()
    {
        // Arrange
        var config = CreateConfig(openEndedQuestion: "Why join?");

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("At least one question type"));
    }

    [Test]
    public void DisplaysBothRequiredInfo_WhenBothQuestionsConfigured()
    {
        // Arrange
        var config = CreateConfig(
            mcQuestions: [CreateQuestion("Q1")],
            openEndedQuestion: "Why join?"
        );

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("both"));
    }

    #endregion

    #region Preview Tests

    [Test]
    public void DisplaysMcPreview_WhenMcQuestionsExist()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [
            CreateQuestion("What is this group about?", "Tech talk", "Sports", "Gaming")
        ]);

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Preview should show question text
        Assert.That(cut.Markup, Does.Contain("What is this group about?"));
    }

    [Test]
    public void DisplaysOpenEndedPreview_WhenOpenEndedQuestionExists()
    {
        // Arrange
        var config = CreateConfig(openEndedQuestion: "Why do you want to join this group?");

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert - Preview should show question text
        Assert.That(cut.Markup, Does.Contain("Why do you want to join this group?"));
    }

    [Test]
    public void DisplaysAddQuestionPrompt_WhenNoMcPreview()
    {
        // Arrange
        var config = CreateConfig(); // No MC questions

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Add a question to see the preview"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesConfigChanged_WhenAddingQuestion()
    {
        // Arrange
        var config = CreateConfig();
        var changeCount = 0;

        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config)
            .Add(x => x.ConfigChanged, EventCallback.Factory.Create<ExamConfig?>(
                this, _ => changeCount++)));

        // Act
        var addButton = cut.FindAll("button").First(b => b.TextContent.Contains("Add Question"));
        await cut.InvokeAsync(() => addButton.Click());

        // Assert
        Assert.That(changeCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task InvokesConfigChanged_WhenRemovingQuestion()
    {
        // Arrange
        var config = CreateConfig(mcQuestions: [CreateQuestion("Q1")]);
        var changeCount = 0;

        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config)
            .Add(x => x.ConfigChanged, EventCallback.Factory.Create<ExamConfig?>(
                this, _ => changeCount++)));

        // Act - Click the first icon button (remove question button uses MudIconButton)
        var iconButtons = cut.FindAll(".mud-icon-button").ToList();
        Assert.That(iconButtons.Count, Is.GreaterThan(0), "Should have icon buttons");
        await cut.InvokeAsync(() => iconButtons[0].Click());

        // Assert
        Assert.That(changeCount, Is.GreaterThan(0));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasPaperContainer()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    [Test]
    public void HasDivider()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-divider"));
    }

    [Test]
    public void HasGridLayout()
    {
        // Arrange
        var config = CreateConfig();

        // Act
        var cut = Render<ExamConfigEditor>(p => p
            .Add(x => x.Config, config));

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-grid"));
    }

    #endregion
}

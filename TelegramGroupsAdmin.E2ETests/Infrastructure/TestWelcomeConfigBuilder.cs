using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Configuration.Models.Welcome;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating WelcomeConfig entries in the database for E2E testing.
/// Required when testing ExamReviewCard since it loads exam questions from the config.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// await new TestWelcomeConfigBuilder(Factory.Services)
///     .ForChat(chat)
///     .WithExamConfig(examConfig)
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestWelcomeConfigBuilder
{
    private readonly IServiceProvider _services;

    private long _chatId;
    private bool _enabled = true;
    private WelcomeMode _mode = WelcomeMode.EntranceExam;
    private int _timeoutSeconds = 300;
    private ExamConfig? _examConfig;

    public TestWelcomeConfigBuilder(IServiceProvider services)
    {
        _services = services;
        // Default chat ID (should be overridden with ForChat)
        _chatId = -Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999);

        // Default exam config with MC questions and open-ended
        _examConfig = CreateDefaultExamConfig();
    }

    /// <summary>
    /// Sets the chat ID for this welcome config.
    /// </summary>
    public TestWelcomeConfigBuilder ForChat(long chatId)
    {
        _chatId = chatId;
        return this;
    }

    /// <summary>
    /// Sets the chat using a TestChat.
    /// </summary>
    public TestWelcomeConfigBuilder ForChat(TestChat chat)
    {
        _chatId = chat.ChatId;
        return this;
    }

    /// <summary>
    /// Sets whether welcome system is enabled.
    /// </summary>
    public TestWelcomeConfigBuilder WithEnabled(bool enabled)
    {
        _enabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets the welcome mode.
    /// </summary>
    public TestWelcomeConfigBuilder WithMode(WelcomeMode mode)
    {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// Sets the timeout in seconds.
    /// </summary>
    public TestWelcomeConfigBuilder WithTimeout(int seconds)
    {
        _timeoutSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Sets a custom exam configuration.
    /// </summary>
    public TestWelcomeConfigBuilder WithExamConfig(ExamConfig examConfig)
    {
        _examConfig = examConfig;
        return this;
    }

    /// <summary>
    /// Sets up exam config with MC questions only.
    /// </summary>
    public TestWelcomeConfigBuilder WithMcQuestionsOnly()
    {
        _examConfig = new ExamConfig
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
                    Question = "How should you behave in this group?",
                    Answers = ["Be respectful and follow rules", "Spam links", "Promote scams", "Troll members"]
                }
            ],
            McPassingThreshold = 80,
            OpenEndedQuestion = null,
            GroupTopic = null,
            EvaluationCriteria = null
        };
        return this;
    }

    /// <summary>
    /// Sets up exam config with open-ended question only.
    /// </summary>
    public TestWelcomeConfigBuilder WithOpenEndedOnly()
    {
        _examConfig = new ExamConfig
        {
            McQuestions = [],
            McPassingThreshold = 80,
            OpenEndedQuestion = "Why do you want to join this group?",
            GroupTopic = "Technology enthusiasts",
            EvaluationCriteria = "PASS if the answer demonstrates genuine interest in technology"
        };
        return this;
    }

    /// <summary>
    /// Sets up exam config with both MC questions and open-ended.
    /// </summary>
    public TestWelcomeConfigBuilder WithFullExam()
    {
        _examConfig = CreateDefaultExamConfig();
        return this;
    }

    /// <summary>
    /// Creates the default exam config with both MC and open-ended questions.
    /// </summary>
    private static ExamConfig CreateDefaultExamConfig()
    {
        return new ExamConfig
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
                    Question = "How should you behave in this group?",
                    Answers = ["Be respectful and follow rules", "Spam links", "Promote scams", "Troll members"]
                }
            ],
            McPassingThreshold = 80,
            OpenEndedQuestion = "Why do you want to join this group?",
            GroupTopic = "Technology enthusiasts",
            EvaluationCriteria = "PASS if the answer demonstrates genuine interest in technology"
        };
    }

    /// <summary>
    /// Builds and persists the WelcomeConfig to the database.
    /// </summary>
    public async Task BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

        var welcomeConfig = new WelcomeConfig
        {
            Enabled = _enabled,
            Mode = _mode,
            TimeoutSeconds = _timeoutSeconds,
            MainWelcomeMessage = "Welcome! Please complete the entrance exam.",
            DmChatTeaserMessage = "Click the button to start the exam.",
            AcceptButtonText = "Start Exam",
            DenyButtonText = "Leave",
            DmButtonText = "Take Exam",
            ExamConfig = _examConfig
        };

        await configService.SaveAsync(ConfigType.Welcome, _chatId, welcomeConfig);
    }
}

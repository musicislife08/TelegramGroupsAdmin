using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing entrance exam flow.
/// Handles MC question display, answer validation, open-ended evaluation.
/// </summary>
public class ExamFlowService : IExamFlowService
{
    private const string ExamCallbackPrefix = "exam:";

    private readonly ILogger<ExamFlowService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IExamEvaluationService _examEvaluationService;

    public ExamFlowService(
        ILogger<ExamFlowService> logger,
        IServiceProvider serviceProvider,
        ITelegramBotClientFactory botClientFactory,
        IExamEvaluationService examEvaluationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClientFactory = botClientFactory;
        _examEvaluationService = examEvaluationService;
    }

    public bool IsExamCallback(string callbackData)
        => callbackData.StartsWith(ExamCallbackPrefix, StringComparison.Ordinal);

    public (long SessionId, int QuestionIndex, int AnswerIndex)? ParseExamCallback(string callbackData)
    {
        // Format: exam:{sessionId}:{questionIndex}:{answerIndex}
        if (!IsExamCallback(callbackData))
            return null;

        var parts = callbackData[ExamCallbackPrefix.Length..].Split(':');
        if (parts.Length != 3)
            return null;

        if (!long.TryParse(parts[0], out var sessionId) ||
            !int.TryParse(parts[1], out var questionIndex) ||
            !int.TryParse(parts[2], out var answerIndex))
            return null;

        return (sessionId, questionIndex, answerIndex);
    }

    public async Task<ExamStartResult> StartExamAsync(
        Chat chat,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config.ExamConfig == null || !config.ExamConfig.IsValid)
        {
            _logger.LogWarning("Exam config not valid for chat {ChatId}", chat.Id);
            return new ExamStartResult(Success: false, WelcomeMessageId: 0);
        }

        var examConfig = config.ExamConfig;
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

            // Create exam session
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(config.TimeoutSeconds);
            var sessionId = await sessionRepo.CreateSessionAsync(chat.Id, user.Id, expiresAt, cancellationToken);

            _logger.LogInformation(
                "Created exam session {SessionId} for {User} in {Chat}",
                sessionId,
                user.ToLogInfo(),
                chat.ToLogInfo());

            // Send first question
            int messageId;
            if (examConfig.HasMcQuestions)
            {
                // Generate deterministic shuffle for first question
                var shuffleState = GenerateShuffleForQuestion(sessionId, 0, examConfig.McQuestions[0].Answers.Count);

                messageId = await SendMcQuestionAsync(
                    operations, chat.Id, user, sessionId,
                    examConfig.McQuestions[0], 0, shuffleState,
                    examConfig.McQuestions.Count, cancellationToken);
            }
            else
            {
                // Only open-ended question
                messageId = await SendOpenEndedQuestionAsync(
                    operations, chat.Id, user, examConfig, cancellationToken);
            }

            return new ExamStartResult(Success: true, WelcomeMessageId: messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start exam for {User} in {Chat}",
                user.ToLogDebug(), chat.ToLogDebug());
            return new ExamStartResult(Success: false, WelcomeMessageId: 0);
        }
    }

    public async Task<ExamAnswerResult> HandleMcAnswerAsync(
        long sessionId,
        int questionIndex,
        int answerIndex,
        User user,
        Message message,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();

        var session = await sessionRepo.GetByIdAsync(sessionId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Exam session {SessionId} not found", sessionId);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // Verify user matches session
        if (session.UserId != user.Id)
        {
            _logger.LogWarning("User {UserId} tried to answer session {SessionId} belonging to {OwnerId}",
                user.Id, sessionId, session.UserId);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // Verify question index
        if (session.CurrentQuestionIndex != questionIndex)
        {
            _logger.LogWarning("Question index mismatch: expected {Expected}, got {Actual}",
                session.CurrentQuestionIndex, questionIndex);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // Load config
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, session.ChatId) ?? WelcomeConfig.Default;

        if (config.ExamConfig == null)
        {
            _logger.LogWarning("No exam config for chat {ChatId}", session.ChatId);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        var examConfig = config.ExamConfig;
        var operations = await _botClientFactory.GetOperationsAsync();

        // Convert answer index to letter (A=0, B=1, C=2, D=3)
        var answerLetter = IndexToLetter(answerIndex);

        // Regenerate the same shuffle used when displaying the question (deterministic)
        var shuffleState = GenerateShuffleForQuestion(sessionId, questionIndex, examConfig.McQuestions[questionIndex].Answers.Count);

        // Record answer with shuffle state for audit/review display
        await sessionRepo.RecordMcAnswerAsync(sessionId, questionIndex, answerLetter, shuffleState, cancellationToken);

        // Delete the question message
        try
        {
            await operations.DeleteMessageAsync(message.Chat.Id, message.MessageId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete exam question message {MessageId}", message.MessageId);
        }

        // Check if more MC questions
        var nextQuestionIndex = questionIndex + 1;
        if (nextQuestionIndex < examConfig.McQuestions.Count)
        {
            // Generate deterministic shuffle for next question
            var nextShuffleState = GenerateShuffleForQuestion(sessionId, nextQuestionIndex, examConfig.McQuestions[nextQuestionIndex].Answers.Count);

            // Send next MC question
            await SendMcQuestionAsync(
                operations, session.ChatId, user, sessionId,
                examConfig.McQuestions[nextQuestionIndex],
                nextQuestionIndex, nextShuffleState,
                examConfig.McQuestions.Count, cancellationToken);

            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // MC questions complete - check if open-ended needed
        if (examConfig.HasOpenEndedQuestion)
        {
            await SendOpenEndedQuestionAsync(
                operations, session.ChatId, user, examConfig, cancellationToken);

            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // All questions complete - evaluate
        // Re-fetch session to get all answers
        session = await sessionRepo.GetByIdAsync(sessionId, cancellationToken);
        return await EvaluateAndCompleteAsync(
            session!, examConfig, user, cancellationToken);
    }

    public async Task<ExamAnswerResult> HandleOpenEndedAnswerAsync(
        long chatId,
        User user,
        string answerText,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();

        var session = await sessionRepo.GetSessionAsync(chatId, user.Id, cancellationToken);
        if (session == null)
        {
            _logger.LogDebug("No active exam session for user {UserId} in chat {ChatId}", user.Id, chatId);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // Load config
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, chatId) ?? WelcomeConfig.Default;

        if (config.ExamConfig == null || !config.ExamConfig.HasOpenEndedQuestion)
        {
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        var examConfig = config.ExamConfig;

        // Store open-ended answer
        await sessionRepo.RecordOpenEndedAnswerAsync(session.Id, answerText, cancellationToken);

        // Re-fetch session to get updated data
        session = await sessionRepo.GetByIdAsync(session.Id, cancellationToken);

        // Evaluate
        return await EvaluateAndCompleteAsync(
            session!, examConfig, user, cancellationToken);
    }

    public async Task<bool> HasActiveSessionAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        return await sessionRepo.HasActiveSessionAsync(chatId, userId, cancellationToken);
    }

    public async Task CancelSessionAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        await sessionRepo.DeleteSessionAsync(chatId, userId, cancellationToken);

        _logger.LogInformation("Cancelled exam session for user {UserId} in chat {ChatId}", userId, chatId);
    }

    private async Task<ExamAnswerResult> EvaluateAndCompleteAsync(
        ExamSession session,
        ExamConfig examConfig,
        User user,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var reviewsRepo = scope.ServiceProvider.GetRequiredService<IReviewsRepository>();
        var operations = await _botClientFactory.GetOperationsAsync();

        // Calculate MC score
        bool? mcPassed = null;
        int mcScore = 0;
        if (examConfig.HasMcQuestions && session.McAnswers?.Count > 0)
        {
            var correctCount = CountCorrectAnswers(session.McAnswers, session.ShuffleState);
            mcScore = (int)Math.Round(100.0 * correctCount / session.McAnswers.Count);
            mcPassed = mcScore >= examConfig.McPassingThreshold;

            _logger.LogInformation(
                "MC score for user {UserId}: {Score}% ({Correct}/{Total}), threshold: {Threshold}%, passed: {Passed}",
                user.Id, mcScore, correctCount, session.McAnswers.Count, examConfig.McPassingThreshold, mcPassed);
        }

        // Evaluate open-ended
        bool? openEndedPassed = null;
        string? aiReasoning = null;
        if (examConfig.HasOpenEndedQuestion && session.OpenEndedAnswer != null)
        {
            var evalResult = await _examEvaluationService.EvaluateAnswerAsync(
                examConfig.OpenEndedQuestion!,
                session.OpenEndedAnswer,
                examConfig.EvaluationCriteria ?? "Accept genuine answers, reject generic or off-topic responses.",
                examConfig.GroupTopic ?? "general discussion",
                cancellationToken);

            if (evalResult != null)
            {
                openEndedPassed = evalResult.Passed;
                aiReasoning = evalResult.Reasoning;

                _logger.LogInformation(
                    "Open-ended evaluation for user {UserId}: passed={Passed}, confidence={Confidence:F2}, reasoning={Reasoning}",
                    user.Id, evalResult.Passed, evalResult.Confidence, evalResult.Reasoning);
            }
            else
            {
                // AI unavailable - send to review
                _logger.LogWarning("AI unavailable for exam evaluation, sending user {UserId} to review", user.Id);
                openEndedPassed = null;
            }
        }

        // Determine final result
        bool passed;
        if (examConfig.RequireBothToPass)
        {
            // Both must pass (if configured)
            var mcOk = !examConfig.HasMcQuestions || mcPassed == true;
            var openOk = !examConfig.HasOpenEndedQuestion || openEndedPassed == true;
            passed = mcOk && openOk;
        }
        else
        {
            // Either can pass
            passed = mcPassed == true || openEndedPassed == true;
        }

        // If AI was unavailable for open-ended, force to review
        if (examConfig.HasOpenEndedQuestion && openEndedPassed == null)
        {
            passed = false;
        }

        // Delete session
        await sessionRepo.DeleteSessionAsync(session.Id, cancellationToken);

        if (passed)
        {
            // Restore permissions
            await RestoreUserPermissionsAsync(operations, session.ChatId, user, cancellationToken);

            // Mark user as active
            var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            await telegramUserRepo.SetActiveAsync(user.Id, true, cancellationToken);

            // Send success message
            await operations.SendMessageAsync(
                chatId: session.ChatId,
                text: "✅ Welcome! You've passed the entrance exam.",
                cancellationToken: cancellationToken);

            _logger.LogInformation("User {User} passed entrance exam in chat {ChatId}",
                user.ToLogInfo(), session.ChatId);

            return new ExamAnswerResult(ExamComplete: true, Passed: true, SentToReview: false);
        }

        // Create exam failure review
        var examFailure = new ExamFailureRecord
        {
            ChatId = session.ChatId,
            UserId = user.Id,
            McAnswers = session.McAnswers,
            OpenEndedAnswer = session.OpenEndedAnswer,
            Score = mcScore,
            PassingThreshold = examConfig.McPassingThreshold,
            AiEvaluation = aiReasoning,
            FailedAt = DateTimeOffset.UtcNow
        };

        await reviewsRepo.InsertExamFailureAsync(examFailure, cancellationToken);

        // Send pending message to user
        await operations.SendMessageAsync(
            chatId: session.ChatId,
            text: "⏳ Your answers are being reviewed by an admin. Please wait.",
            cancellationToken: cancellationToken);

        _logger.LogInformation("User {User} failed entrance exam in chat {ChatId}, sent to review",
            user.ToLogInfo(), session.ChatId);

        return new ExamAnswerResult(ExamComplete: true, Passed: false, SentToReview: true);
    }

    private async Task<int> SendMcQuestionAsync(
        ITelegramOperations operations,
        long chatId,
        User user,
        long sessionId,
        ExamMcQuestion question,
        int questionIndex,
        int[] shuffleOrder,
        int totalQuestions,
        CancellationToken cancellationToken)
    {
        var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);

        var text = $"📝 {username}, Question {questionIndex + 1}/{totalQuestions}:\n\n{question.Question}";

        // Build keyboard with shuffled answers
        var buttons = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < shuffleOrder.Length; i++)
        {
            var originalIndex = shuffleOrder[i];
            var answerText = question.Answers[originalIndex];
            var callbackData = $"{ExamCallbackPrefix}{sessionId}:{questionIndex}:{i}";

            buttons.Add([InlineKeyboardButton.WithCallbackData($"{IndexToLetter(i)}) {answerText}", callbackData)]);
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        var message = await operations.SendMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        return message.MessageId;
    }

    private async Task<int> SendOpenEndedQuestionAsync(
        ITelegramOperations operations,
        long chatId,
        User user,
        ExamConfig examConfig,
        CancellationToken cancellationToken)
    {
        var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);

        var text = $"📝 {username}, please answer this question:\n\n{examConfig.OpenEndedQuestion}\n\n" +
                   "Reply to this message with your answer.";

        var message = await operations.SendMessageAsync(
            chatId: chatId,
            text: text,
            cancellationToken: cancellationToken);

        return message.MessageId;
    }

    private async Task RestoreUserPermissionsAsync(
        ITelegramOperations operations,
        long chatId,
        User user,
        CancellationToken cancellationToken)
    {
        try
        {
            var chatDetails = await operations.GetChatAsync(chatId, cancellationToken);
            var defaultPermissions = chatDetails.Permissions ?? new ChatPermissions
            {
                CanSendMessages = true,
                CanSendPhotos = true,
                CanSendVideos = true,
                CanSendAudios = true,
                CanSendDocuments = true,
                CanSendVideoNotes = true,
                CanSendVoiceNotes = true,
                CanSendPolls = true,
                CanSendOtherMessages = true,
                CanAddWebPagePreviews = true,
                CanChangeInfo = false,
                CanInviteUsers = true,
                CanPinMessages = false,
                CanManageTopics = false
            };

            await operations.RestrictChatMemberAsync(
                chatId: chatId,
                userId: user.Id,
                permissions: defaultPermissions,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Restored permissions for user {UserId} in chat {ChatId}", user.Id, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore permissions for user {UserId} in chat {ChatId}", user.Id, chatId);
            throw;
        }
    }

    /// <summary>
    /// Generate Fisher-Yates shuffle for answer indices.
    /// Uses deterministic seed so the same shuffle can be regenerated when validating answers.
    /// </summary>
    private static int[] GenerateShuffleForQuestion(long sessionId, int questionIndex, int answerCount)
    {
        var indices = Enumerable.Range(0, answerCount).ToArray();

        // Deterministic seed from session ID and question index
        var seed = HashCode.Combine(sessionId, questionIndex);
        var random = new Random(seed);

        // Fisher-Yates shuffle
        for (var i = answerCount - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        return indices;
    }

    /// <summary>
    /// Convert index to letter (0=A, 1=B, 2=C, 3=D).
    /// </summary>
    private static string IndexToLetter(int index) => ((char)('A' + index)).ToString();

    /// <summary>
    /// Convert letter to index (A=0, B=1, C=2, D=3).
    /// </summary>
    private static int LetterToIndex(string letter) => letter[0] - 'A';

    /// <summary>
    /// Count correct MC answers by checking if the original index was 0 (correct answer).
    /// </summary>
    private static int CountCorrectAnswers(
        Dictionary<int, string> mcAnswers,
        Dictionary<int, int[]>? shuffleState)
    {
        var correctCount = 0;

        foreach (var (questionIndex, answerLetter) in mcAnswers)
        {
            var answerPosition = LetterToIndex(answerLetter);

            // Get the original answer index from shuffle state
            if (shuffleState?.TryGetValue(questionIndex, out var shuffle) == true &&
                answerPosition < shuffle.Length)
            {
                var originalIndex = shuffle[answerPosition];
                // Original index 0 is always the correct answer (first answer in config)
                if (originalIndex == 0)
                {
                    correctCount++;
                }
            }
        }

        return correctCount;
    }
}

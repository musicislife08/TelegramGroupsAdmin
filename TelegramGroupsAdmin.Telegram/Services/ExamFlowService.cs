using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

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
            _logger.LogWarning("Exam config not valid for {Chat}", chat.ToLogInfo());
            return new ExamStartResult(Success: false, WelcomeMessageId: 0);
        }

        var examConfig = config.ExamConfig;
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
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

    public async Task<ExamStartResult> StartExamInDmAsync(
        long groupChatId,
        User user,
        long dmChatId,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config.ExamConfig == null || !config.ExamConfig.IsValid)
        {
            _logger.LogWarning("Exam config not valid for chat {ChatId}", groupChatId);
            return new ExamStartResult(Success: false, WelcomeMessageId: 0);
        }

        var examConfig = config.ExamConfig;
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

            // Create exam session - store groupChatId for permission restore
            // Note: For DMs, we use user.Id directly as the chat ID (Telegram uses userId as chatId for private chats)
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(config.TimeoutSeconds);
            var sessionId = await sessionRepo.CreateSessionAsync(groupChatId, user.Id, expiresAt, cancellationToken);

            _logger.LogInformation(
                "Created exam session {SessionId} for user {UserId} (group: {GroupId})",
                sessionId,
                user.Id,
                groupChatId);

            // Send exam intro (MainWelcomeMessage) first - rules/guidelines without buttons
            var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);
            var chatInfo = await operations.GetChatAsync(groupChatId, cancellationToken);
            var chatName = chatInfo.Title ?? "the group";

            var introText = WelcomeMessageBuilder.FormatExamIntro(config, username, chatName);
            await operations.SendMessageAsync(
                chatId: dmChatId,
                text: introText,
                cancellationToken: cancellationToken);

            // Then send first question to DM
            int messageId;
            if (examConfig.HasMcQuestions)
            {
                // Generate deterministic shuffle for first question
                var shuffleState = GenerateShuffleForQuestion(sessionId, 0, examConfig.McQuestions[0].Answers.Count);

                messageId = await SendMcQuestionAsync(
                    operations, dmChatId, user, sessionId,  // dmChatId instead of groupChatId
                    examConfig.McQuestions[0], 0, shuffleState,
                    examConfig.McQuestions.Count, cancellationToken);
            }
            else
            {
                // Only open-ended question
                messageId = await SendOpenEndedQuestionAsync(
                    operations, dmChatId, user, examConfig, cancellationToken);  // dmChatId
            }

            return new ExamStartResult(Success: true, WelcomeMessageId: messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start exam in DM for user {UserId} from group {GroupId}",
                user.Id, groupChatId);
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
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();

        var session = await sessionRepo.GetByIdAsync(sessionId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Exam session {SessionId} not found", sessionId);
            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // Check if session has expired (timeout = automatic fail)
        if (session.IsExpired)
        {
            _logger.LogWarning("Exam session {SessionId} has expired", sessionId);
            var expiredChatId = session.ChatId;
            await sessionRepo.DeleteSessionAsync(sessionId, cancellationToken);
            return new ExamAnswerResult(ExamComplete: true, Passed: false, SentToReview: false, GroupChatId: expiredChatId);
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

        // Send to user's DM (in Telegram, private chat ID = user ID)
        var targetChatId = session.UserId;

        // Check if more MC questions
        var nextQuestionIndex = questionIndex + 1;
        if (nextQuestionIndex < examConfig.McQuestions.Count)
        {
            // Generate deterministic shuffle for next question
            var nextShuffleState = GenerateShuffleForQuestion(sessionId, nextQuestionIndex, examConfig.McQuestions[nextQuestionIndex].Answers.Count);

            // Send next MC question to DM (or group for legacy)
            await SendMcQuestionAsync(
                operations, targetChatId, user, sessionId,
                examConfig.McQuestions[nextQuestionIndex],
                nextQuestionIndex, nextShuffleState,
                examConfig.McQuestions.Count, cancellationToken);

            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // MC questions complete - check if open-ended needed
        if (examConfig.HasOpenEndedQuestion)
        {
            await SendOpenEndedQuestionAsync(
                operations, targetChatId, user, examConfig, cancellationToken);

            return new ExamAnswerResult(ExamComplete: false, Passed: null, SentToReview: false);
        }

        // All questions complete - evaluate
        // Capture ChatId before re-fetch in case session is deleted
        var groupChatId = session.ChatId;

        // Re-fetch session to get all answers
        session = await sessionRepo.GetByIdAsync(sessionId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Exam session {SessionId} was deleted before evaluation", sessionId);
            return new ExamAnswerResult(ExamComplete: true, Passed: false, SentToReview: false, GroupChatId: groupChatId);
        }
        return await EvaluateAndCompleteAsync(session, examConfig, user, cancellationToken);
    }

    public async Task<ExamAnswerResult> HandleOpenEndedAnswerAsync(
        long chatId,
        User user,
        string answerText,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
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
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        return await sessionRepo.HasActiveSessionAsync(chatId, userId, cancellationToken);
    }

    public async Task CancelSessionAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        await sessionRepo.DeleteSessionAsync(chatId, userId, cancellationToken);

        _logger.LogInformation("Cancelled exam session for user {UserId} in chat {ChatId}", userId, chatId);
    }

    public async Task<ActiveExamContext?> GetActiveExamContextAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var configService = scope.ServiceProvider.GetRequiredService<Configuration.Services.IConfigService>();

        // Find any active session for this user
        var session = await sessionRepo.GetActiveSessionForUserAsync(userId, cancellationToken);
        if (session == null)
            return null;

        // Load exam config to determine if awaiting open-ended
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(
            Configuration.ConfigType.Welcome, session.ChatId) ?? WelcomeConfig.Default;

        if (config.ExamConfig == null)
            return null;

        // Awaiting open-ended if:
        // 1. Exam has open-ended question configured
        // 2. All MC questions have been answered (or there are none)
        // 3. Open-ended answer hasn't been submitted yet
        var mcQuestionsComplete = !config.ExamConfig.HasMcQuestions ||
            (session.McAnswers?.Count >= config.ExamConfig.McQuestions.Count);

        var awaitingOpenEnded = config.ExamConfig.HasOpenEndedQuestion &&
            mcQuestionsComplete &&
            string.IsNullOrEmpty(session.OpenEndedAnswer);

        return new ActiveExamContext(session.ChatId, awaitingOpenEnded);
    }

    private async Task<ExamAnswerResult> EvaluateAndCompleteAsync(
        ExamSession session,
        ExamConfig examConfig,
        User user,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();
        var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        var operations = await _botClientFactory.GetOperationsAsync();

        // Calculate MC score
        bool? mcPassed = null;
        int mcScore = 0;
        int mcCorrectCount = 0;
        if (examConfig.HasMcQuestions && session.McAnswers?.Count > 0)
        {
            mcCorrectCount = CountCorrectAnswers(session.McAnswers, session.ShuffleState);
            mcScore = (int)Math.Round(100.0 * mcCorrectCount / session.McAnswers.Count);
            mcPassed = mcScore >= examConfig.McPassingThreshold;

            _logger.LogInformation(
                "MC score for user {UserId}: {Score}% ({Correct}/{Total}), threshold: {Threshold}%, passed: {Passed}",
                user.Id, mcScore, mcCorrectCount, session.McAnswers.Count, examConfig.McPassingThreshold, mcPassed);
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

        // Send messages to user's DM (in Telegram, private chat ID = user ID)
        var messageChatId = session.UserId;

        if (passed)
        {
            // Execute full approval flow (same as manual approval, but with ExamFlow actor)
            var approvalResult = await ExecuteExamApprovalAsync(
                userId: user.Id,
                chatId: session.ChatId,
                executor: Actor.ExamFlow,
                reason: "Passed entrance exam",
                isManualApproval: false,
                cancellationToken);

            if (!approvalResult.Success)
            {
                _logger.LogWarning(
                    "Failed to complete exam approval for {User} in {Chat}: {Error}",
                    user.ToLogInfo(), session.ChatId, approvalResult.ErrorMessage);
            }

            return new ExamAnswerResult(ExamComplete: true, Passed: true, SentToReview: false, GroupChatId: session.ChatId);
        }

        // Create exam failure review (include shuffle state for review display)
        var examFailure = new ExamFailureRecord
        {
            ChatId = session.ChatId,
            UserId = user.Id,
            McAnswers = session.McAnswers,
            ShuffleState = session.ShuffleState,
            OpenEndedAnswer = session.OpenEndedAnswer,
            Score = mcScore,
            PassingThreshold = examConfig.McPassingThreshold,
            AiEvaluation = aiReasoning,
            FailedAt = DateTimeOffset.UtcNow
        };

        var examFailureId = await reportsRepo.InsertExamFailureAsync(examFailure, cancellationToken);

        // Get chat info for notification
        var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        var failureChat = await managedChatsRepo.GetByChatIdAsync(session.ChatId, cancellationToken);
        var failureChatName = failureChat?.ChatName ?? "Unknown Chat";

        // Notify admins of exam failure
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var userName = TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, user.Id);
        var mcTotal = examConfig.McQuestions.Count;
        var hasOpenEnded = !string.IsNullOrEmpty(examConfig.OpenEndedQuestion);

        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine($"User: {userName}");
        messageBuilder.AppendLine($"Chat: {failureChatName}");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine($"Answered: {mcCorrectCount}/{mcTotal} correct");
        messageBuilder.AppendLine($"Score: {mcScore}% (Required: {examConfig.McPassingThreshold}%)");
        if (hasOpenEnded && !string.IsNullOrEmpty(session.OpenEndedAnswer))
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine($"Question: {examConfig.OpenEndedQuestion}");
            messageBuilder.AppendLine($"Answer: {session.OpenEndedAnswer}");
            if (!string.IsNullOrEmpty(aiReasoning))
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine($"AI: {aiReasoning}");
            }
        }

        await notificationService.SendChatNotificationAsync(
            chatId: session.ChatId,
            eventType: NotificationEventType.ExamFailed,
            subject: "Entrance Exam Review Required",
            message: messageBuilder.ToString(),
            reportId: examFailureId,
            reportedUserId: user.Id,
            reportType: ReportType.ExamFailure,
            cancellationToken: cancellationToken);

        // Send pending message to user in DM
        await operations.SendMessageAsync(
            chatId: messageChatId,
            text: "⏳ Your answers are being reviewed by an admin. Please wait.",
            cancellationToken: cancellationToken);

        _logger.LogInformation("User {User} failed entrance exam in {Chat}, sent to review",
            user.ToLogInfo(), failureChat?.ToLogInfo() ?? session.ChatId.ToString());

        return new ExamAnswerResult(ExamComplete: true, Passed: false, SentToReview: true, GroupChatId: session.ChatId);
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
        var text = ExamMessageBuilder.FormatMcQuestion(username, questionIndex + 1, totalQuestions, question.Question);

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
        var text = ExamMessageBuilder.FormatOpenEndedQuestion(username, examConfig.OpenEndedQuestion!);

        var message = await operations.SendMessageAsync(
            chatId: chatId,
            text: text,
            cancellationToken: cancellationToken);

        return message.MessageId;
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

    /// <inheritdoc />
    public async Task<ModerationResult> ApproveExamFailureAsync(
        long userId,
        long chatId,
        long examFailureId,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        var reason = $"Exam failure #{examFailureId} - manually approved after review";
        return await ExecuteExamApprovalAsync(
            userId,
            chatId,
            executor,
            reason,
            isManualApproval: true,
            cancellationToken);
    }

    /// <summary>
    /// Shared method for completing exam approval (both auto-pass and manual approval flows).
    /// Handles: restore permissions, delete teaser, update welcome response, mark active, send DM.
    /// </summary>
    private async Task<ModerationResult> ExecuteExamApprovalAsync(
        long userId,
        long chatId,
        Actor executor,
        string reason,
        bool isManualApproval,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IBotModerationService>();
        var welcomeResponsesRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var operations = await _botClientFactory.GetOperationsAsync();

        // 1. Restore user permissions via orchestrator (audit trail for both flows)
        var restoreResult = await orchestrator.RestoreUserPermissionsAsync(
            userId: userId,
            chatId: chatId,
            executor: executor,
            reason: reason,
            cancellationToken: cancellationToken);

        if (!restoreResult.Success)
        {
            return restoreResult;
        }

        // 2. Get chat info from Telegram API (needed for Title, Username, and logging)
        ChatFullInfo? groupChat = null;
        try
        {
            groupChat = await operations.GetChatAsync(chatId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get chat info for {ChatId}", chatId);
        }

        var chatName = groupChat?.Title ?? "the chat";

        // 3. Delete teaser and update welcome response
        var welcomeResponse = await welcomeResponsesRepo.GetByUserAndChatAsync(userId, chatId, cancellationToken);
        if (welcomeResponse != null)
        {
            // Delete teaser message via orchestrator (audit trail)
            await orchestrator.DeleteMessageAsync(
                messageId: welcomeResponse.WelcomeMessageId,
                chatId: chatId,
                userId: userId,
                deletedBy: executor,
                reason: "Exam teaser cleanup after approval",
                cancellationToken: cancellationToken);

            // Update welcome response to accepted
            await welcomeResponsesRepo.UpdateResponseAsync(
                welcomeResponse.Id,
                WelcomeResponseType.Accepted,
                dmSent: true,
                dmFallback: false,
                cancellationToken);
        }

        // 4. Mark user as active
        await telegramUserRepo.SetActiveAsync(userId, true, cancellationToken);

        // 5. Build deeplink to return to chat
        string? chatDeepLink = null;
        if (groupChat != null)
        {
            chatDeepLink = WelcomeDeepLinkBuilder.BuildPublicChatLink(groupChat.Username);

            if (chatDeepLink == null)
            {
                // For private chats (no username), try to get invite link
                var inviteLinkService = scope.ServiceProvider.GetRequiredService<IChatInviteLinkService>();
                chatDeepLink = await inviteLinkService.GetInviteLinkAsync(groupChat, cancellationToken);
            }
        }

        // 6. Send success DM to user
        InlineKeyboardMarkup? keyboard = null;
        if (chatDeepLink != null)
        {
            keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard(chatName, chatDeepLink);
        }

        var messageText = isManualApproval
            ? $"✅ Good news! An admin has approved your entrance exam. You can now participate in {chatName}."
            : $"✅ Welcome! You've passed the entrance exam and can now participate in {chatName}.";

        try
        {
            await operations.SendMessageAsync(
                chatId: userId, // DM chat ID = user ID
                text: messageText,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal - user may have blocked bot
            _logger.LogDebug(ex, "Failed to send exam approval DM to user {UserId}", userId);
        }

        _logger.LogInformation(
            "Exam approved for {UserId} in {Chat} by {Executor}",
            userId,
            groupChat?.ToLogInfo() ?? chatId.ToString(),
            executor.DisplayName);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc />
    public async Task<ModerationResult> DenyExamFailureAsync(
        long userId,
        long chatId,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteExamDenialAsync(
            userId,
            chatId,
            executor,
            banGlobally: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModerationResult> DenyAndBanExamFailureAsync(
        long userId,
        long chatId,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteExamDenialAsync(
            userId,
            chatId,
            executor,
            banGlobally: true,
            cancellationToken);
    }

    /// <summary>
    /// Shared method for completing exam denial (both kick and ban flows).
    /// Handles: delete teaser, update welcome response, kick/ban user, send DM notification.
    /// </summary>
    private async Task<ModerationResult> ExecuteExamDenialAsync(
        long userId,
        long chatId,
        Actor executor,
        bool banGlobally,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IBotModerationService>();
        var welcomeResponsesRepo = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        var operations = await _botClientFactory.GetOperationsAsync();

        // 1. Get chat info from Telegram API (needed for notification message)
        string chatName = "the chat";
        try
        {
            var groupChat = await operations.GetChatAsync(chatId, cancellationToken);
            chatName = groupChat.Title ?? chatName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get chat info for {ChatId}", chatId);
        }

        // 2. Delete teaser and update welcome response
        var welcomeResponse = await welcomeResponsesRepo.GetByUserAndChatAsync(userId, chatId, cancellationToken);
        if (welcomeResponse != null)
        {
            // Delete teaser message via orchestrator (audit trail)
            await orchestrator.DeleteMessageAsync(
                messageId: welcomeResponse.WelcomeMessageId,
                chatId: chatId,
                userId: userId,
                deletedBy: executor,
                reason: "Exam teaser cleanup after denial",
                cancellationToken: cancellationToken);

            // Update welcome response to denied
            await welcomeResponsesRepo.UpdateResponseAsync(
                welcomeResponse.Id,
                WelcomeResponseType.Denied,
                dmSent: false,
                dmFallback: false,
                cancellationToken);
        }

        // 3. Kick or ban user
        if (banGlobally)
        {
            // Global ban (no specific message triggered it)
            var banResult = await orchestrator.BanUserAsync(
                userId: userId,
                messageId: null, // No triggering message - exam failure
                executor: executor,
                reason: "Exam failed - banned to prevent repeat join spam",
                cancellationToken: cancellationToken);

            if (!banResult.Success)
            {
                return banResult;
            }
        }
        else
        {
            // Kick from this chat only (user can rejoin)
            var kickResult = await orchestrator.KickUserFromChatAsync(
                userId: userId,
                chatId: chatId,
                executor: executor,
                reason: "Exam denied - kicked from chat",
                cancellationToken: cancellationToken);

            if (!kickResult.Success)
            {
                return kickResult;
            }
        }

        // 4. Send notification DM to user
        var notificationText = banGlobally
            ? $"❌ Your request to join {chatName} has been denied and you have been banned."
            : $"❌ Your request to join {chatName} has been denied after admin review. You may try joining again later.";

        try
        {
            await operations.SendMessageAsync(
                chatId: userId, // DM chat ID = user ID
                text: notificationText,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal - user may have blocked bot
            _logger.LogDebug(ex, "Failed to send exam denial DM to user {UserId}", userId);
        }

        var actionType = banGlobally ? "denied and banned" : "denied (kicked)";
        _logger.LogInformation(
            "Exam {ActionType} for {UserId} in chat {ChatId} by {Executor}",
            actionType,
            userId,
            chatId,
            executor.DisplayName);

        return new ModerationResult { Success = true };
    }
}

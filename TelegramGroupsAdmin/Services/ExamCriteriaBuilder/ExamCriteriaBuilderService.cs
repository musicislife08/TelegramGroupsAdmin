using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

/// <summary>
/// Service for generating AI-powered evaluation criteria for entrance exam open-ended questions.
/// Uses the PromptBuilder AI connection (same as spam veto prompt builder).
/// </summary>
public class ExamCriteriaBuilderService : IExamCriteriaBuilderService
{
    private readonly IChatService _chatService;
    private readonly ILogger<ExamCriteriaBuilderService> _logger;

    public ExamCriteriaBuilderService(
        IChatService chatService,
        ILogger<ExamCriteriaBuilderService> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExamCriteriaBuilderResponse> GenerateCriteriaAsync(
        ExamCriteriaBuilderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if prompt builder feature is available (reuse same connection as spam prompt builder)
            if (!await _chatService.IsFeatureAvailableAsync(AIFeatureType.PromptBuilder, cancellationToken))
            {
                return new ExamCriteriaBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI service not configured for prompt building"
                };
            }

            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(request);

            var result = await _chatService.GetCompletionAsync(
                AIFeatureType.PromptBuilder,
                systemPrompt,
                userPrompt,
                options: null,  // Use feature config defaults
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                return new ExamCriteriaBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI returned empty response"
                };
            }

            var chatContext = request.Chat != null
                ? $" for {request.Chat.Chat.ToLogInfo()}"
                : " (global default)";
            _logger.LogInformation("Generated exam evaluation criteria{ChatContext}, topic: {GroupTopic}",
                chatContext, request.GroupTopic);

            return new ExamCriteriaBuilderResponse
            {
                Success = true,
                GeneratedCriteria = result.Content.Trim(),
                ChatDisplayName = request.Chat?.Chat.ChatName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating exam evaluation criteria");
            return new ExamCriteriaBuilderResponse
            {
                Success = false,
                ErrorMessage = $"Generation failed: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<ExamCriteriaBuilderResponse> ImproveCriteriaAsync(
        string currentCriteria,
        string improvementFeedback,
        ManagedChatRecord? chat = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _chatService.IsFeatureAvailableAsync(AIFeatureType.PromptBuilder, cancellationToken))
            {
                return new ExamCriteriaBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI service not configured for prompt building"
                };
            }

            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildImprovementPrompt(currentCriteria, improvementFeedback);

            var result = await _chatService.GetCompletionAsync(
                AIFeatureType.PromptBuilder,
                systemPrompt,
                userPrompt,
                options: null,  // Use feature config defaults
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                return new ExamCriteriaBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI returned empty response"
                };
            }

            var chatContext = chat != null
                ? $" for {chat.Chat.ToLogInfo()}"
                : " (global default)";
            _logger.LogInformation("Improved exam evaluation criteria{ChatContext} based on feedback", chatContext);

            return new ExamCriteriaBuilderResponse
            {
                Success = true,
                GeneratedCriteria = result.Content.Trim(),
                ChatDisplayName = chat?.Chat.ChatName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error improving exam evaluation criteria");
            return new ExamCriteriaBuilderResponse
            {
                Success = false,
                ErrorMessage = $"Improvement failed: {ex.Message}"
            };
        }
    }

    internal static string BuildSystemPrompt()
    {
        return """
            You are an expert at creating evaluation criteria for community entrance exams.
            Your job is to generate clear, specific criteria that AI can use to evaluate open-ended answers.

            IMPORTANT CONTEXT:
            - This is a ONE-SHOT evaluation, not a conversation
            - The user submits their answer ONCE and gets an immediate pass/fail decision
            - There is NO opportunity for follow-up questions, clarification, or rebuttal
            - If the user fails, they are sent to a human review queue (not auto-rejected)
            - Criteria must be fair for a single attempt - don't require information the user couldn't reasonably provide

            The criteria you generate will be used by another AI to make this single pass/fail decision.

            Output format requirements:
            - Use clear PASS and FAIL sections
            - Be specific and actionable
            - Include word count or detail requirements where appropriate
            - Consider common spam/bot patterns to reject
            - Be fair for a one-shot evaluation (no "trick" requirements)
            - Output plain text only, no markdown code blocks
            """;
    }

    internal static string BuildUserPrompt(ExamCriteriaBuilderRequest request)
    {
        var strictnessGuidance = request.Strictness switch
        {
            ExamStrictnessLevel.Lenient => "Be lenient - accept answers that show any genuine effort or interest. Only reject obvious spam, bots, or completely off-topic responses.",
            ExamStrictnessLevel.Balanced => "Use balanced judgment - require reasonable effort and relevance, but don't demand perfection. Accept good-faith attempts.",
            ExamStrictnessLevel.Strict => "Be strict - require detailed, thoughtful answers that demonstrate clear understanding and genuine interest. Reject low-effort responses.",
            _ => "Use balanced judgment."
        };

        return $"""
            Generate evaluation criteria for an entrance exam question.

            <group_context>
              <topic>{request.GroupTopic}</topic>
            </group_context>

            <exam_question>
            {request.Question}
            </exam_question>

            {(string.IsNullOrWhiteSpace(request.GoodAnswerHints) ? "" : $"""
            <admin_hints_for_good_answers>
            {request.GoodAnswerHints}
            </admin_hints_for_good_answers>
            """)}

            {(string.IsNullOrWhiteSpace(request.FailureIndicators) ? "" : $"""
            <admin_hints_for_failures>
            {request.FailureIndicators}
            </admin_hints_for_failures>
            """)}

            <strictness_level>
            {strictnessGuidance}
            </strictness_level>

            Generate evaluation criteria with two sections:

            PASS if answer:
            - [List specific criteria that indicate a passing answer]
            - [Include relevance, effort, and detail requirements]

            FAIL if answer:
            - [List specific criteria that indicate a failing answer]
            - [Include common spam/bot patterns to reject]

            Output ONLY the criteria in plain text. No preamble, no explanation, no code blocks.
            """;
    }

    internal static string BuildImprovementPrompt(string currentCriteria, string improvementFeedback)
    {
        return $"""
            Improve these entrance exam evaluation criteria based on the admin's feedback.

            <current_criteria>
            {currentCriteria}
            </current_criteria>

            <improvement_request>
            {improvementFeedback}
            </improvement_request>

            Revise the criteria to address the feedback while maintaining the PASS/FAIL structure.
            Keep what works, modify or add rules as needed.

            Output ONLY the improved criteria in plain text. No preamble, no explanation, no code blocks.
            """;
    }
}

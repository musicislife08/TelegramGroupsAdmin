using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// AI-based evaluation service for entrance exam open-ended answers.
/// Uses the content moderation AI connection (SpamDetection feature type).
/// </summary>
public class ExamEvaluationService : IExamEvaluationService
{
    private readonly IChatService _chatService;
    private readonly ILogger<ExamEvaluationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExamEvaluationService(
        IChatService chatService,
        ILogger<ExamEvaluationService> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Reuse the SpamDetection/ContentDetection AI connection (Issue #282 tracks rename)
        return await _chatService.IsFeatureAvailableAsync(AIFeatureType.SpamDetection, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ExamEvaluationResult?> EvaluateAnswerAsync(
        string question,
        string userAnswer,
        string evaluationCriteria,
        string groupTopic,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new ExamEvaluationResult(
                Passed: false,
                Reasoning: "No answer provided",
                Confidence: 1.0);
        }

        try
        {
            var systemPrompt = """
                You are an entrance exam evaluator for an online community.
                Your job is to determine if a user's answer demonstrates genuine interest in joining the group.

                IMPORTANT CONTEXT:
                - This is a ONE-SHOT evaluation, not a conversation
                - The user submitted their answer once with no chance for follow-up or clarification
                - If you mark as failed, the user goes to a human review queue (not auto-rejected)
                - Be fair for a single attempt - give benefit of the doubt for minor issues

                You must respond with valid JSON in this exact format:
                {
                  "passed": true or false,
                  "reasoning": "brief explanation of your decision (1-2 sentences)",
                  "confidence": 0.0 to 1.0
                }

                Be fair but firm. Accept answers that show genuine effort even if imperfect.
                Reject generic, off-topic, or bot-like responses.
                """;

            var userPrompt = $"""
                Evaluate this entrance exam answer.

                <group_context>
                  <topic>{groupTopic}</topic>
                </group_context>

                <exam_question>
                {question}
                </exam_question>

                <user_answer>
                {userAnswer}
                </user_answer>

                <evaluation_criteria>
                {evaluationCriteria}
                </evaluation_criteria>

                Respond with JSON matching the format specified in the system prompt.
                """;

            var options = new ChatCompletionOptions
            {
                JsonMode = true  // Required for structured response parsing; other settings from feature config
            };

            // Reuse the SpamDetection/ContentDetection AI connection (Issue #282 tracks rename)
            var result = await _chatService.GetCompletionAsync(
                AIFeatureType.SpamDetection,
                systemPrompt,
                userPrompt,
                options,
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                _logger.LogDebug("AI exam evaluation not configured or returned empty response");
                return null;
            }

            var content = result.Content.Trim();

            // Remove markdown code blocks if present
            if (content.StartsWith("```"))
            {
                var lines = content.Split('\n');
                content = string.Join('\n', lines.Skip(1).SkipLast(1)).Trim();
            }

            var evalResult = JsonSerializer.Deserialize<EvaluationApiResult>(content, JsonOptions);
            if (evalResult == null)
            {
                _logger.LogWarning("Failed to parse AI exam evaluation result: {Content}", content);
                return null;
            }

            _logger.LogInformation(
                "Exam answer evaluated: passed={Passed}, confidence={Confidence:F2}, reasoning={Reasoning}",
                evalResult.Passed,
                evalResult.Confidence,
                evalResult.Reasoning);

            return new ExamEvaluationResult(
                Passed: evalResult.Passed,
                Reasoning: evalResult.Reasoning ?? "No reasoning provided",
                Confidence: Math.Clamp(evalResult.Confidence, 0.0, 1.0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate exam answer using AI");
            return null;
        }
    }

    /// <summary>
    /// Structure for parsing AI's evaluation result
    /// </summary>
    private record EvaluationApiResult
    {
        public bool Passed { get; init; }
        public string? Reasoning { get; init; }
        public double Confidence { get; init; }
    }
}

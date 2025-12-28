using System.Text;
using System.Text.Json;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.Ui.Server.Services.PromptBuilder;

/// <summary>
/// Service for generating AI-powered custom spam detection prompts.
/// Provider-agnostic - uses IChatService for multi-provider support.
/// Configuration loaded from database (hot-reload support)
/// </summary>
public class PromptBuilderService : IPromptBuilderService
{
    private readonly IChatService _chatService;
    private readonly IDetectionResultsRepository _detectionResultsRepository;
    private readonly ISystemConfigRepository _configRepo;
    private readonly ILogger<PromptBuilderService> _logger;

    public PromptBuilderService(
        IChatService chatService,
        IDetectionResultsRepository detectionResultsRepository,
        ISystemConfigRepository configRepo,
        ILogger<PromptBuilderService> logger)
    {
        _chatService = chatService;
        _detectionResultsRepository = detectionResultsRepository;
        _configRepo = configRepo;
        _logger = logger;
    }

    public async Task<PromptBuilderResponse> GeneratePromptAsync(
        PromptBuilderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if prompt builder feature is available
            if (!await _chatService.IsFeatureAvailableAsync(AIFeatureType.PromptBuilder, cancellationToken))
            {
                return new PromptBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI service not configured for prompt building"
                };
            }

            // Load training samples for context (spam is spam regardless of chat)
            var trainingSamples = await LoadTrainingSamplesAsync(request.MessageHistoryCount, cancellationToken);

            // Build the meta-prompt
            var metaPrompt = BuildMetaPrompt(request, trainingSamples);

            // Call AI service
            var result = await _chatService.GetCompletionAsync(
                AIFeatureType.PromptBuilder,
                "You are an expert at creating spam detection rules for online communities.",
                metaPrompt,
                new ChatCompletionOptions
                {
                    MaxTokens = 1000, // Cap for detailed rules
                    Temperature = 0.3 // Cap for focused output
                },
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                return new PromptBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI returned empty response"
                };
            }

            // Serialize request metadata for storage
            var metadata = JsonSerializer.Serialize(new
            {
                topic = request.Topic,
                groupDescription = request.GroupDescription,
                customRules = request.CustomRules,
                commonSpamPatterns = request.CommonSpamPatterns,
                legitimateExamples = request.LegitimateExamples,
                strictness = request.Strictness.ToString(),
                messageHistoryCount = request.MessageHistoryCount,
                generatedAt = DateTimeOffset.UtcNow
            });

            return new PromptBuilderResponse
            {
                Success = true,
                GeneratedPrompt = result.Content,
                GenerationMetadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating custom prompt for chat {ChatId}", request.ChatId);
            return new PromptBuilderResponse
            {
                Success = false,
                ErrorMessage = $"Generation failed: {ex.Message}"
            };
        }
    }

    public async Task<PromptBuilderResponse> ImprovePromptAsync(
        string currentPrompt,
        string improvementFeedback,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if prompt builder feature is available
            if (!await _chatService.IsFeatureAvailableAsync(AIFeatureType.PromptBuilder, cancellationToken))
            {
                return new PromptBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI service not configured for prompt building"
                };
            }

            // Build the improvement meta-prompt
            var metaPrompt = BuildImprovementMetaPrompt(currentPrompt, improvementFeedback);

            // Call AI service
            var result = await _chatService.GetCompletionAsync(
                AIFeatureType.PromptBuilder,
                "You are an expert at creating spam detection rules for online communities.",
                metaPrompt,
                new ChatCompletionOptions
                {
                    MaxTokens = 1000, // Cap for detailed rules
                    Temperature = 0.3 // Cap for focused output
                },
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.Content))
            {
                return new PromptBuilderResponse
                {
                    Success = false,
                    ErrorMessage = "AI returned empty response"
                };
            }

            // Serialize metadata for storage
            var metadata = JsonSerializer.Serialize(new
            {
                improvedFrom = currentPrompt.Length > 100 ? currentPrompt[..100] + "..." : currentPrompt,
                improvementFeedback = improvementFeedback,
                generatedAt = DateTimeOffset.UtcNow
            });

            return new PromptBuilderResponse
            {
                Success = true,
                GeneratedPrompt = result.Content,
                GenerationMetadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error improving prompt");
            return new PromptBuilderResponse
            {
                Success = false,
                ErrorMessage = $"Improvement failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Load training samples for context
    /// Includes both spam and ham examples to help AI understand what the system has learned
    /// Training samples are global - spam is spam regardless of which chat it came from
    /// </summary>
    private async Task<string> LoadTrainingSamplesAsync(
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0) return string.Empty;

        try
        {
            // Get training samples (high-quality spam/ham classifications)
            var samples = await _detectionResultsRepository.GetTrainingSamplesAsync(cancellationToken);

            if (!samples.Any()) return "No training data available yet.";

            var sb = new StringBuilder();
            sb.AppendLine("Example spam and legitimate messages from training data:");
            sb.AppendLine();

            // Get a balanced sample: half spam, half ham
            var spamSamples = samples.Where(s => s.IsSpam).Take(count / 2).ToList();
            var hamSamples = samples.Where(s => !s.IsSpam).Take(count / 2).ToList();
            var combined = spamSamples.Concat(hamSamples).OrderBy(_ => Random.Shared.Next()).Take(count);

            foreach (var (messageText, isSpam) in combined)
            {
                var label = isSpam ? "[SPAM]" : "[OK]";
                var truncated = messageText.Length > 150
                    ? messageText[..150] + "..."
                    : messageText;

                sb.AppendLine($"{label} {truncated}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load training samples");
            return "Training data unavailable.";
        }
    }

    /// <summary>
    /// Build the meta-prompt for improving an existing prompt based on user feedback
    /// </summary>
    private string BuildImprovementMetaPrompt(string currentPrompt, string improvementFeedback)
    {
        return $$"""
            You are an expert at creating spam detection rules for Telegram groups.
            Your task is to improve an existing spam detection prompt based on specific feedback.

            ## Current Prompt
            {{currentPrompt}}

            ## Improvement Request
            {{improvementFeedback}}

            ## Your Task
            Revise the above spam detection rules to address the improvement request.
            Maintain the overall structure (SPAM indicators section and LEGITIMATE content section).
            Keep what works, but modify or add rules to address the specific feedback.

            Output ONLY the improved prompt in plain text with the same two-section format:
            - ### SPAM indicators (mark as "spam"):
            - ### LEGITIMATE content (mark as "clean"):

            Do not include any preamble, explanation, or JSON. Just the improved rules.
            """;
    }

    /// <summary>
    /// Build the meta-prompt that tells AI how to generate good spam detection rules
    /// </summary>
    private string BuildMetaPrompt(PromptBuilderRequest request, string trainingSamples)
    {
        var strictnessGuidance = request.Strictness switch
        {
            StrictnessLevel.Conservative => "Be very cautious - only flag obvious spam. False negatives (letting spam through) are preferred over false positives (blocking legitimate content).",
            StrictnessLevel.Balanced => "Use balanced judgment - flag clear spam but allow borderline cases. Aim for accuracy.",
            StrictnessLevel.Aggressive => "Be strict - flag anything questionable. False positives (blocking borderline content) are acceptable to catch more spam.",
            _ => "Use balanced judgment."
        };

        return $$"""
            You are an expert at creating spam detection rules for Telegram groups.
            Your task is to generate custom spam detection rules for a specific group.

            ## Group Context
            Topic: {{request.Topic}}
            Description: {{request.GroupDescription}}

            {{(string.IsNullOrWhiteSpace(request.CustomRules) ? "" : $"Custom Rules from Admin:\n{request.CustomRules}\n")}}
            {{(string.IsNullOrWhiteSpace(request.CommonSpamPatterns) ? "" : $"Common Spam Patterns Observed:\n{request.CommonSpamPatterns}\n")}}
            {{(string.IsNullOrWhiteSpace(request.LegitimateExamples) ? "" : $"Legitimate Content Examples:\n{request.LegitimateExamples}\n")}}

            ## Strictness Level
            {{strictnessGuidance}}

            {{trainingSamples}}

            ## Your Task
            Generate clear, specific spam detection rules for this group. Your output will REPLACE the default rules in the AI spam detection system.

            Generate two sections:

            ### SPAM indicators (mark as "spam"):
            - [List specific patterns and characteristics that indicate spam FOR THIS SPECIFIC GROUP]
            - Be specific to this group's topic and culture
            - Reference actual patterns from the message history if relevant

            ### LEGITIMATE content (mark as "clean"):
            - [List what should be allowed in this group]
            - Be specific about legitimate discussions for this topic
            - Ensure the legitimate messages shown above would not be flagged

            Make your rules actionable and specific. Use terminology from this community.
            Focus on patterns, not just keywords.

            Output ONLY the two sections above in plain text with bullet points. No JSON, no code blocks, no preamble.
            """;
    }
}

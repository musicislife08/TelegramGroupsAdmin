using System.Text;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Static utility class for building OpenAI API prompts
/// Extracted from OpenAISpamCheck (Phase 4.5 modular prompt system)
/// </summary>
public static class OpenAIPromptBuilder
{
    /// <summary>
    /// Create enhanced OpenAI API request with history context and JSON response format
    /// Phase 4.5: Uses modular prompt building system
    /// </summary>
    public static OpenAIRequest CreateRequest(
        OpenAICheckRequest req,
        IEnumerable<HistoryMessage> history)
    {
        // Phase 4.5: Use modular prompt builder
        // Custom prompt (if provided) replaces the default rules section only
        var systemPrompt = BuildSystemPrompt(req.VetoMode, req.SystemPrompt);

        // Build context from message history
        var contextBuilder = new StringBuilder();

        if (history.Any())
        {
            contextBuilder.AppendLine("\nRecent message history for context:");
            // TODO: Make message history count configurable (currently hardcoded to 3, but 5 messages are fetched)
            // Add OpenAIConfig.MessageHistoryCount property and use it here instead of Take(3)
            foreach (var msg in history.Take(3))
            {
                var status = msg.WasSpam ? "[SPAM]" : "[OK]";
                contextBuilder.AppendLine($"{status} {msg.UserName}: {msg.Message.Substring(0, Math.Min(100, msg.Message.Length))}");
            }
            contextBuilder.AppendLine();
        }

        var userPrompt = req.VetoMode
            ? $$"""
               Analyze this message that was flagged by other spam filters. Is it actually spam?

               {{contextBuilder}}
               Current message from user {{req.UserName}} (ID: {{req.UserId}}):
               "{{req.Message}}"

               Respond with JSON: {"result": "spam" or "clean" or "review", "reason": "explanation", "confidence": 0.0-1.0}
               """
            : $$"""
              Analyze this message for spam content.

              {{contextBuilder}}
              Message from user {{req.UserName}} (ID: {{req.UserId}}):
              "{{req.Message}}"

              Respond with JSON: {"result": "spam" or "clean" or "review", "reason": "explanation", "confidence": 0.0-1.0}
              """;

        return new OpenAIRequest
        {
            Model = req.Model,
            Messages =
            [
                new OpenAIMessage { Role = "system", Content = systemPrompt },
                new OpenAIMessage { Role = "user", Content = userPrompt }
            ],
            MaxTokens = req.MaxTokens,
            Temperature = 0.1,
            TopP = 1.0,
            ResponseFormat = new { type = "json_object" }
        };
    }

    /// <summary>
    /// Phase 4.5: Build complete system prompt from modular components
    /// </summary>
    public static string BuildSystemPrompt(bool vetoMode, string? customRulesPrompt = null)
    {
        var baseTechnical = GetBaseTechnicalPrompt();
        var rules = customRulesPrompt ?? GetDefaultRulesPrompt();
        var modeGuidance = GetModeGuidancePrompt(vetoMode);

        return $$"""
            {{baseTechnical}}

            {{rules}}

            {{modeGuidance}}

            Consider the message context, user history, and conversation flow when making your decision.
            Always respond with valid JSON format.
            """;
    }

    /// <summary>
    /// Phase 4.5: Get technical base prompt (unchangeable by users)
    /// Defines JSON format and result types
    /// </summary>
    public static string GetBaseTechnicalPrompt()
    {
        return """
            You must respond with valid JSON in this exact format:
            {
              "result": "spam" | "clean" | "review",
              "reason": "clear explanation of your decision",
              "confidence": 0.0-1.0
            }

            Result types:
            - "spam": Message is definitely spam/scam/unwanted
            - "clean": Message is legitimate conversation
            - "review": Uncertain - requires human review
            """;
    }

    /// <summary>
    /// Phase 4.5: Get default spam/legitimate content rules
    /// Can be overridden by chat-specific custom prompts
    /// </summary>
    public static string GetDefaultRulesPrompt()
    {
        return """
            SPAM indicators (mark as "spam"):
            - Personal testimonials promoting paid services/individuals ("X transformed my life/trading/income")
            - Direct solicitation or selling of services
            - Get-rich-quick schemes or unrealistic profit promises
            - Requests to contact someone for trading/investment advice
            - Scam signals: "fee-free", "guaranteed profits", "no tricks", success stories
            - Unsolicited financial advice with calls-to-action
            - Adult content, obvious scams, repetitive spam patterns

            LEGITIMATE content (mark as "clean"):
            - Genuine discussion about crypto, trading, AI, or technology topics
            - Educational content, tutorials, or proof-of-concepts being shared
            - News articles, research, or analysis
            - Questions and answers about topics
            - Sharing legitimate tools, resources, or links for discussion
            - Normal conversation about markets, technology, or current events

            Key distinction: Sharing knowledge/discussion = legitimate. Promoting services/testimonials = spam.
            """;
    }

    /// <summary>
    /// Phase 4.5: Get mode-specific guidance (veto vs detection)
    /// </summary>
    public static string GetModeGuidancePrompt(bool vetoMode)
    {
        if (vetoMode)
        {
            return """
                MODE: Spam Verification (Veto)
                Other filters have flagged this message as potential spam. Your job is to verify if it's actually spam or a false positive.

                Return "spam" (confirm spam) if:
                - The message clearly matches spam indicators above
                - Contains personal testimonials, solicitation, or promotional content
                - You agree with the other filters' assessment

                Return "clean" (veto/override) if:
                - The message is educational, informational, or conversational in nature
                - No direct solicitation or testimonial promoting paid services
                - Legitimate sharing of resources, tools, or ideas for group discussion
                - You disagree with the other filters (false positive)

                Return "review" if:
                - You're uncertain whether it's spam or legitimate
                - The message is borderline or context-dependent
                - Human judgment would be more reliable

                Be cautious with vetoes - only override if you're confident it's a false positive.
                """;
        }

        return """
            MODE: Spam Detection
            Analyze this message and determine if it's spam.

            Return "spam" if:
            - Message clearly matches spam indicators above
            - Promotional, solicitation, or scam content

            Return "clean" if:
            - Legitimate conversation or discussion
            - When in doubt, lean toward "clean" to preserve conversation

            Return "review" if:
            - Uncertain or borderline case
            - Requires human judgment

            Be conservative - false positives (blocking legitimate messages) are worse than false negatives.
            """;
    }
}

/// <summary>
/// OpenAI API request structure
/// </summary>
public record OpenAIRequest
{
    public required string Model { get; init; }
    public required OpenAIMessage[] Messages { get; init; }
    public int MaxTokens { get; init; }
    public double Temperature { get; init; }
    public double TopP { get; init; }
    public object? ResponseFormat { get; init; }
}

/// <summary>
/// OpenAI message structure
/// </summary>
public record OpenAIMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

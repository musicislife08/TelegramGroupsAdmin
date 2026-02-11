using System.Text;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Static utility class for building AI prompts for spam veto.
/// Provider-agnostic - returns prompts that can be used with any AI provider via IChatService.
/// AI always runs as veto to verify spam detection from other checks.
/// </summary>
public static class AIPromptBuilder
{
    /// <summary>
    /// Create system and user prompts for spam veto verification.
    /// Returns prompts that can be passed to IChatService.GetCompletionAsync().
    /// </summary>
    public static AIPromptResult CreatePrompts(
        AIVetoCheckRequest req,
        IEnumerable<HistoryMessage> history)
    {
        // Use modular prompt builder
        // Custom prompt (if provided) replaces the default rules section only
        var systemPrompt = BuildSystemPrompt(req.SystemPrompt);

        // Combine caption and OCR text for analysis (handles image-only messages with OCR)
        var effectiveText = req.Message;
        if (!string.IsNullOrWhiteSpace(req.OcrExtractedText))
        {
            effectiveText = string.IsNullOrWhiteSpace(effectiveText)
                ? req.OcrExtractedText
                : $"{effectiveText}\n\n[IMAGE TEXT]\n{req.OcrExtractedText}";
        }

        // Build context from message history
        // XML tags clearly delineate user-generated content to prevent prompt injection
        var contextBuilder = new StringBuilder();

        if (history.Any())
        {
            contextBuilder.AppendLine("\n<message_history>");
            // Use all history messages passed in (count controlled by MessageHistoryCount config)
            // 500 char limit captures 95% of messages fully (based on production data analysis)
            foreach (var msg in history)
            {
                var status = msg.WasSpam ? "spam" : "ok";
                var truncatedMessage = msg.Message.Length > 500
                    ? msg.Message[..500] + "..."
                    : msg.Message;
                contextBuilder.AppendLine($"<historical_message status=\"{status}\">");
                contextBuilder.AppendLine($"  <username>{msg.UserName}</username>");
                contextBuilder.AppendLine($"  <content>{truncatedMessage}</content>");
                contextBuilder.AppendLine("</historical_message>");
            }
            contextBuilder.AppendLine("</message_history>\n");
        }

        // AI always runs as veto - message was already flagged by other spam checks
        // XML tags wrap all user-generated content to prevent prompt injection attacks
        var userPrompt = $$"""
            Analyze this message that was flagged by other spam filters. Is it actually spam?

            {{contextBuilder}}
            <current_message>
              <username>{{req.User.DisplayName}}</username>
              <user_id>{{req.User.Id}}</user_id>
              <content>
            {{effectiveText}}
              </content>
            </current_message>

            Respond with JSON: {"result": "spam" or "clean" or "review", "reason": "explanation", "confidence": 0.0-1.0}
            """;

        return new AIPromptResult(systemPrompt, userPrompt);
    }

    /// <summary>
    /// Build complete system prompt from modular components
    /// </summary>
    public static string BuildSystemPrompt(string? customRulesPrompt = null)
    {
        var baseTechnical = GetBaseTechnicalPrompt();
        var rules = customRulesPrompt ?? GetDefaultRulesPrompt();
        var modeGuidance = GetVetoGuidancePrompt();

        return $$"""
            {{baseTechnical}}

            {{rules}}

            {{modeGuidance}}

            Consider the message context, user history, and conversation flow when making your decision.
            Always respond with valid JSON format.
            """;
    }

    /// <summary>
    /// Get technical base prompt (unchangeable by users)
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
    /// Get default spam/legitimate content rules
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
    /// Get veto mode guidance - AI verifies spam detected by other checks
    /// </summary>
    public static string GetVetoGuidancePrompt()
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
}

/// <summary>
/// Result of prompt building - contains system and user prompts for AI chat completion.
/// </summary>
public record AIPromptResult(string SystemPrompt, string UserPrompt);

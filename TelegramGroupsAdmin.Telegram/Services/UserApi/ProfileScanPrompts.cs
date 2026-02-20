using System.Security;
using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// AI prompt templates for profile scanning.
/// Follows the AIPromptBuilder pattern from ContentDetection.
/// </summary>
internal static class ProfileScanPrompts
{
    /// <summary>
    /// XML-escape user-generated content to prevent prompt injection.
    /// Escapes &lt;, &gt;, &amp;, &quot;, and &apos;.
    /// </summary>
    internal static string SanitizeForPrompt(string? text)
        => string.IsNullOrEmpty(text) ? "" : SecurityElement.Escape(text);

    internal static string BuildSystemPrompt() =>
        """
        You are a profile safety analyzer for Telegram group administration.
        You will receive a user's complete profile including images and text.
        Analyze ALL provided evidence to determine if this is a spam, adult content,
        or solicitation account.

        You must respond with valid JSON in this exact format:
        {"spam": true/false, "confidence": 0-100, "reason": "clear explanation", "signals_detected": ["signal1", "signal2"]}

        SPAM/BAN indicators:
        - Explicit or pornographic profile photo or story images
        - Sexually suggestive profile photo designed to attract clicks
        - Bio text promoting adult services, escort services, or sexual solicitation
        - Personal channel linking to adult/porn/scam content
        - Story content containing explicit imagery or adult solicitation
        - Combination of suggestive name + suggestive photo + solicitation bio
        - Bio or channel description containing known spam domains or URL shorteners

        CLEAN indicators:
        - Normal profile photo (selfie, pet, landscape, avatar, no photo)
        - Bio about hobbies, work, education, or interests
        - Personal channel about legitimate topics
        - Stories showing normal life content

        Be aggressive with obvious porn bots but cautious with borderline cases.
        If uncertain, return confidence < 60 to flag for admin review rather than auto-ban.
        """;

    internal static string BuildUserPrompt(
        string? firstName,
        string? lastName,
        string? username,
        string? bio,
        string? channelTitle,
        string? channelAbout,
        int storyCount,
        IReadOnlyList<string>? storyCaptions,
        int imageCount)
    {
        var captionsBlock = "";
        if (storyCaptions is { Count: > 0 })
        {
            var sanitizedCaptions = storyCaptions
                .Select(c => $"    <caption>{SanitizeForPrompt(c)}</caption>");
            captionsBlock = $$"""
              <captions>
            {{string.Join("\n", sanitizedCaptions)}}
              </captions>
            """;
        }

        return $$"""
            Analyze this Telegram user profile for spam/adult content indicators.

            <profile>
              <display_name>{{SanitizeForPrompt(firstName)}} {{SanitizeForPrompt(lastName)}}</display_name>
              <username>{{SanitizeForPrompt(username)}}</username>
              <bio>{{(string.IsNullOrEmpty(bio) ? "No bio set" : SanitizeForPrompt(bio))}}</bio>
            </profile>

            <personal_channel>
              <title>{{(string.IsNullOrEmpty(channelTitle) ? "No personal channel" : SanitizeForPrompt(channelTitle))}}</title>
              <description>{{(string.IsNullOrEmpty(channelAbout) ? "" : SanitizeForPrompt(channelAbout))}}</description>
            </personal_channel>

            <stories>
              <story_count>{{storyCount}}</story_count>
            {{captionsBlock}}</stories>

            <images>
              <image_count>{{imageCount}}</image_count>
            </images>

            Respond with JSON: {"spam": true/false, "confidence": 0-100, "reason": "...", "signals_detected": [...]}
            """;
    }
}

/// <summary>
/// Deserialization target for the AI profile scan response.
/// </summary>
internal record ProfileScanAIResponse(
    [property: JsonPropertyName("spam")] bool Spam,
    [property: JsonPropertyName("confidence")] int Confidence,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("signals_detected")] string[]? SignalsDetected);

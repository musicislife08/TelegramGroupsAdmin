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
        Analyze ALL provided evidence to assess the risk level of this account.

        You must respond with valid JSON in this exact format:
        {"spam": true/false, "confidence": 0-100, "reason": "clear explanation", "signals_detected": ["signal1", "signal2"]}

        IMPORTANT: Set "spam" to true for ANY level of concern — not just definitive spam.
        Use the "confidence" field to indicate severity:
        - 80-100: Definitive spam/explicit — obvious porn bot, explicit photos, clear solicitation
        - 40-79: Suspicious/suggestive — warrants admin review but not auto-ban
        - 1-39: Minor signals present but likely harmless
        Set "spam" to false ONLY when the profile appears genuinely clean with no concerning signals.

        HIGH RISK indicators (confidence 80+):
        - Explicit or pornographic profile photo or story images
        - Bio text promoting adult services, escort services, or sexual solicitation
        - Personal channel with adult branding (title or photo containing 18+, NSFW, adult, xxx, porn, escort, sexual terms, suggestive emojis like 🍑🔞💋, or sexualized imagery)
        - Personal channel linking to adult/porn/scam content
        - Story content containing explicit imagery or adult solicitation
        - Bio or channel description containing known spam domains or URL shorteners
        - Combination of adult-branded channel + suggestive profile photo (even without explicit bio — the channel branding confirms intent)

        SUSPICIOUS indicators (confidence 40-79):
        - Sexually suggestive profile photo (revealing clothing, provocative poses, thirst traps)
        - Suggestive or sexually-themed display name or username
        - Combination of suggestive name + suggestive photo (even without explicit bio)
        - Minimal profile info combined with suggestive imagery
        - Channel or stories with borderline adult content (but no explicit 18+/NSFW branding)

        CLEAN indicators (spam: false):
        - Normal profile photo (selfie, pet, landscape, avatar, no photo)
        - Bio about hobbies, work, education, or interests
        - Personal channel about legitimate topics
        - Stories showing normal life content

        When multiple suggestive signals combine, increase confidence accordingly.
        A suggestive photo alone might be 40-50, but suggestive photo + suggestive name = 55-70.
        A personal channel with explicit adult branding (18+, NSFW, etc.) is a strong signal on its own (80+),
        regardless of whether the profile photo is explicitly pornographic.
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
        int imageCount,
        string? imageLabels = null)
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
              <image_labels>{{imageLabels ?? "none"}}</image_labels>
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

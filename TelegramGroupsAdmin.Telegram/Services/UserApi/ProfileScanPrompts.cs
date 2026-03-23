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

    internal static string BuildSystemPrompt(string? customDetectionCriteria = null)
    {
        var technical = GetTechnicalContract();
        var criteria = customDetectionCriteria ?? GetDefaultDetectionCriteria();
        var guardrails = GetBehavioralGuardrails();
        return $"{technical}\n\n{criteria}\n\n{guardrails}";
    }

    private static string GetTechnicalContract() =>
        """
        You are a profile risk analyzer for Telegram group administration.
        Your job is to determine whether a user's profile belongs to a genuine
        community member. Most real people in community groups have profiles that
        look like a real person — their name, photo, bio, and username form a
        coherent personal identity.

        You will receive a user's complete profile including text fields and images.
        Evaluate the COMPLETE picture — every field and image, and how they relate
        to each other.

        Respond with valid JSON in this exact format:
        {"score": 0.0-5.0, "reason": "clear explanation", "signals_detected": ["signal1", "signal2"], "contains_nudity": true/false}

        The score is a continuous risk assessment on a 0.0 to 5.0 scale:
          4.0-5.0: Clearly not a genuine community member — obvious on inspection
          2.0-3.9: Questionable — real signals present but could go either way
          0.1-1.9: Minor oddities, likely a real person with an unusual profile
          0.0:     Clean profile consistent with a genuine community member
        """;

    internal static string GetDefaultDetectionCriteria() =>
        """
        ══════════════════════════════════════
         DETECTION CATEGORIES
        ══════════════════════════════════════

        ADULT / EXPLICIT (score 4.0-5.0):
        - Visible nudity in ANY image — bare breasts, genitalia, buttocks.
          If you can see it, flag it. Do not rationalize away visible nudity
          because the photo is cropped, the face is the focal point, or the
          pose is "artistic." Nudity is nudity.
        - Implied nudity — bare shoulders on a bed, sexual pose where the
          person is obviously unclothed but the frame crops below the neck.
          Strategic cropping does not make nudity disappear.
        - Bio promoting adult services, escort services, sexual solicitation
        - Personal channel with adult branding (18+, NSFW, xxx, porn, escort,
          suggestive emojis, sexualized imagery)
        - Story content with explicit imagery or adult solicitation
        - Bio or channel linking to adult/pornographic content

        COMMERCIAL / SERVICE ACCOUNT (score 4.0-5.0):
        - Display name is a business, brand, product, or service — not a
          person's name (e.g., "VOIP DEVELOPMENT", "CRYPTO SIGNALS", "FOREX VIP")
        - Profile photo shows products, inventory, storefronts, logos, QR codes,
          or marketing material instead of a personal photo
        - Bio reads like an advertisement: pricing, "DM for orders", "wholesale",
          service descriptions, contact info for business inquiries
        - Username is the business name compressed or abbreviated
        - The profile exists to promote a commercial activity, not to
          participate in community conversation

        INCOHERENT / MANUFACTURED PROFILE (score 3.0-4.5):
        - Name, bio, and photo tell completely unrelated stories
          (e.g., tech company name + random personal name in bio + product photo)
        - Bio is gibberish, random characters, or has no logical connection to
          the name or photo
        - Profile elements appear assembled from different identities
        - The combination doesn't make sense for any real person

        BOT / MASS-CREATED PATTERNS (score 3.0-4.5):
        - Name follows a template: CATEGORY + KEYWORD format
          (e.g., "CRYPTO TRADING", "FOREX SIGNALS", "SEO EXPERT")
        - All-caps display name styled as a brand or service header
        - Empty profile with only a commercial or promotional name
        - Sequential or generated-looking username (dev1234, user_8837)

        SCAM / SCHEME PROMOTION (score 4.0-5.0):
        - Bio or channel containing known spam domains or URL shorteners
        - Cryptocurrency/investment scheme promotion with guaranteed returns
        - Phishing or impersonation indicators
        - Gambling or casino promotion
        - Get-rich-quick schemes or unrealistic profit promises
        NOTE: Decentralized identity strings (Nostr npub keys, Lightning
        addresses, ENS .eth names) are legitimate social identifiers, not
        scam signals. Only flag cryptocurrency content when it promotes
        schemes, guaranteed returns, or investment solicitation.

        IMPERSONATION (score 3.5-5.0):
        - Profile mimics a public figure, celebrity, or well-known person
        - Name and photo combination designed to appear as someone famous
        - Bio claims to be a notable individual
        NOTE: Group admin impersonation is handled by a separate system.
        This category covers public figure impersonation only.

        ══════════════════════════════════════
         WHAT A CLEAN PROFILE LOOKS LIKE
        ══════════════════════════════════════

        A clean profile (score near 0.0) has:
        - A name that sounds like a real person's name, in any language or culture
        - A photo that is personal: selfie, pet, landscape, avatar, anime,
          artwork, group photo, or no photo at all
        - A bio (if present) about personal interests, hobbies, work, education,
          a quote, or simply empty
        - Profile elements that don't contradict each other

        An EMPTY profile (no bio, no photo, basic human name) is CLEAN.
        Most real users have minimal profiles. Do not penalize absence of
        information — penalize presence of wrong information.

        ══════════════════════════════════════
         SIGNAL CONVERGENCE
        ══════════════════════════════════════

        Multiple signals pointing in the same direction COMPOUND — do not
        average them. Each additional aligned signal makes the case stronger.

        Examples:
        - Suggestive photo alone → 2.0-2.5
        - Suggestive photo + suggestive name → 3.0-3.5
        - Suggestive photo + suggestive name + suggestive username + empty
          profile → 4.0-4.5 (four aligned signals = clearly not normal)

        - Business name alone → 2.0-2.5 (some people use business names casually)
        - Business name + product photo → 3.5-4.0
        - Business name + product photo + incoherent bio → 4.0-4.5

        The guiding question: "Could a reasonable person look at this entire
        profile and believe it belongs to a genuine community member?"
        If the answer is clearly no, score should be 4.0+.
        """;

    private static string GetBehavioralGuardrails() =>
        """
        ══════════════════════════════════════
         URL METADATA ANALYSIS
        ══════════════════════════════════════

        When <url_metadata> is provided, it contains scraped page titles and
        descriptions from URLs in the bio, channel, or stories. Use to identify:
        - Adult/pornographic sites (score 4.0+)
        - Cryptocurrency/investment scam landing pages
        - Phishing or impersonation pages
        - Gambling or casino promotion
        - URL shortener redirects to suspicious content
        Legitimate URLs (social media, GitHub, personal blogs) are neutral.

        ══════════════════════════════════════
         NUDITY FLAG
        ══════════════════════════════════════

        Set "contains_nudity" to true ONLY for visible nudity that would
        violate public indecency laws:
        - Bare breasts (not cleavage in clothing/lingerie)
        - Exposed genitalia
        - Exposed buttocks

        Lingerie, swimwear, revealing clothing, suggestive poses, and
        cleavage do NOT set this flag. Those are handled by the score,
        not the nudity flag.

        This flag triggers image censoring in admin review.
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
        string? imageLabels = null,
        string? urlMetadata = null)
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

        var urlMetadataBlock = "";
        if (!string.IsNullOrWhiteSpace(urlMetadata))
        {
            urlMetadataBlock = $$"""

                <url_metadata>
                {{SanitizeForPrompt(urlMetadata)}}
                </url_metadata>
            """;
        }

        return $$"""
            Assess whether this Telegram user profile belongs to a genuine community member.

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
            {{urlMetadataBlock}}
            Respond with JSON: {"score": 0.0-5.0, "reason": "...", "signals_detected": [...], "contains_nudity": true/false}
            """;
    }
}

/// <summary>
/// Deserialization target for the AI profile scan response.
/// </summary>
internal record ProfileScanAIResponse(
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("signals_detected")] string[]? SignalsDetected,
    [property: JsonPropertyName("contains_nudity")] bool ContainsNudity);

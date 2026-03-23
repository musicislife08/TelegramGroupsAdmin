# Profile Scanner Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the profile scanner from "flag known-bad" to "flag deviation from genuine community member" with a new AI prompt, simplified scoring, and 6 detection categories.

**Architecture:** Three-part prompt sandwich (technical contract / detection criteria / guardrails) replaces the monolithic system prompt. AI returns a 0.0–5.0 score directly, eliminating the 3-bucket confidence mapping. `AiConfidence` removed from the entire stack.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL 18, OpenAI API, NUnit + NSubstitute

**Spec:** `docs/superpowers/specs/2026-03-23-profile-scanner-redesign-design.md`

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs` | Modify | Three-part system prompt, updated user prompt, new `ProfileScanAIResponse` record |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs` | Modify | Score passthrough with clamp, drop `Confidence` from `AiScoringResult`, remove `Spam` gate |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs` | Modify | Drop `AiConfidence` parameter |
| `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs` | Modify | Drop `AiConfidence` parameter |
| `TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs` | Modify | Remove `AiConfidence` from both mapping directions |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` | Modify | Remove `AiConfidence` from `ScoringResult` construction and history insert |
| `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs` | Modify | Drop `AiConfidence` property |
| `TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor` | Modify | Remove confidence display |
| `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md` | Modify | Update AI response docs, remove confidence references, document new categories |
| `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs` | Modify | Replace AI-layer tests, update Layer 1 tests for record shape |
| New EF Core migration | Create | Drop `ai_confidence` column from `profile_scan_results` |

---

### Task 1: Update AI Response Record and Scoring Engine Core

This task changes the data contracts and scoring logic. Everything downstream breaks until the ripple is completed in Task 2.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs:141-149` (ProfileScanAIResponse record)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs:26-34` (AiScoringResult record)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs:234-264` (ParseAiResponse method)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs:64-101` (ScoreAsync — ScoringResult construction)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs:6-14`

- [ ] **Step 1: Update `ProfileScanAIResponse` record**

In `ProfileScanPrompts.cs`, replace the record at lines 144-149:

```csharp
internal record ProfileScanAIResponse(
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("signals_detected")] string[]? SignalsDetected,
    [property: JsonPropertyName("contains_nudity")] bool ContainsNudity);
```

Remove the `using System.Security;` import only if `SanitizeForPrompt` no longer needs it — it still does, so keep it.

- [ ] **Step 2: Update `AiScoringResult` internal record**

In `ProfileScoringEngine.cs`, replace lines 26-34:

```csharp
private record AiScoringResult(
    decimal Score,
    string? Reason,
    string[]? Signals,
    bool ContainsNudity = false)
{
    public static readonly AiScoringResult Empty = new(0.0m, null, null, false);
}
```

Note: `Confidence` parameter removed entirely.

- [ ] **Step 3: Update `ScoringResult` record**

In `ScoringResult.cs`, remove the `AiConfidence` parameter:

```csharp
public record ScoringResult(
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string[]? AiSignals,
    bool ContainsNudity = false);
```

- [ ] **Step 4: Rewrite `ParseAiResponse` method**

In `ProfileScoringEngine.cs`, replace lines 234-264. The entire 3-bucket mapping and `Spam` gate logic is replaced with a score passthrough:

```csharp
private AiScoringResult ParseAiResponse(string content, UserIdentity user)
{
    try
    {
        var response = JsonSerializer.Deserialize<ProfileScanAIResponse>(content, JsonOptions);
        if (response == null)
        {
            logger.LogWarning("Profile scan AI response deserialized to null for {User}", user.ToLogDebug());
            return AiScoringResult.Empty;
        }

        var score = Math.Clamp(response.Score, 0.0m, MaxScore);
        return new AiScoringResult(score, response.Reason, response.SignalsDetected, response.ContainsNudity);
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Failed to parse profile scan AI response for {User}: {Content}",
            user.ToLogDebug(), content[..Math.Min(content.Length, 200)]);
        return AiScoringResult.Empty;
    }
}
```

- [ ] **Step 5: Update `ScoringResult` construction in `ScoreAsync`**

In `ProfileScoringEngine.cs`, update the two places where `ScoringResult` is constructed to remove `AiConfidence`:

Line ~67 (rule-based ban path):
```csharp
return new ScoringResult(
    Score: Cap(ruleScore),
    Outcome: ProfileScanOutcome.Banned,
    RuleScore: ruleScore,
    AiScore: 0.0m,
    AiReason: "Rule-based detection triggered ban threshold",
    AiSignals: null,
    ContainsNudity: false);
```

Line ~88 (combined score path):
```csharp
return new ScoringResult(
    Score: totalScore,
    Outcome: outcome,
    RuleScore: ruleScore,
    AiScore: aiResult.Score,
    AiReason: aiResult.Reason,
    AiSignals: aiResult.Signals,
    ContainsNudity: aiResult.ContainsNudity);
```

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs \
       TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs \
       TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs
git commit -m "refactor: replace AI confidence mapping with direct 0-5 score passthrough"
```

---

### Task 2: Ripple AiConfidence Removal Through Data Layer and UI

This task propagates the `AiConfidence` removal to the data models, mappings, orchestrator, and UI.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs:17`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs:22,38`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs:358`
- Modify: `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs:41`
- Modify: `TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor:50-53`

- [ ] **Step 1: Update `ProfileScanResultRecord`**

In `ProfileScanResultRecord.cs`, remove the `AiConfidence` parameter. Read the file first to confirm exact current shape, then remove the `int? AiConfidence` line from the record.

- [ ] **Step 2: Update `ProfileScanResultDto`**

In `ProfileScanResultDto.cs`, remove the `AiConfidence` property:
```csharp
// Remove this line:
public int? AiConfidence { get; set; }
```

- [ ] **Step 3: Update `ProfileScanResultMappings`**

In `ProfileScanResultMappings.cs`, remove `AiConfidence` from both `ToModel()` (line ~22) and `ToDto()` (line ~38) extension methods. Read the file first to see exact mapping structure.

- [ ] **Step 4: Update `ProfileScanService`**

In `ProfileScanService.cs`, update the `ProfileScanResultRecord` construction at line ~358 to remove `AiConfidence: scoreResult.AiConfidence`.

- [ ] **Step 5: Update `ProfileScanHistoryDialog.razor`**

In the Razor file, remove lines 50-53 (the confidence display block):
```razor
@* Remove this block: *@
@if (result.AiConfidence.HasValue)
{
    <text> (confidence: @result.AiConfidence%)</text>
}
```

- [ ] **Step 6: Verify build compiles**

Run: `dotnet build --no-restore`
Expected: Build succeeds with no errors related to `AiConfidence`.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs \
       TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs \
       TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs \
       TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs \
       TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor
git commit -m "refactor: remove AiConfidence from data layer, mappings, and UI"
```

---

### Task 3: EF Core Migration — Drop ai_confidence Column

**Files:**
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (only if `AiConfidence` has Fluent API config)
- Create: New migration file

- [ ] **Step 1: Check if AppDbContext has Fluent API config for AiConfidence**

Search `AppDbContext.cs` for any `AiConfidence` or `ai_confidence` configuration in `OnModelCreating`. If found, remove it.

- [ ] **Step 2: Generate migration**

Run from repo root:
```bash
dotnet ef migrations add DropProfileScanResultsAiConfidence -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin
```

- [ ] **Step 3: Review generated migration**

Read the generated migration file. It should contain only:
```csharp
migrationBuilder.DropColumn(
    name: "ai_confidence",
    table: "profile_scan_results");
```

If EF Core generates anything beyond dropping the column (e.g., table recreation), manually fix the migration.

- [ ] **Step 4: Validate migration runs**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: Migration applies successfully, app exits cleanly.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Data/Migrations/
git commit -m "migration: drop ai_confidence column from profile_scan_results"
```

---

### Task 4: New Three-Part System Prompt

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs:10-75` (replace BuildSystemPrompt and add new methods)

- [ ] **Step 1: Replace `BuildSystemPrompt` with three-part architecture**

Replace the entire `BuildSystemPrompt()` method (lines 19-75) with:

```csharp
internal static string BuildSystemPrompt(string? customDetectionCriteria = null)
{
    var technical = GetTechnicalContract();
    var criteria = customDetectionCriteria ?? GetDefaultDetectionCriteria();
    var guardrails = GetBehavioralGuardrails();
    return $"{technical}\n\n{criteria}\n\n{guardrails}";
}
```

Note: The caller at `ProfileScoringEngine.cs:197` calls `ProfileScanPrompts.BuildSystemPrompt()` with no arguments. This still compiles because the new parameter has a default value of `null`. **Do not modify the call site.**

- [ ] **Step 2: Add `GetTechnicalContract` method**

```csharp
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
```

- [ ] **Step 3: Add `GetDefaultDetectionCriteria` method**

```csharp
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
```

- [ ] **Step 4: Add `GetBehavioralGuardrails` method**

```csharp
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
```

- [ ] **Step 5: Update `BuildUserPrompt` opening line**

In `BuildUserPrompt` method, change the first line of the return string (line ~113) from:
```
Analyze this Telegram user profile for spam/adult content indicators.
```
To:
```
Assess whether this Telegram user profile belongs to a genuine community member.
```

Also update the JSON instruction at the end of the user prompt (line ~136) from:
```
Respond with JSON: {"spam": true/false, "confidence": 0-100, "reason": "...", "signals_detected": [...], "contains_nudity": true/false}
```
To:
```
Respond with JSON: {"score": 0.0-5.0, "reason": "...", "signals_detected": [...], "contains_nudity": true/false}
```

- [ ] **Step 6: Verify build compiles**

Run: `dotnet build --no-restore`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs
git commit -m "feat: new three-part profile scan prompt with 6 detection categories"
```

---

### Task 5: Replace AI-Layer Tests

This task replaces the old confidence-tier tests with new score-passthrough tests and adds nudity flag tests.

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs`

- [ ] **Step 1: Update the test class docstring**

Update the summary comment at the top of the test class (lines 13-24) to reflect the new scoring model. Change the Layer 2 description from "confidence tiers (>=80 → 4.5, >=40 → 2.5, <40 → 0.0)" to "direct 0.0-5.0 score passthrough with clamp".

- [ ] **Step 2: Delete old AI-layer tests that test the 3-bucket mapping**

Remove these test methods entirely (they test behavior that no longer exists):
- `ScoreAsync_AiReturnsSpamFalse_AiScoreIsZeroAndClean` (the `Spam=false` gate is gone)
- `ScoreAsync_AiSpamTrueConfidence80_AiScoreIsFourPointFive`
- `ScoreAsync_AiSpamTrueConfidenceExactly80_AiScoreIsFourPointFive`
- `ScoreAsync_AiSpamTrueConfidence50_AiScoreIsTwoPointFive`
- `ScoreAsync_AiSpamTrueConfidenceExactly40_AiScoreIsTwoPointFive`
- `ScoreAsync_AiSpamTrueConfidence20_AiScoreIsZero`
- `ScoreAsync_AiSpamTrueConfidenceExactly39_AiScoreIsZero`

- [ ] **Step 3: Add new score passthrough tests**

Add a helper method for enabling AI and setting up text completion mocks:

```csharp
private void EnableAiWithResponse(string json)
{
    _chatService
        .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
        .Returns(true);
    _chatService
        .GetCompletionAsync(
            Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
        .Returns(AiResponse(json));
}
```

Then add the new tests:

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// Layer 2: AI score passthrough (new 0-5 direct scoring)
// ═══════════════════════════════════════════════════════════════════════════

[Test]
public async Task ScoreAsync_AiReturnsCleanScore_AiScoreIsZero()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 0.0, "reason": "genuine community member", "signals_detected": [], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    using (Assert.EnterMultipleScope())
    {
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
        Assert.That(result.AiReason, Is.EqualTo("genuine community member"));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
    }
}

[Test]
public async Task ScoreAsync_AiReturnsSuspiciousScore_OutcomeIsHeldForReview()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 2.8, "reason": "suggestive photo + name", "signals_detected": ["suggestive_photo", "suggestive_name"], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    using (Assert.EnterMultipleScope())
    {
        Assert.That(result.AiScore, Is.EqualTo(2.8m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
    }
}

[Test]
public async Task ScoreAsync_AiReturnsBanScore_OutcomeIsBanned()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 4.5, "reason": "commercial account with product photos", "signals_detected": ["commercial_name", "product_photo"], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    using (Assert.EnterMultipleScope())
    {
        Assert.That(result.AiScore, Is.EqualTo(4.5m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    }
}

[Test]
public async Task ScoreAsync_AiScoreExceedsFive_ClampedToMaxScore()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 7.5, "reason": "extreme risk", "signals_detected": [], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    Assert.That(result.AiScore, Is.EqualTo(5.0m));
}

[Test]
public async Task ScoreAsync_AiScoreNegative_ClampedToZero()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": -1.0, "reason": "invalid", "signals_detected": [], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    Assert.That(result.AiScore, Is.EqualTo(0.0m));
}

[Test]
public async Task ScoreAsync_RuleScorePlusAiScore_Additive()
{
    // Rule: stop word (+1.5) + AI score 3.0 = 4.5 → ban
    var profile = BuildProfile(bio: "guaranteed profits from my service");
    _stopWordsRepository
        .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
        .Returns(["guaranteed profits"]);
    EnableAiWithResponse("""{"score": 3.0, "reason": "scam promotion", "signals_detected": ["scam_language"], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    using (Assert.EnterMultipleScope())
    {
        Assert.That(result.RuleScore, Is.EqualTo(1.5m));
        Assert.That(result.AiScore, Is.EqualTo(3.0m));
        Assert.That(result.Score, Is.EqualTo(4.5m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    }
}
```

- [ ] **Step 4: Add nudity flag passthrough tests**

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// Layer 2: Nudity flag passthrough
// ═══════════════════════════════════════════════════════════════════════════

[Test]
public async Task ScoreAsync_AiReturnsNudityTrue_ContainsNudityIsTrue()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 4.8, "reason": "visible nudity in profile photo", "signals_detected": ["nudity"], "contains_nudity": true}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    Assert.That(result.ContainsNudity, Is.True);
}

[Test]
public async Task ScoreAsync_AiReturnsNudityFalse_ContainsNudityIsFalse()
{
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 3.5, "reason": "suggestive but not nude", "signals_detected": ["suggestive_photo"], "contains_nudity": false}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    Assert.That(result.ContainsNudity, Is.False);
}

[Test]
public async Task ScoreAsync_NudityFlagIndependentOfScore_LowScoreCanHaveNudity()
{
    // Edge case: nudity detected but AI gave low overall score (shouldn't happen in practice, but tests independence)
    var profile = BuildProfile(bio: "Some bio text");
    EnableAiWithResponse("""{"score": 1.5, "reason": "minimal risk but nudity present", "signals_detected": ["nudity"], "contains_nudity": true}""");

    var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

    using (Assert.EnterMultipleScope())
    {
        Assert.That(result.ContainsNudity, Is.True);
        Assert.That(result.AiScore, Is.EqualTo(1.5m));
    }
}
```

- [ ] **Step 5: Update existing tests that reference old JSON format or AiConfidence**

Update these tests to use the new JSON format (`score` instead of `spam`/`confidence`) and remove `AiConfidence` assertions:

- `ScoreAsync_AiFeatureUnavailable_ReturnsRuleScoreOnly` — remove `Assert.That(result.AiConfidence, Is.Null)`
- `ScoreAsync_AiReturnsNull_AiScoreIsZero` — remove `Assert.That(result.AiConfidence, Is.Null)`
- `ScoreAsync_AiReturnsMalformedJson_AiScoreIsZeroGracefully` — remove `Assert.That(result.AiConfidence, Is.Null)`
- `ScoreAsync_CleanProfile_ReturnsExpectedScoringResultFields` — remove `Assert.That(result.AiConfidence, Is.Null)`
- `ScoreAsync_NoImages_UsesTextCompletionNotVision` — update JSON from `{"spam": false, "confidence": 10, ...}` to `{"score": 0.0, ...}`
- `ScoreAsync_SingleImage_UsesSingleVisionCompletion` — update JSON
- `ScoreAsync_MultipleImages_UsesMultiImageVisionCompletion` — update JSON
- `ScoreAsync_TotalScoreAtNotifyThreshold_OutcomeIsHeldForReview` — update JSON from `{"spam": true, "confidence": 50, ...}` to `{"score": 2.5, ...}` and update score assertion
- `ScoreAsync_TotalScoreAtBanThreshold_OutcomeIsBanned` — update JSON from `{"spam": true, "confidence": 95, ...}` to `{"score": 4.5, ...}` and update score assertion
- `ScoreAsync_CombinedRuleAndAiScoreExceedsFive_CappedAtFive` — update JSON from `{"spam": true, "confidence": 95, ...}` to `{"score": 4.5, ...}`
- `ScoreAsync_CustomHighBanThreshold_ScoreBelowThresholdIsHeldForReview` — update JSON to `{"score": 4.5, ...}`
- `ScoreAsync_CustomLowNotifyThreshold_LowScoreTriggersHeldForReview` — update JSON from `{"spam": false, "confidence": 30, ...}` to `{"score": 0.0, ...}` (this test triggers via rule score only)
- `ScoreAsync_AiSpamWithSignals_SignalsPopulatedOnResult` — update JSON mock from `{"spam": true, "confidence": 85, ...}` to `{"score": 4.5, ...}` (signal assertions are unchanged, no score assertion exists in this test)
- `ScoreAsync_UrlMetadataPassedToAiPrompt_WhenScrapingReturnsData` — update JSON and score assertion
- `ScoreAsync_UrlScrapingFailure_ContinuesWithoutMetadata` — update JSON
- `ScoreAsync_NoUrlMetadata_PromptDoesNotContainUrlMetadataSection` — update JSON
- `ScoreAsync_AiReturnsEmptyJsonObject_AiScoreIsZero` — this one still works since `Score` defaults to `0` for `decimal`

- [ ] **Step 6: Run all tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --no-restore --verbosity normal`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs
git commit -m "test: replace confidence-tier tests with score passthrough and nudity flag tests"
```

---

### Task 6: Update Feature Documentation

**Files:**
- Modify: `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md`

- [ ] **Step 1: Read the full documentation file**

Read the entire file to understand structure and find all references to the old model.

- [ ] **Step 2: Update Layer 2 AI response documentation**

Replace the section describing the AI response (around lines 166-182) with the new format:

- Change bullet list from `spam`, `confidence`, `reason`, `signals_detected`, `contains_nudity` to `score` (0.0-5.0), `reason`, `signals_detected`, `contains_nudity`
- Replace the confidence-to-score mapping table with a note that the AI returns the score directly on the 0.0-5.0 scale
- Remove the "When `spam` is false, the AI score is 0.0 regardless of confidence" paragraph
- Add brief mention of the 6 detection categories

- [ ] **Step 3: Update scan history description**

In Step 7 (line ~104), update "confidence" reference to reflect the new data model.

- [ ] **Step 4: Verify build compiles** (documentation doesn't affect build, but verify content)

Read the file again to confirm all changes are consistent.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Docs/features/08-profile-scanning.md
git commit -m "docs: update profile scanning docs for new scoring model"
```

---

### Task 7: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build --no-restore`
Expected: Clean build, zero errors, zero warnings related to changed code.

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --no-restore --verbosity normal`
Expected: All tests pass.

- [ ] **Step 3: Run migrate-only to validate migration**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: Migration applies successfully.

- [ ] **Step 4: Verify no stale references**

Search for any remaining references to old field names:
```bash
grep -r "AiConfidence" --include="*.cs" --include="*.razor" | grep -v "/Migrations/" | grep -v "Designer.cs"
grep -r '"spam"' --include="*.cs" TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin.UnitTests/
grep -r '"confidence"' --include="*.cs" TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin.UnitTests/
```
Expected: No matches outside of migration files.

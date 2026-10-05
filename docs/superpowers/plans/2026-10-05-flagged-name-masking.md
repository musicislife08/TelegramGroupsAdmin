# Flagged Name Masking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a gate-admitted profile scan cannot read the profile, score the name alone and act on it like a scan; both scans judge whether the name is explicit or promotional and store it on the scan row; a flagged name is masked in bot chat posts only while the user is banned.

**Architecture:** `ProfileScanPrompts.NameFlagDefinitions` defines both name flags for the full scan and a new name-only scan (same system prompt, a user prompt that marks everything but the name unknown). `ProfileScoringEngine.ScoreNameOnlyAsync` scores the name against the name-only ban threshold; `ProfileScanService` runs it as a fallback inside gate-admitted scans that hit no session / unresolvable / full-profile fetch failure / timeout / `FLOOD_WAIT`, persists a `NameOnly` row and acts through the existing ban / alert path. The `user_identities` view exposes the verdict inputs (`latest_scan_explicit`, `latest_scan_promotional`, `is_banned`) and `UserIdentityMapping` applies "banned && explicit → Explicit; banned && promotional → Promotional; else Clean".

**Tech Stack:** .NET 10, C# 14, EF Core 10 + PostgreSQL 18 (raw-SQL views), Blazor Server + MudBlazor 9, NUnit, NSubstitute 6, bUnit, Testcontainers-backed integration tests on the canonical golden dataset.

**Spec:** `docs/superpowers/specs/2026-10-03-flagged-name-masking-design.md` (part 1: `docs/superpowers/specs/2026-10-03-user-identity-service-design.md`, plan `docs/superpowers/plans/2026-10-03-user-identity-service.md`).

## Global Constraints

- Branch: `feat/552-flagged-name-masking` (spec, plan and implementation together). PR to `develop` only. Conventional commits. Never commit to `develop`/`master`. Never use git worktrees. Never push from a task.
- Fixed wording (part 1, unchanged): `[name removed: explicit]` (`NameRedaction.Explicit`), `[name removed: spam]` (`NameRedaction.Spam`).
- Verdict rule, in `UserIdentityMapping` only: banned and explicit → `Explicit`; else banned and promotional → `Promotional`; else `Clean`. No scan row, bots and Telegram system accounts stay `Unscanned`.
- Prompt text is copied verbatim from the spec: the "What gets flagged" block is `ProfileScanPrompts.NameFlagDefinitions`; the name-only preamble is the spec's "Name-only scan" block. Do not reword either.
- Name-only triggers (inside a scan the gate admitted): no User API session, user can't be resolved, timeout, `FLOOD_WAIT`. No name-only scan on a rule short-circuit, for bots, when scanning is disabled, or when the AI feature is unavailable.
- `profile_scan_results`: `ai_promotional_display_text boolean not null default false`; `source smallint not null default 0` (`FullScan = 0`, `NameOnly = 1`).
- `ProfileScanConfig.NameOnlyBanThreshold`: decimal, default `4.5`, global with per-chat override like `BanThreshold` / `NotifyThreshold`, validated to be at least the notify threshold, absent key → default (no data migration).
- Settings label "Name-only ban threshold", caption: "A scan that could only read the name auto-bans at this score; below it, scores at or above the notify threshold go to review."
- A failed name-only call writes nothing and logs a warning; exception text goes to the log only, never into a chat.
- Identity rules: `.claude/rules/user-identity.md` (identities from `IUserIdentityService` / `user_identities` + `UserIdentityMapping`; tests use `UserIdentity.ForTest`).
- Integration tests use canonical golden data only (`.claude/rules/integration-test-data.md`): no SUT write as setup, no inserts; flag-edit unreferenced rows, pin anchors in `GoldenDatasetConstants`, add a recipe to `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` marked "(canonical edit 2026-10-03)", read edited preconditions back first.
- Test data and examples stay generic: never use real evaluation names (the spec's own prompt examples are fine).
- EF Core: change models / `AppDbContext` first, then `dotnet ef migrations add <Name> --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`. View SQL changes freeze the old SQL for older migrations.
- NSubstitute matcher lambdas use `x!.Prop`, never `?.`. When a substituted method has two optional `bool` parameters, a matcher call must pass a matcher for every argument (NSubstitute cannot place one `Arg.Any<bool>()` among two `bool`s).
- `TreatWarningsAsErrors` everywhere: 0 warnings.
- Verification per task: build + targeted tests for the changed code. Final task runs the unit, component and integration suites, and only targeted E2E filters (never the full E2E suite).

## Planning rulings (spec gaps resolved here)

1. "No User API session" means the service found no usable client inside a gate-admitted scan (`ProfileScanService` `client == null`). The gate's own `no_session` skip (no active session recorded at all) is unchanged, so a deployment with no session configured runs no name-only scans.
2. Only gate-admitted scans fall back (`IProfileScanService.ScanUserProfileAsync(..., nameOnlyFallback: true)` from `ProfileScanGate`). The bulk `ProfileRescanJob` and the manual UI rescan bypass the gate and do not fall back: a session flicker there must not replace a full-scan verdict with a name-only one or auto-ban on a name alone, and the job's session-loss abort reads the skip reason.
3. A `Users_GetFullUser` failure after the user resolved counts as "the user can't be resolved": it now carries the skip reason "Could not fetch the user's full profile." (it had none) and falls back.
4. "A later successful full scan replaces the name-only verdict" is implemented explicitly: when the latest scan row is `NameOnly`, both reuse paths (60 s freshness window and the unchanged-profile diff) are skipped. The name-only scan writes only `profile_scan_score` and `profile_scanned_at`; stored bio, photo and channel fields are left as they were.
5. The fallback is also skipped when the user has no `telegram_users` row (the scan row's FK parent), has no name at all, or is a bot (defence in depth beyond the gate).
6. A full scan whose AI call is unavailable or fails (rule-only score) does not fall back: the name-only scan needs the same AI.
7. The name-only user prompt ends with the same `Respond with JSON: …` line as the full prompt, and its profile block keeps the full prompt's sections (profile, personal channel, stories, images) with every non-name field `Unknown (could not be retrieved)`.
8. `NameFlagDefinitions` is appended after the remaining guardrails (URL metadata, nudity flag); the old "EXPLICIT DISPLAY-TEXT FLAG" section is removed.
9. Threshold validation lives in the settings form (inline error + save refused). Runtime outcome code does not clamp.
10. Enriched views gain `latest_scan_promotional` and `is_banned` for every identity slot that feeds `UserIdentityMapping` (message author; report suspected, target, exam user, profile user). The reply-to slot is not mapped to an identity and stays as is.
11. `JoinUserIdentitiesInEnrichedViews` replays the live `CreateViewSql` constants, so it switches to frozen V2 copies before those constants change.
12. The "Mask flagged names" caption said a flagged name is masked; it now says a banned user's flagged name, keeping the rest of the sentence.
13. Row 531 (@LisoBran) is flag-edited only; its clean AI reason and score stay (spec-chosen anchor). Row 526 (@splendorfraying) is flag-edited only; its outcome stays Clean (the test needs "not banned + promotional", which it has).
14. "Explicit beats promotional" at integration level uses read-only row 534 (explicit, promotional false, banned) and asserts the explicit label; precedence with both flags true is pinned by the `UserIdentityMapping` unit tests.
15. The admin-DM integration test needs `InternalsVisibleTo TelegramGroupsAdmin.IntegrationTests` on the web project (`AdminNotificationService` and `NotificationDmDispatcher` are internal).
16. `ProfileScanService.ScanTimeout` becomes an internal init property (default 45 s) so the timeout trigger test runs the real `Task.WhenAny` race in milliseconds.
17. The masked-username metric tag is `verdict` = `explicit` / `promotional`. No new scan metric for name-only scans; the explicit-name detection counter also counts name-only rows.

## Review Focus

1. A joiner with no stored row or no name at all reaching the fallback: no AI call, no FK failure, the skip result is returned — pinned by Task 5 `Fallback_UserWithoutNames_SkipsNameOnlyScan` and `Fallback_NoStoredRow_SkipsNameOnlyScan`.
2. A full scan right after a name-only scan on an empty, unchanged profile must rescore instead of reusing the name-only score (freshness window and profile diff) — pinned by Task 5 `LatestScanNameOnly_RecentlyScanned_DoesNotReuseCachedScore` and `LatestScanNameOnly_ProfileUnchanged_IsFullyRescored`.
3. The rescan job or manual rescan losing the session mid-batch must not write name-only verdicts and must keep the "No User API session" skip reason the job aborts on — pinned by Task 5 `NoFallbackRequested_NoSession_ReturnsSkipWithoutNameOnlyScan` and the Task 5 `ProfileRescanJobTests` assertion that the job passes `nameOnlyFallback: false`.
4. Names carrying markup (`</display_name>`, `&`, `<b>`) must be XML-escaped in the name-only prompt — pinned by Task 4 `BuildNameOnlyUserPrompt_EscapesMarkupInNames`.
5. A malformed or out-of-range AI reply on the name-only path: malformed → no verdict, nothing written; `7.0` → clamped to `5.0` → auto-ban — pinned by Task 4 `ScoreNameOnlyAsync_MalformedJson_ReturnsNullAndLogsWarning` and `ScoreNameOnlyAsync_ScoreAboveMax_IsClampedAndBanned`.

---

## File Structure

| File | Responsibility |
|---|---|
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs` (modify) | `NameFlagDefinitions`, response schema, `BuildNameOnlyUserPrompt`, `ProfileScanAIResponse.PromotionalDisplayText` |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs`, `IProfileScoringEngine.cs`, `ScoringResult.cs` (modify) | promotional flag; `ScoreNameOnlyAsync` |
| `TelegramGroupsAdmin.Core/Models/ProfileScanSource.cs` (create) | `FullScan` / `NameOnly` |
| `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs`, `AppDbContext.cs` (modify) + migration `AddNameOnlyScanColumns` | storage columns |
| `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs`, `Repositories/Mappings/ProfileScanResultMappings.cs`, `Repositories/IProfileScanResultsRepository.cs`, `ProfileScanResultsRepository.cs` (modify) | record fields, `GetLatestSourceAsync` |
| `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserRepository.cs`, `TelegramUserRepository.cs` (modify) | `UpdateProfileScanScoreAsync` |
| `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`, `TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs`, `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs` (modify) | `NameOnlyBanThreshold` |
| `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor` (modify) | threshold field, validation, caption wording |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs`, `ProfileScanResult.cs`, `IProfileScanService.cs`, `ProfileScanGate.cs` (modify) | fallback orchestration |
| `TelegramGroupsAdmin.Data/Models/UserIdentityView.cs`, `EnrichedMessageView.cs`, `EnrichedReportView.cs`, `Migrations/LegacyEnrichedViewSql.cs`, `Migrations/20261003220638_JoinUserIdentitiesInEnrichedViews.cs` (modify) + migration `AddNameVerdictInputsToUserIdentities` | verdict inputs in views |
| `TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs`, `EnrichedReportMappings.cs`, `TelegramGroupsAdmin.Telegram/Repositories/Mappings/EnrichedMessageMappings.cs`, `MessageMappings.cs`, `MessageHistoryRepository.cs` (modify) | verdict rule and its inputs |
| `TelegramGroupsAdmin.Testing.Golden/SQL/canonical/23_profile_scan_results.sql`, `GoldenDatasetConstants.cs`, `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (modify) | canonical anchors |
| `TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor` (modify) | source + both flags |
| `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs`, `Services/BanCelebrationService.cs` (modify) | verdict tag |
| `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md` (modify) | user docs |

---

### Task 0: Branch

- [ ] **Step 1: Confirm the branch**

```bash
git branch --show-current
```
Expected: `feat/552-flagged-name-masking`

---

### Task 1: Name flag prompt and the promotional flag through scoring

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs:25-34, 67-100, 234-259`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs`

**Interfaces:**
- Produces: `internal const string ProfileScanPrompts.NameFlagDefinitions`; `ProfileScanAIResponse.PromotionalDisplayText` (`bool`, default `false`, JSON `promotional_display_text`); `ScoringResult(..., bool ExplicitDisplayText = false, bool PromotionalDisplayText = false)`; `private AiScoringResult? TryParseAiResponse(string content, UserIdentity user)` in `ProfileScoringEngine` (null on unparseable JSON).

- [ ] **Step 1: Write the failing tests**

Append to `ProfileScoringEngineTests` (new region after "Layer 2: ExplicitDisplayText flag passthrough"):

```csharp
    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: name flags (prompt + PromotionalDisplayText passthrough)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_SystemPromptCarriesNameFlagDefinitionsAndSchema()
    {
        string? systemPrompt = null;
        string? userPrompt = null;
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Do<string>(s => systemPrompt = s), Arg.Do<string>(u => userPrompt = u),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 0.0, "reason": "ok", "signals_detected": [], "contains_nudity": false}"""));

        await _sut.ScoreAsync(BuildProfile(), [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(systemPrompt, Does.Contain(ProfileScanPrompts.NameFlagDefinitions));
            Assert.That(systemPrompt, Does.Not.Contain("EXPLICIT DISPLAY-TEXT FLAG"));
            Assert.That(systemPrompt, Does.Contain("\"promotional_display_text\": true/false"));
            Assert.That(userPrompt, Does.Contain("\"promotional_display_text\": true/false"));
        }
    }

    [Test]
    public async Task ScoreAsync_AiReturnsPromotionalDisplayTextTrue_PromotionalDisplayTextIsTrue()
    {
        EnableAiWithResponse(
            """{"score": 3.0, "reason": "name sells a service", "signals_detected": ["service_for_hire_name"], "contains_nudity": false, "explicit_display_text": false, "promotional_display_text": true}""");

        var result = await _sut.ScoreAsync(BuildProfile(), [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.PromotionalDisplayText, Is.True);
            Assert.That(result.ExplicitDisplayText, Is.False);
        }
    }

    [Test]
    public async Task ScoreAsync_AiOmitsPromotionalDisplayTextField_DefaultsToFalse()
    {
        EnableAiWithResponse(
            """{"score": 1.0, "reason": "fine name", "signals_detected": [], "contains_nudity": false, "explicit_display_text": false}""");

        var result = await _sut.ScoreAsync(BuildProfile(), [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.PromotionalDisplayText, Is.False);
    }

    [Test]
    public async Task ScoreAsync_RuleBasedFastPathBan_PromotionalDisplayTextIsFalse()
    {
        var result = await _sut.ScoreAsync(BuildProfile(isScam: true), [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.PromotionalDisplayText, Is.False);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScoringEngineTests"`
Expected: build error: `ProfileScanPrompts` does not contain `NameFlagDefinitions`; `ScoringResult` does not contain `PromotionalDisplayText`.

- [ ] **Step 3: Implement**

`ScoringResult.cs`, replace the record:

```csharp
/// <summary>Result from the full two-layer scoring pipeline, or from the name-only scan.</summary>
public record ScoringResult(
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string[]? AiSignals,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false,
    bool PromotionalDisplayText = false);
```

`ProfileScanPrompts.cs`:

1. `BuildSystemPrompt` appends the flag definitions:

```csharp
    internal static string BuildSystemPrompt(string? customDetectionCriteria = null)
    {
        var technical = GetTechnicalContract();
        var criteria = customDetectionCriteria ?? GetDefaultDetectionCriteria();
        var guardrails = GetBehavioralGuardrails();
        return $"{technical}\n\n{criteria}\n\n{guardrails}\n\n{NameFlagDefinitions}";
    }
```

2. In `GetTechnicalContract`, replace the JSON format line with:

```text
        {"score": 0.0-5.0, "reason": "clear explanation", "signals_detected": ["signal1", "signal2"], "contains_nudity": true/false, "explicit_display_text": true/false, "promotional_display_text": true/false}
```

3. In `GetBehavioralGuardrails`, delete everything from the `EXPLICIT DISPLAY-TEXT FLAG` banner (the `══…` line above it) through `members when this flag is false.`, so the method ends after `This flag triggers image censoring in admin review.`

4. Add a response-format constant (used by both user prompts) and the definitions constant, directly below `SanitizeForPrompt`:

```csharp
    /// <summary>The response line both user prompts end with.</summary>
    private const string ResponseFormat =
        """Respond with JSON: {"score": 0.0-5.0, "reason": "...", "signals_detected": [...], "contains_nudity": true/false, "explicit_display_text": true/false, "promotional_display_text": true/false}""";

    /// <summary>
    /// Definitions of both name flags, used verbatim by the full scan and the name-only scan.
    /// Settled against the profile-scan model; do not reword without re-evaluating.
    /// </summary>
    internal const string NameFlagDefinitions = """
        ══════════════════════════════════════
         NAME FLAGS (display name + username only)
        ══════════════════════════════════════

        These two flags judge ONLY the visible name: the display name (first +
        last name) and the @username. A name that says who or what the account
        is (a person, a nickname, a farm, a shop, a studio, a podcast, a
        project) is an identity and stays clean. A name that speaks to the
        reader (sells, offers a service, solicits, recruits, lures, or points
        somewhere else) gets flagged. Judge the display
        name and the username each on their own: either one alone can set a
        flag. Judge meaning in any language or script.

        "explicit_display_text" — true ONLY when the name text itself reads as
        explicit sexual content to an ordinary reader:
        - Sexual solicitation phrases ("looking for F buddy", "DM me horny")
        - Graphic sexual terminology or explicit slurs in the name
        - Sexual roleplay handles ("sub4daddy", "kinky_milf")
        Do NOT set it for suggestive but non-explicit names ("BeachBabe92",
        "lonely_girl", "Hot Kristina"); judge those as lures below. A single
        word that is also a surname, an ordinary word or obscure slang
        ("Dick", "Cox", "Wang", "Johnson") is not explicit on its own.

        "promotional_display_text" — true when the name pitches to the reader
        instead of naming someone: it sells, solicits, recruits, or offers a
        service for hire. A business, farm, homestead, shop, craft, studio,
        podcast or project used as a person's identity is NOT promotional by
        itself ("Maple Ridge Farm", "@oakhollowhomestead", "@NorthForgeKnives",
        "Pixel Studio", "@TheTrailPodcast"). Any one of these is enough:
        - Advertises a product, service, business, channel or group, including
          clickbait ("Crypto Signals VIP", "Best Web Design", "Free — Join Now 👉")
        - Describes a service for hire instead of naming anyone: a generic
          trade or role with no person or named thing behind it ("Expert
          Developer", "Pro Graphic Designer", "Digital Marketer"), or a name
          built from a common spam trade even without a call to action:
          e-commerce, SEO, marketing, growth, ads, web or app development,
          VoIP, call center, SIP, bulk SMS, crypto or trading signals, loans or
          funding, "supplier" or "provider" ("Ecom Expert Pro", "@cheap_seo_ads",
          "@callcenter_pro", "@voip_deals", "Bulk Supplier", "@lisa_capital_team")
        - Solicits contact, money, loans, jobs, trading or investing ("DM me
          for loans", "Forex mentor – message me", "Sara Crypto Signals"). A
          trading or finance word on its own is an interest, not an offer
          ("Mike_FX", "@btc_sam"); flag it only when the name offers something
          (signals, team, capital, mentor, invest, VIP, profits).
        - Makes health or miracle claims ("Natural cure for diabetes")
        - Sells drugs or other contraband ("Delivery 🍁 💊")
        - Presents itself as a role or an organization instead of a person:
          support, help desk, official or staff accounts ("Admin Support",
          "Help Desk", "Official Team", "<community name> Support"). You do
          not need to know who the real admins are; judge the role words in
          the name itself. "Official" next to a person's own name ("Official
          Mark Hayes", "@sara_official") is a vanity tag, not a role.
        - Is a lure: romance or suggestive bait, including a name that
          advertises sexiness or availability ("Lonely Anna 💋 text me",
          "Sweet girl waiting for you", "Hot Kristina", "naughty_jess22")
        - Points somewhere else: a link, domain, @handle, "see my bio", or an
          obfuscated variant ("site . com", "t me/xyz", "info in my profile")

        Read emoji for what they suggest in context. Emoji used as sexual slang
        (food or body-part innuendo, lips, hot or drooling faces) or to
        signal availability make a name a lure on their own.
        Emoji that stand for drugs, money, trading or urgency support other
        promotional signals. Ordinary decoration (hearts, flowers, smiles,
        animals, flags, sparkles) is not a flag.

        Styled Unicode letters (fullwidth, mathematical bold) and look-alike
        characters ("€" for "e", "0" for "o") strengthen other signals but are
        not a flag on their own.

        Leave both flags false for ordinary names: gamer tags, nicknames,
        emoji-only names, names in any script, initials, abbreviations with
        dots ("Mr.Bean", "Dr. Smith", "St.John"), a profession or hobby next to
        a name ("Lisa | Nurse", "Coach Tom", "jen_knits", "Tom paints"), a
        profession shown with a matching emoji ("Nurse Kim 💉", "Dr. Lee 🩺💊"),
        and a normal name with a heart, flower or smiling emoji. A hobby or job is
        only promotional when the name sells it ("Tom paints — commissions open").

        Both flags may be true at once.
        """;
```

5. In `BuildUserPrompt`, replace the last line of the returned template (`Respond with JSON: {...}`) with `{{ResponseFormat}}`.

6. Replace `ProfileScanAIResponse`:

```csharp
/// <summary>
/// Deserialization target for the AI profile scan response (full and name-only scans).
/// </summary>
internal record ProfileScanAIResponse(
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("signals_detected")] string[]? SignalsDetected,
    [property: JsonPropertyName("contains_nudity")] bool ContainsNudity,
    [property: JsonPropertyName("explicit_display_text")] bool ExplicitDisplayText = false,
    [property: JsonPropertyName("promotional_display_text")] bool PromotionalDisplayText = false);
```

`ProfileScoringEngine.cs`:

1. `AiScoringResult`:

```csharp
    private record AiScoringResult(
        decimal Score,
        string? Reason,
        string[]? Signals,
        bool ContainsNudity = false,
        bool ExplicitDisplayText = false,
        bool PromotionalDisplayText = false)
    {
        public static readonly AiScoringResult Empty = new(0.0m, null, null);
    }
```

2. The rule short-circuit `return new ScoringResult(...)` adds `PromotionalDisplayText: false` after `ExplicitDisplayText: false`. The final `return new ScoringResult(...)` adds `PromotionalDisplayText: aiResult.PromotionalDisplayText`.

3. In `RunAiScoringAsync`, replace `return ParseAiResponse(result.Content, profile.User);` with `return TryParseAiResponse(result.Content, profile.User) ?? AiScoringResult.Empty;`.

4. Replace `ParseAiResponse` with:

```csharp
    /// <summary>Parses the AI reply; null when it is not the expected JSON (logged as a warning).</summary>
    private AiScoringResult? TryParseAiResponse(string content, UserIdentity user)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ProfileScanAIResponse>(content, JsonOptions);
            if (response == null)
            {
                logger.LogWarning("Profile scan AI response deserialized to null for {User}", user.ToLogDebug());
                return null;
            }

            return new AiScoringResult(
                Score: Math.Clamp(response.Score, 0.0m, MaxScore),
                Reason: response.Reason,
                Signals: response.SignalsDetected,
                ContainsNudity: response.ContainsNudity,
                ExplicitDisplayText: response.ExplicitDisplayText,
                PromotionalDisplayText: response.PromotionalDisplayText);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse profile scan AI response for {User}: {Content}",
                user.ToLogDebug(), content[..Math.Min(content.Length, 200)]);
            return null;
        }
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScoringEngineTests"`
Expected: PASS (all, including the existing explicit-flag and malformed-JSON tests).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs
git commit -m "feat(profile-scan): judge names as explicit or promotional with one flag definition"
```

---

### Task 2: Store the promotional flag and the scan source

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/ProfileScanSource.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs`, `TelegramGroupsAdmin.Data/AppDbContext.cs:904-906`
- Create (generated): `TelegramGroupsAdmin.Data/Migrations/<ts>_AddNameOnlyScanColumns.cs` (+ Designer, snapshot)
- Modify: `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs`, `Repositories/Mappings/ProfileScanResultMappings.cs`, `Repositories/IProfileScanResultsRepository.cs`, `Repositories/ProfileScanResultsRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserRepository.cs`, `TelegramUserRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanResult.cs`, `ProfileScanService.cs:458-512`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs`

**Interfaces:**
- Consumes: `ScoringResult.PromotionalDisplayText` (Task 1).
- Produces: `enum ProfileScanSource { FullScan = 0, NameOnly = 1 }` (`TelegramGroupsAdmin.Core.Models`); `ProfileScanResultDto.AiPromotionalDisplayText` (`bool`), `ProfileScanResultDto.Source` (`short`); `ProfileScanResultRecord(..., bool ExplicitDisplayText = false, bool PromotionalDisplayText = false, ProfileScanSource Source = ProfileScanSource.FullScan)`; `ProfileScanResult(..., string? SkipReason = null, bool PromotionalDisplayText = false, ProfileScanSource Source = ProfileScanSource.FullScan)`; `Task<ProfileScanSource?> IProfileScanResultsRepository.GetLatestSourceAsync(long userId, CancellationToken cancellationToken)`; `Task ITelegramUserRepository.UpdateProfileScanScoreAsync(long telegramUserId, decimal score, CancellationToken cancellationToken = default)`; private `ProfileScanService.PersistScanResultAsync(long userId, ScoringResult scoreResult, ProfileScanSource source, IServiceProvider sp, CancellationToken ct)` and `ActOnOutcomeAsync(UserIdentity user, ChatIdentity? triggeringChat, ProfileScanResult result, IServiceProvider sp, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

In `ProfileScanResultsRepositoryTests`, replace `InsertAsync_PersistsExplicitDisplayText` with a round-trip of all three new-or-flag fields, and add the latest-source tests and a user-repository score test (the SUT write is the assertion subject in each):

```csharp
    [TestCase(true, false, ProfileScanSource.FullScan)]
    [TestCase(false, true, ProfileScanSource.NameOnly)]
    [TestCase(false, false, ProfileScanSource.FullScan)]
    public async Task InsertAsync_PersistsNameFlagsAndSource(bool explicitDisplayText, bool promotionalDisplayText, ProfileScanSource source)
    {
        await AssertUnscannedAsync();
        var record = new ProfileScanResultRecord(
            Id: 0,
            UserId: UnscannedUserId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.7m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.7m,
            AiReason: "test reason",
            AiSignals: "test_signal",
            ExplicitDisplayText: explicitDisplayText,
            PromotionalDisplayText: promotionalDisplayText,
            Source: source);

        var insertedId = await _repository!.InsertAsync(record, CancellationToken.None);
        var history = await _repository.GetByUserIdAsync(UnscannedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(history[0].Id, Is.EqualTo(insertedId));
            Assert.That(history[0].ExplicitDisplayText, Is.EqualTo(explicitDisplayText));
            Assert.That(history[0].PromotionalDisplayText, Is.EqualTo(promotionalDisplayText));
            Assert.That(history[0].Source, Is.EqualTo(source));
        }
    }

    [Test]
    public async Task GetByUserIdAsync_CanonicalRows_DefaultToFullScanAndNotPromotional()
    {
        // Rows written before the columns existed read back with the column defaults.
        var history = await _repository!.GetByUserIdAsync(CanonicalFlaggedUserId, CancellationToken.None);

        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history.Select(h => (h.Source, h.PromotionalDisplayText)),
            Is.All.EqualTo((ProfileScanSource.FullScan, false)));
    }

    [Test]
    public async Task GetLatestSourceAsync_CanonicalFullScanUser_ReturnsFullScan()
    {
        var source = await _repository!.GetLatestSourceAsync(CanonicalFlaggedUserId, CancellationToken.None);

        Assert.That(source, Is.EqualTo(ProfileScanSource.FullScan));
    }

    [Test]
    public async Task GetLatestSourceAsync_UnscannedUser_ReturnsNull()
    {
        await AssertUnscannedAsync();

        var source = await _repository!.GetLatestSourceAsync(UnscannedUserId, CancellationToken.None);

        Assert.That(source, Is.Null);
    }
```

Also cover the new user-repository write in the same fixture: in `SetUp`, add `services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();` next to the scan-results repository, then add:

```csharp
    [Test]
    public async Task UpdateProfileScanScoreAsync_SetsScoreAndAdvancesScannedAt_LeavesProfileFields()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UnscannedUserId);
        Assert.That(before.ProfileScannedAt, Is.Null, "anchor has never been scanned");
        var users = _scope!.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var start = DateTimeOffset.UtcNow;

        await users.UpdateProfileScanScoreAsync(UnscannedUserId, 3.2m);

        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UnscannedUserId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.ProfileScanScore, Is.EqualTo(3.2m));
            Assert.That(after.ProfileScannedAt, Is.GreaterThanOrEqualTo(start.AddSeconds(-1)));
            Assert.That(after.Bio, Is.EqualTo(before.Bio));
            Assert.That(after.ProfilePhotoId, Is.EqualTo(before.ProfilePhotoId));
            Assert.That(after.PersonalChannelId, Is.EqualTo(before.PersonalChannelId));
        }
    }
```

(`Microsoft.EntityFrameworkCore` and `TelegramGroupsAdmin.Telegram.Repositories` are already imported; the write is the assertion subject, on the canonical never-scanned anchor.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ProfileScanResultsRepositoryTests"`
Expected: build error: `ProfileScanSource`, `PromotionalDisplayText`, `GetLatestSourceAsync`, `UpdateProfileScanScoreAsync` do not exist.

- [ ] **Step 3: Implement the model and storage**

`TelegramGroupsAdmin.Core/Models/ProfileScanSource.cs`:

```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>Which scan produced a profile scan row. Stored as smallint.</summary>
public enum ProfileScanSource
{
    /// <summary>The whole profile was read and scored.</summary>
    FullScan = 0,
    /// <summary>The profile could not be read; only the name and username were scored.</summary>
    NameOnly = 1
}
```

`ProfileScanResultDto.cs`, after `AiExplicitDisplayText`:

```csharp
    /// <summary>AI flagged the visible display name or @username as promotional (sells, solicits, lures, points elsewhere)</summary>
    [Column("ai_promotional_display_text")]
    public bool AiPromotionalDisplayText { get; set; }

    /// <summary>0=FullScan, 1=NameOnly (Core.Models.ProfileScanSource)</summary>
    [Column("source")]
    public short Source { get; set; }
```

`AppDbContext.cs`, after the `AiExplicitDisplayText` default:

```csharp
        modelBuilder.Entity<ProfileScanResultDto>()
            .Property(p => p.AiPromotionalDisplayText)
            .HasDefaultValue(false);
        modelBuilder.Entity<ProfileScanResultDto>()
            .Property(p => p.Source)
            .HasDefaultValue((short)0);
```

Run: `dotnet ef migrations add AddNameOnlyScanColumns --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`
Expected: a migration whose `Up` adds `ai_promotional_display_text` (`boolean`, not null, default `false`) and `source` (`smallint`, not null, default `(short)0`) to `profile_scan_results`, and whose `Down` drops both. Nothing else.

`ProfileScanResultRecord.cs`:

```csharp
public record ProfileScanResultRecord(
    long Id,
    long UserId,
    DateTimeOffset ScannedAt,
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string? AiSignals,
    bool ExplicitDisplayText = false,
    bool PromotionalDisplayText = false,
    ProfileScanSource Source = ProfileScanSource.FullScan);
```

`ProfileScanResultMappings.cs`: `ToModel()` adds `PromotionalDisplayText: data.AiPromotionalDisplayText, Source: (ProfileScanSource)data.Source`; `ToDto()` adds `AiPromotionalDisplayText = ui.PromotionalDisplayText, Source = (short)ui.Source`.

`IProfileScanResultsRepository.cs`, add:

```csharp
    /// <summary>
    /// Source of the user's latest scan (scanned_at DESC, id DESC), or null when the user has none.
    /// </summary>
    Task<ProfileScanSource?> GetLatestSourceAsync(long userId, CancellationToken cancellationToken);
```

`ProfileScanResultsRepository.cs`, add (and `using TelegramGroupsAdmin.Core.Models;`):

```csharp
    public async Task<ProfileScanSource?> GetLatestSourceAsync(long userId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => (short?)r.Source)
            .FirstOrDefaultAsync(cancellationToken);
        return source is { } value ? (ProfileScanSource)value : null;
    }
```

`ITelegramUserRepository.cs`, after `UpdateProfileScannedAtAsync`:

```csharp
    /// <summary>
    /// Record a name-only scan: set ProfileScanScore and bump ProfileScannedAt + UpdatedAt.
    /// Stored bio, channel, story and photo fields are left as they are.
    /// </summary>
    Task UpdateProfileScanScoreAsync(long telegramUserId, decimal score, CancellationToken cancellationToken = default);
```

`TelegramUserRepository.cs`, after `UpdateProfileScannedAtAsync`:

```csharp
    public async Task UpdateProfileScanScoreAsync(long telegramUserId, decimal score, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await context.TelegramUsers
            .Where(u => u.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileScanScore, score)
                .SetProperty(u => u.ProfileScannedAt, now)
                .SetProperty(u => u.UpdatedAt, now), cancellationToken);
    }
```

`ProfileScanResult.cs`: append two parameters after `SkipReason`:

```csharp
    string? SkipReason = null,
    bool PromotionalDisplayText = false,
    ProfileScanSource Source = ProfileScanSource.FullScan);
```

`ProfileScanService.cs`: replace the block from `// Persist scan result history` (line 458) through the end of Step 8 (line 512, the closing brace of the `if (scoreResult.Outcome is ...)` block) with:

```csharp
        await PersistScanResultAsync(user.Id, scoreResult, ProfileScanSource.FullScan, sp, ct);

        var result = new ProfileScanResult(
            TelegramUserId: user.Id,
            Bio: bio,
            PersonalChannelId: personalChannelId,
            PersonalChannelTitle: channelTitle,
            PersonalChannelAbout: channelAbout,
            HasPinnedStories: hasPinnedStories,
            PinnedStoryCaptions: pinnedStoryCaptions,
            IsScam: isScam,
            IsFake: isFake,
            IsVerified: isVerified,
            Score: scoreResult.Score,
            Outcome: scoreResult.Outcome,
            AiReason: scoreResult.AiReason,
            AiSignalsDetected: scoreResult.AiSignals,
            ContainsNudity: scoreResult.ContainsNudity,
            ExplicitDisplayText: scoreResult.ExplicitDisplayText,
            PromotionalDisplayText: scoreResult.PromotionalDisplayText);

        // ── Step 8: Take moderation action ──
        await ActOnOutcomeAsync(user, triggeringChat, result, sp, ct);

        return result;
```

and add these private methods after `ScanUserProfileCoreAsync`:

```csharp
    /// <summary>Writes the scan history row (both scan sources) and counts explicit names.</summary>
    private async Task PersistScanResultAsync(
        long userId, ScoringResult scoreResult, ProfileScanSource source, IServiceProvider sp, CancellationToken ct)
    {
        await sp.GetRequiredService<IProfileScanResultsRepository>().InsertAsync(new ProfileScanResultRecord(
            Id: 0,
            UserId: userId,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: scoreResult.Score,
            Outcome: scoreResult.Outcome,
            RuleScore: scoreResult.RuleScore,
            AiScore: scoreResult.AiScore,
            AiReason: scoreResult.AiReason,
            AiSignals: scoreResult.AiSignals is { Length: > 0 } ? string.Join(", ", scoreResult.AiSignals) : null,
            ExplicitDisplayText: scoreResult.ExplicitDisplayText,
            PromotionalDisplayText: scoreResult.PromotionalDisplayText,
            Source: source), cancellationToken: ct);

        if (scoreResult.ExplicitDisplayText)
            pipelineMetrics.RecordExplicitUsernameDetection(OutcomeToTag(scoreResult.Outcome));
    }

    /// <summary>
    /// Bans or raises a review alert for a scored result. Re-resolves the identity first: the
    /// caller's copy predates this scan's row, so bot-written text would otherwise miss its verdict.
    /// </summary>
    private async Task ActOnOutcomeAsync(
        UserIdentity user, ChatIdentity? triggeringChat, ProfileScanResult result, IServiceProvider sp, CancellationToken ct)
    {
        if (result.Outcome is not (ProfileScanOutcome.Banned or ProfileScanOutcome.HeldForReview))
            return;

        var scannedUser = await sp.GetRequiredService<IUserIdentityService>().ResolveAsync(user.Id, ct);
        if (result.Outcome == ProfileScanOutcome.Banned)
            await HandleBanAsync(scannedUser, triggeringChat, result, sp, ct);
        else
            await CreateProfileScanAlertAsync(scannedUser, triggeringChat, result, sp, ct);
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ProfileScanResultsRepositoryTests" && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanServiceSingleFlightTests"`
Expected: build succeeds with 0 warnings; all tests PASS (the single-flight tests prove the Step 7/8 refactor kept the re-resolve-then-act order).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/ProfileScanSource.cs TelegramGroupsAdmin.Data TelegramGroupsAdmin.Telegram TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs
git commit -m "feat(profile-scan): store the promotional name flag and the scan source"
```

---

### Task 3: Name-only ban threshold setting

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs`
- Modify: `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs:148-171`
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:157-172, 555-560, 599-601`
- Test: `TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs`
- Test: `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`

**Interfaces:**
- Produces: `ProfileScanConfig.DefaultNameOnlyBanThreshold` (`const decimal` = `4.5m`), `ProfileScanConfig.NameOnlyBanThreshold` (`decimal`), `ProfileScanConfigData.NameOnlyBanThreshold` (`decimal`, default `4.5m`).

- [ ] **Step 1: Write the failing tests**

Append to `WelcomeConfigMappingsTests`:

```csharp
    [Test]
    public void ProfileScanConfig_NameOnlyBanThreshold_RoundTripsAndDefaultsTo45()
    {
        var model = new ProfileScanConfig { NameOnlyBanThreshold = 3.5m };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.ToData().NameOnlyBanThreshold, Is.EqualTo(3.5m));
            Assert.That(new ProfileScanConfigData { NameOnlyBanThreshold = 3.5m }.ToModel().NameOnlyBanThreshold, Is.EqualTo(3.5m));
            Assert.That(new ProfileScanConfig().NameOnlyBanThreshold, Is.EqualTo(4.5m));
            Assert.That(ProfileScanConfig.DefaultNameOnlyBanThreshold, Is.EqualTo(4.5m));
            Assert.That(new ProfileScanConfigData().NameOnlyBanThreshold, Is.EqualTo(4.5m));
        }
    }

    [Test]
    public void ProfileScanConfigData_StoredJsonWithoutNameOnlyKey_DeserializesToDefault()
    {
        // Rows saved before the setting existed carry no key: absent means the default, no data migration.
        const string json = """{"enabled":true,"banThreshold":4.0,"notifyThreshold":2.0}""";

        var data = System.Text.Json.JsonSerializer.Deserialize<ProfileScanConfigData>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.That(data.NameOnlyBanThreshold, Is.EqualTo(4.5m));
    }
```

Append to `WelcomeSystemConfigTests` (inside a new `#region Name-only Threshold Tests`):

```csharp
    private static WelcomeConfig ProfileScanConfigWith(decimal notify, decimal nameOnlyBan) => new()
    {
        Enabled = true,
        MainWelcomeMessage = "Welcome {username}!",
        JoinSecurity = new JoinSecurityConfig
        {
            ProfileScan = new ProfileScanConfig
            {
                Enabled = true,
                NotifyThreshold = notify,
                NameOnlyBanThreshold = nameOnlyBan
            }
        }
    };

    [Test]
    public void NameOnlyBanThreshold_RendersWithLabelAndCaption()
    {
        ConfigService.GetWelcomeAsync(Arg.Any<long>()).Returns(ProfileScanConfigWith(2.0m, 4.5m));

        var cut = Render<WelcomeSystemConfig>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Name-only ban threshold"));
            Assert.That(cut.Markup, Does.Contain(
                "A scan that could only read the name auto-bans at this score; below it, scores at or above the notify threshold go to review."));
        }, TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Save_NameOnlyThresholdBelowNotify_IsRefused()
    {
        ConfigService.GetWelcomeAsync(Arg.Any<long>()).Returns(ProfileScanConfigWith(3.0m, 2.5m));
        this.AddTestWebUser();
        var cut = Render<WelcomeSystemConfig>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Save Configuration")), TimeSpan.FromSeconds(2));

        cut.FindAll("button").First(b => b.TextContent.Contains("Save Configuration")).Click();

        cut.WaitForAssertion(() =>
            Assert.That(cut.Markup, Does.Contain("Must be at least the notify threshold")), TimeSpan.FromSeconds(2));
        await ConfigService.DidNotReceiveWithAnyArgs().SaveWelcomeAsync(default!, default!, default!, default);
    }

    [Test]
    public async Task Save_PassesNameOnlyThresholdThrough()
    {
        ConfigService.GetWelcomeAsync(Arg.Any<long>()).Returns(ProfileScanConfigWith(2.0m, 3.5m));
        this.AddTestWebUser();
        var cut = Render<WelcomeSystemConfig>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Save Configuration")), TimeSpan.FromSeconds(2));

        cut.FindAll("button").First(b => b.TextContent.Contains("Save Configuration")).Click();

        await ConfigService.Received(1).SaveWelcomeAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Is<WelcomeConfig>(c => c!.JoinSecurity.ProfileScan.NameOnlyBanThreshold == 3.5m),
            Arg.Any<Actor>(),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests"`
Expected: build error: `NameOnlyBanThreshold` / `DefaultNameOnlyBanThreshold` do not exist.

- [ ] **Step 3: Implement**

`ProfileScanConfig.cs`, below `DefaultNotifyThreshold`:

```csharp
    public const decimal DefaultNameOnlyBanThreshold = 4.5m;
```

and below `NotifyThreshold`:

```csharp
    /// <summary>
    /// Score threshold for automatic ban when a scan could only read the name (0.0-5.0).
    /// A name alone is weaker evidence than a whole profile, so it needs more certainty.
    /// Must be at least <see cref="NotifyThreshold"/>.
    /// </summary>
    public decimal NameOnlyBanThreshold { get; set; } = DefaultNameOnlyBanThreshold;
```

`ProfileScanConfigData.cs`, after `NotifyThreshold`:

```csharp
    public decimal NameOnlyBanThreshold { get; set; } = 4.5m;
```

`WelcomeConfigMappings.cs`: in both `ToModel()` and `ToData()` add `NameOnlyBanThreshold = data.NameOnlyBanThreshold,` / `NameOnlyBanThreshold = model.NameOnlyBanThreshold,` after the `NotifyThreshold` line.

`WelcomeSystemConfig.razor`: after the `Admin Notify Threshold` `MudItem` (line 172) insert:

```razor
                                    <MudItem xs="12">
                                        <MudNumericField @bind-Value="_config.JoinSecurity.ProfileScan.NameOnlyBanThreshold"
                                                         Label="Name-only ban threshold"
                                                         Variant="Variant.Outlined"
                                                         Min="1.0m" Max="5.0m" Step="0.5m"
                                                         HelperText="A scan that could only read the name auto-bans at this score; below it, scores at or above the notify threshold go to review."
                                                         Error="@NameOnlyThresholdInvalid"
                                                         ErrorText="Must be at least the notify threshold"
                                                         Disabled="!_config.JoinSecurity.ProfileScan.Enabled" />
                                    </MudItem>
```

In `@code`, below `MaskFlaggedNamesHelpId`:

```csharp
    private bool NameOnlyThresholdInvalid =>
        _config is not null
        && _config.JoinSecurity.ProfileScan.NameOnlyBanThreshold < _config.JoinSecurity.ProfileScan.NotifyThreshold;
```

In `SaveConfig`, after `if (_config == null || !_isValid) return;`:

```csharp
        if (NameOnlyThresholdInvalid)
        {
            Snackbar.Add("Name-only ban threshold must be at least the notify threshold.", Severity.Error);
            return;
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests" && dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~WelcomeSystemConfigTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Configuration TelegramGroupsAdmin.Data/Models/Configs TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -m "feat(settings): add the name-only ban threshold to profile scan settings"
```

---

### Task 4: Name-only scoring in the engine

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScoringEngine.cs`, `ProfileScoringEngine.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineNameOnlyTests.cs` (create)

**Interfaces:**
- Consumes: `NameFlagDefinitions`, `ResponseFormat`, `TryParseAiResponse`, `ScoringResult.PromotionalDisplayText` (Task 1).
- Produces: `internal const string ProfileScanPrompts.UnknownField` = `"Unknown (could not be retrieved)"`; `internal static string ProfileScanPrompts.BuildNameOnlyUserPrompt(string? firstName, string? lastName, string? username)`; `Task<ScoringResult?> IProfileScoringEngine.ScoreNameOnlyAsync(UserIdentity user, decimal nameOnlyBanThreshold, decimal notifyThreshold, CancellationToken cancellationToken)` — null when the AI feature is unavailable, the call returns null or throws, or the reply is unparseable (each logged as a warning).

- [ ] **Step 1: Write the failing tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineNameOnlyTests.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The name-only scan: the full scan's system prompt with only the name filled in, scored
/// against the name-only ban threshold. The AI is the only fake (IChatService).
/// </summary>
[TestFixture]
public class ProfileScoringEngineNameOnlyTests
{
    private const decimal NameOnlyBan = 4.5m;
    private const decimal Notify = 2.0m;
    private static readonly UserIdentity User = UserIdentity.ForTest(12345L, "Sam", "Rivera", "sam_rivera");

    private IChatService _chat = null!;
    private CapturingLogger<ProfileScoringEngine> _logs = null!;
    private ProfileScoringEngine _sut = null!;
    private string? _systemPrompt;
    private string? _userPrompt;

    [SetUp]
    public void SetUp()
    {
        _chat = Substitute.For<IChatService>();
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        _logs = new CapturingLogger<ProfileScoringEngine>();
        _sut = new ProfileScoringEngine(
            Substitute.For<IUrlPreFilterService>(),
            Substitute.For<IUrlContentScrapingService>(),
            Substitute.For<IStopWordsRepository>(),
            _chat,
            _logs);
        _systemPrompt = null;
        _userPrompt = null;
    }

    private void AiReplies(string json) =>
        _chat.GetCompletionAsync(
                AIFeatureType.ProfileScan, Arg.Do<string>(s => _systemPrompt = s), Arg.Do<string>(u => _userPrompt = u),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult { Content = json });

    private static string Reply(decimal score, bool promotional = false, bool explicitName = false) =>
        $$"""{"score": {{score.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "reason": "name judged", "signals_detected": ["name_signal"], "contains_nudity": false, "explicit_display_text": {{(explicitName ? "true" : "false")}}, "promotional_display_text": {{(promotional ? "true" : "false")}}}""";

    [TestCase(1.9, ProfileScanOutcome.Clean)]
    [TestCase(2.0, ProfileScanOutcome.HeldForReview)]
    [TestCase(4.2, ProfileScanOutcome.HeldForReview)] // above the regular ban threshold (4.0), below the name-only one
    [TestCase(4.5, ProfileScanOutcome.Banned)]
    public async Task ScoreNameOnlyAsync_OutcomeUsesNameOnlyBanThreshold(decimal score, ProfileScanOutcome expected)
    {
        AiReplies(Reply(score));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Outcome, Is.EqualTo(expected));
            Assert.That(result.Score, Is.EqualTo(score));
            Assert.That(result.AiScore, Is.EqualTo(score));
            Assert.That(result.RuleScore, Is.Zero);
        }
    }

    [Test]
    public async Task ScoreNameOnlyAsync_UsesTheFullScanSystemPromptAndANameOnlyUserPrompt()
    {
        AiReplies(Reply(1.0m));

        await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_systemPrompt, Is.EqualTo(ProfileScanPrompts.BuildSystemPrompt()));
            Assert.That(_userPrompt, Is.EqualTo(ProfileScanPrompts.BuildNameOnlyUserPrompt("Sam", "Rivera", "sam_rivera")));
        }
        await _chat.Received(1).GetCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<ChatCompletionOptions?>(o => o!.JsonMode), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreNameOnlyAsync_CarriesBothNameFlags()
    {
        AiReplies(Reply(3.0m, promotional: true, explicitName: true));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.PromotionalDisplayText, Is.True);
            Assert.That(result.ExplicitDisplayText, Is.True);
            Assert.That(result.ContainsNudity, Is.False);
            Assert.That(result.AiReason, Is.EqualTo("name judged"));
            Assert.That(result.AiSignals, Is.EqualTo(new[] { "name_signal" }));
        }
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiFeatureUnavailable_ReturnsNullWithoutCallingAi()
    {
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        await _chat.DidNotReceiveWithAnyArgs().GetCompletionAsync(default, default!, default!, default, default);
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiCallThrows_ReturnsNullAndLogsWarning()
    {
        _chat.GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider exploded"));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_AiReturnsNull_ReturnsNullAndLogsWarning()
    {
        _chat.GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns((ChatCompletionResult?)null);

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_MalformedJson_ReturnsNullAndLogsWarning()
    {
        AiReplies("not json at all");

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(e => e.Level == LogLevel.Warning));
    }

    [Test]
    public async Task ScoreNameOnlyAsync_ScoreAboveMax_IsClampedAndBanned()
    {
        AiReplies(Reply(7.0m));

        var result = await _sut.ScoreNameOnlyAsync(User, NameOnlyBan, Notify, CancellationToken.None);

        Assert.That(result!.Score, Is.EqualTo(5.0m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    }

    [Test]
    public void BuildNameOnlyUserPrompt_MarksEverythingButTheNameUnknown()
    {
        var prompt = ProfileScanPrompts.BuildNameOnlyUserPrompt("Sam", "Rivera", "sam_rivera");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.StartWith("Only the name could be retrieved for this account."));
            Assert.That(prompt, Does.Contain("are UNKNOWN, not empty"));
            Assert.That(prompt, Does.Contain("<display_name>Sam Rivera</display_name>"));
            Assert.That(prompt, Does.Contain("<username>sam_rivera</username>"));
            Assert.That(prompt, Does.Contain("<bio>Unknown (could not be retrieved)</bio>"));
            Assert.That(prompt, Does.Contain("<title>Unknown (could not be retrieved)</title>"));
            Assert.That(prompt, Does.Contain("<description>Unknown (could not be retrieved)</description>"));
            Assert.That(prompt, Does.Contain("<story_count>Unknown (could not be retrieved)</story_count>"));
            Assert.That(prompt, Does.Contain("<image_count>Unknown (could not be retrieved)</image_count>"));
            Assert.That(prompt, Does.Contain("\"promotional_display_text\": true/false"));
        }
    }

    [Test]
    public void BuildNameOnlyUserPrompt_EscapesMarkupInNames()
    {
        var prompt = ProfileScanPrompts.BuildNameOnlyUserPrompt("</display_name><b>Sam", "&Co", "x<y");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("<display_name>&lt;/display_name&gt;&lt;b&gt;Sam &amp;Co</display_name>"));
            Assert.That(prompt, Does.Contain("<username>x&lt;y</username>"));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScoringEngineNameOnlyTests"`
Expected: build error: `ScoreNameOnlyAsync` and `BuildNameOnlyUserPrompt` do not exist.

- [ ] **Step 3: Implement**

`ProfileScanPrompts.cs`, below `BuildUserPrompt`:

```csharp
    /// <summary>Value of every profile field a name-only scan could not read.</summary>
    internal const string UnknownField = "Unknown (could not be retrieved)";

    /// <summary>
    /// User prompt for the name-only scan: the full scan's profile block with only the name and
    /// username filled in. Used with <see cref="BuildSystemPrompt"/> so a name-only score means the
    /// same as a full one, made on less evidence.
    /// </summary>
    internal static string BuildNameOnlyUserPrompt(string? firstName, string? lastName, string? username) =>
        $$"""
        Only the name could be retrieved for this account. The bio, photos,
        personal channel and stories are UNKNOWN, not empty: do not treat their
        absence as a clean empty profile, and do not treat it as suspicious.
        Score on what the name and username show.

        <profile>
          <display_name>{{SanitizeForPrompt(firstName)}} {{SanitizeForPrompt(lastName)}}</display_name>
          <username>{{SanitizeForPrompt(username)}}</username>
          <bio>{{UnknownField}}</bio>
        </profile>

        <personal_channel>
          <title>{{UnknownField}}</title>
          <description>{{UnknownField}}</description>
        </personal_channel>

        <stories>
          <story_count>{{UnknownField}}</story_count>
        </stories>

        <images>
          <image_count>{{UnknownField}}</image_count>
          <image_labels>{{UnknownField}}</image_labels>
        </images>

        {{ResponseFormat}}
        """;
```

`IProfileScoringEngine.cs`, add:

```csharp
    /// <summary>
    /// Name-only scan: scores the user's display name and username with the full scan's system
    /// prompt when the profile could not be read. Outcome: below <paramref name="notifyThreshold"/>
    /// clean, below <paramref name="nameOnlyBanThreshold"/> held for review, otherwise banned.
    /// </summary>
    /// <returns>The result, or null when the AI feature is unavailable or the call fails (logged as a warning).</returns>
    Task<ScoringResult?> ScoreNameOnlyAsync(
        UserIdentity user,
        decimal nameOnlyBanThreshold,
        decimal notifyThreshold,
        CancellationToken cancellationToken);
```

(add `using TelegramGroupsAdmin.Core.Models;`)

`ProfileScoringEngine.cs`, after `ScoreAsync`:

```csharp
    public async Task<ScoringResult?> ScoreNameOnlyAsync(
        UserIdentity user,
        decimal nameOnlyBanThreshold,
        decimal notifyThreshold,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await chatService.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, cancellationToken))
            {
                logger.LogWarning("ProfileScan AI feature not configured — name-only scan skipped for {User}", user.ToLogDebug());
                return null;
            }

            var result = await chatService.GetCompletionAsync(
                AIFeatureType.ProfileScan,
                ProfileScanPrompts.BuildSystemPrompt(),
                ProfileScanPrompts.BuildNameOnlyUserPrompt(user.FirstName, user.LastName, user.Username),
                new ChatCompletionOptions { JsonMode = true },
                cancellationToken);
            if (result == null)
            {
                logger.LogWarning("Name-only profile scan AI call returned null for {User}", user.ToLogDebug());
                return null;
            }

            var ai = TryParseAiResponse(result.Content, user);
            if (ai == null)
                return null;

            var outcome = ai.Score >= nameOnlyBanThreshold
                ? ProfileScanOutcome.Banned
                : ai.Score >= notifyThreshold
                    ? ProfileScanOutcome.HeldForReview
                    : ProfileScanOutcome.Clean;

            logger.LogInformation(
                "Name-only profile scan for {User}: score={Score}, outcome={Outcome}, explicit={Explicit}, promotional={Promotional}",
                user.ToLogInfo(), ai.Score, outcome, ai.ExplicitDisplayText, ai.PromotionalDisplayText);

            return new ScoringResult(
                Score: ai.Score,
                Outcome: outcome,
                RuleScore: 0.0m,
                AiScore: ai.Score,
                AiReason: ai.Reason,
                AiSignals: ai.Signals,
                ContainsNudity: false,
                ExplicitDisplayText: ai.ExplicitDisplayText,
                PromotionalDisplayText: ai.PromotionalDisplayText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail open: the exception text stays in the log, nothing is written or posted.
            logger.LogWarning(ex, "Name-only profile scan failed for {User}", user.ToLogDebug());
            return null;
        }
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScoringEngine"`
Expected: PASS (both engine test classes).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineNameOnlyTests.cs
git commit -m "feat(profile-scan): score a name alone against the name-only ban threshold"
```

---
### Task 5: Name-only fallback inside gate-admitted scans

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScanService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` (fields `:43-59`, `ScanUserProfileAsync`, `RunAndRemoveAsync`, `ScanOnceAsync`, `ScanUserProfileCoreAsync:250-256`, new helpers)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanGate.cs:105`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanServiceNameOnlyTests.cs` (create)
- Test (update matchers): `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanGateTests.cs:51, 319-329, 336`, `TelegramGroupsAdmin.UnitTests/BackgroundJobs/Jobs/ProfileRescanJobTests.cs:32, 58-59`, `TelegramGroupsAdmin.IntegrationTests/Telegram/MessageProcessingFirstMessageScanTests.cs:133, 245-248, 277-280`

**Interfaces:**
- Consumes: `IProfileScoringEngine.ScoreNameOnlyAsync` (Task 4); `ProfileScanConfig.NameOnlyBanThreshold` / `DefaultNameOnlyBanThreshold` (Task 3); `PersistScanResultAsync`, `ActOnOutcomeAsync`, `ProfileScanResult.Source` / `PromotionalDisplayText`, `IProfileScanResultsRepository.GetLatestSourceAsync`, `ITelegramUserRepository.UpdateProfileScanScoreAsync` (Task 2).
- Produces: `Task<ProfileScanResult> IProfileScanService.ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool forceRescan = false, bool nameOnlyFallback = false)`; `internal TimeSpan ProfileScanService.ScanTimeout { get; init; }` (default 45 s). A name-only result has `Source == ProfileScanSource.NameOnly` and `SkipReason == null`; a skipped scan keeps its skip reason.

- [ ] **Step 1: Write the failing tests**

`TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanServiceNameOnlyTests.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TL;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The name-only fallback of ProfileScanService: when a gate-admitted scan cannot read the profile
/// (no session, unresolvable, full profile not fetched, timeout, FLOOD_WAIT) the name is scored
/// alone, stored as a NameOnly row and acted on like a scan. The real service runs; Telegram, the
/// scorer and the repositories are faked.
/// </summary>
[TestFixture]
public class ProfileScanServiceNameOnlyTests
{
    private const long UserId = 7;
    private const long ChatId = -100;
    private static readonly ChatIdentity Chat = ChatIdentity.FromId(ChatId);
    private static readonly UserIdentity Named = UserIdentity.ForTest(UserId, "Sam", "Rivera", "sam_rivera");

#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private ITelegramUserRepository _users = null!;
    private IUserIdentityService _identities = null!;
    private IProfileScoringEngine _scoring = null!;
    private IProfileScanResultsRepository _results = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private IReportsRepository _reports = null!;
    private IAdminNotificationService _notifications = null!;
    private CapturingLogger<ProfileScanService> _logs = null!;
    private ServiceProvider _provider = null!;
    private ProfileScanService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = Substitute.For<ITelegramSessionManager>();
        _users = Substitute.For<ITelegramUserRepository>();
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow());
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(Named);
        _scoring = Substitute.For<IProfileScoringEngine>();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(0m, ProfileScanOutcome.Clean, 0m, 0m, null, null));
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(1.0m, ProfileScanOutcome.Clean));
        _results = Substitute.For<IProfileScanResultsRepository>();
        _config = Substitute.For<IConfigService>();
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });
        _reports = Substitute.For<IReportsRepository>();
        _notifications = Substitute.For<IAdminNotificationService>();
        _logs = new CapturingLogger<ProfileScanService>();
        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<IUsernameHistoryRepository>())
            .AddSingleton(_moderation)
            .AddSingleton(_reports)
            .AddSingleton(_notifications)
            .AddSingleton(_users)
            .AddSingleton(_identities)
            .AddSingleton(_scoring)
            .AddSingleton(_results)
            .AddSingleton(_config)
            .BuildServiceProvider();
        _sut = NewSut(TimeSpan.FromSeconds(45));
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private ProfileScanService NewSut(TimeSpan timeout) => new(
        _sessions,
        _provider.GetRequiredService<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        Substitute.For<IImageProcessor>(),
        _logs)
    { ScanTimeout = timeout };

    private Task<ProfileScanResult> GateScanAsync(UserIdentity? user = null) =>
        _sut.ScanUserProfileAsync(user ?? Named, Chat, CancellationToken.None, nameOnlyFallback: true);

    private static ScoringResult NameOnlyScore(decimal score, ProfileScanOutcome outcome) =>
        new(score, outcome, 0m, score, "name judged", ["name_signal"], PromotionalDisplayText: true);

    private static TelegramUser StoredRow(
        bool isBot = false, DateTimeOffset? scannedAt = null, decimal? score = null, long? personalChannelId = null) =>
        new(UserId, "sam_rivera", "Sam", "Rivera", null, null, null,
            IsBot: isBot, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            ProfileScannedAt: scannedAt, ProfileScanScore: score, PersonalChannelId: personalChannelId);

    private void NoSession() =>
        _sessions.GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

    private IWTelegramApiClient Client()
    {
        var client = Substitute.For<IWTelegramApiClient>();
        _sessions.GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>()).Returns(client);
        return client;
    }

    // Resolves through the username strategy, then returns a full user with an empty profile.
    private IWTelegramApiClient ResolvingClient()
    {
        var live = new TL.User { id = UserId, access_hash = 1, first_name = "Sam", last_name = "Rivera", username = "sam_rivera" };
        var client = Client();
        client.Contacts_ResolveUsername("sam_rivera").Returns(new Contacts_ResolvedPeer
        {
            peer = new PeerUser { user_id = UserId },
            users = new Dictionary<long, TL.User> { [UserId] = live },
            chats = new Dictionary<long, ChatBase>()
        });
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).Returns(new Users_UserFull
        {
            full_user = new UserFull(),
            users = new Dictionary<long, TL.User> { [UserId] = live },
            chats = new Dictionary<long, ChatBase>()
        });
        return client;
    }

    private async Task AssertNameOnlyScanRanAsync(ProfileScanResult result)
    {
        await _scoring.Received(1).ScoreNameOnlyAsync(
            Arg.Is<UserIdentity>(u => u!.Id == UserId && u.FirstName == "Sam" && u.Username == "sam_rivera"),
            4.5m, 2.0m, Arg.Any<CancellationToken>());
        await _results.Received(1).InsertAsync(
            Arg.Is<ProfileScanResultRecord>(r => r!.UserId == UserId && r.Source == ProfileScanSource.NameOnly
                && r.Score == 1.0m && r.Outcome == ProfileScanOutcome.Clean && r.RuleScore == 0m && r.AiScore == 1.0m
                && r.PromotionalDisplayText && !r.ExplicitDisplayText && r.AiSignals == "name_signal"),
            Arg.Any<CancellationToken>());
        await _users.Received(1).UpdateProfileScanScoreAsync(UserId, 1.0m, Arg.Any<CancellationToken>());
        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProfileScanSource.NameOnly));
            Assert.That(result.SkipReason, Is.Null);
            Assert.That(result.Score, Is.EqualTo(1.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
            Assert.That(result.PromotionalDisplayText, Is.True);
        });
    }

    // ── Triggers ──

    [Test]
    public async Task NoSession_RunsNameOnlyScan()
    {
        NoSession();

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
        await _users.DidNotReceiveWithAnyArgs().UpdateProfileScanDataAsync(
            default, default, default, default, default, default, default, default, default, default, default, default, default, default, default);
    }

    [Test]
    public async Task UserNotResolvable_RunsNameOnlyScan()
    {
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("USERNAME_NOT_OCCUPIED"));
        client.Contacts_Search(Arg.Any<string>(), Arg.Any<int>()).ThrowsAsync(new InvalidOperationException("SEARCH_FAILED"));

        var result = await GateScanAsync();

        await _users.Received(1).ExcludeFromProfileScanAsync(UserId, Arg.Any<CancellationToken>());
        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task FullProfileNotFetched_RunsNameOnlyScan()
    {
        var client = ResolvingClient();
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).ThrowsAsync(new InvalidOperationException("USER_ID_INVALID"));

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task Timeout_RunsNameOnlyScan()
    {
        _sut = NewSut(TimeSpan.FromMilliseconds(100));
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>())
            .Returns(new TaskCompletionSource<Contacts_ResolvedPeer>().Task); // a hung DC connection

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task FloodWait_RunsNameOnlyScan()
    {
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>())
            .ThrowsAsync(new TelegramFloodWaitException(30, DateTimeOffset.UtcNow.AddSeconds(30)));

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    // ── No trigger ──

    [Test]
    public async Task RuleShortCircuit_NoNameOnlyScan()
    {
        ResolvingClient();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(5.0m, ProfileScanOutcome.Banned, 5.0m, 0m,
                "Rule-based detection triggered ban threshold", null));

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.Source, Is.EqualTo(ProfileScanSource.FullScan));
        await _results.Received(1).InsertAsync(
            Arg.Is<ProfileScanResultRecord>(r => r!.Source == ProfileScanSource.FullScan), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Bot_NoNameOnlyScan()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow(isBot: true));
        NoSession();

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Does.Contain("No User API session"));
    }

    [Test]
    public async Task NoFallbackRequested_NoSession_ReturnsSkipWithoutNameOnlyScan()
    {
        // The bulk rescan job and the manual rescan call the service directly without the fallback;
        // the job aborts its batch on this skip reason.
        NoSession();

        var result = await _sut.ScanUserProfileAsync(Named, Chat, CancellationToken.None);

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        await _results.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        Assert.That(result.SkipReason, Does.Contain("No User API session"));
    }

    [Test]
    public async Task Fallback_UserWithoutNames_SkipsNameOnlyScan()
    {
        var idOnly = UserIdentity.ForTest(UserId);
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(idOnly);
        NoSession();

        var result = await GateScanAsync(idOnly);

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Is.Not.Null);
    }

    [Test]
    public async Task Fallback_NoStoredRow_SkipsNameOnlyScan()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        NoSession();

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Is.Not.Null);
    }

    // ── Failure: nothing written ──

    [Test]
    public async Task NameOnlyScanReturnsNoVerdict_WritesNothingAndLogsWarning()
    {
        _scoring.ScoreNameOnlyAsync(default!, default, default, default).ReturnsForAnyArgs((ScoringResult?)null);
        NoSession();

        var result = await GateScanAsync();

        await _results.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await _users.DidNotReceiveWithAnyArgs().UpdateProfileScanScoreAsync(default, default, default);
        await _moderation.DidNotReceiveWithAnyArgs().BanUserAsync(default!, default);
        await _reports.DidNotReceiveWithAnyArgs().InsertProfileScanAlertAsync(default!, default);
        Assert.That(result.SkipReason, Does.Contain("No User API session"));
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("Name-only")));
    }

    // ── Outcome goes through the existing moderation path ──

    [Test]
    public async Task NameOnlyHeldForReview_RaisesProfileScanAlert()
    {
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(3.0m, ProfileScanOutcome.HeldForReview));
        NoSession();

        await GateScanAsync();

        await _reports.Received(1).InsertProfileScanAlertAsync(
            Arg.Is<ProfileScanAlertRecord>(a => a!.User == Named && a.Score == 3.0m && a.Outcome == ProfileScanOutcome.HeldForReview),
            Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendProfileScanAlertAsync(
            Chat, Named, 3.0m, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _moderation.DidNotReceiveWithAnyArgs().BanUserAsync(default!, default);
    }

    [Test]
    public async Task NameOnlyBanned_BansWithIdentityResolvedAfterTheScan()
    {
        var resolvedAfter = UserIdentity.ForTest(UserId, "Sam", "Rivera", "sam_rivera", NameVerdict.Clean);
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(resolvedAfter);
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(4.8m, ProfileScanOutcome.Banned));
        NoSession();

        await GateScanAsync();

        Received.InOrder(() =>
        {
            _results.InsertAsync(Arg.Any<ProfileScanResultRecord>(), Arg.Any<CancellationToken>());
            _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>());
            _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());
        });
        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.User == resolvedAfter && i.Chat == Chat && i.Executor == Actor.ProfileScan),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PerChatOverride_OfNameOnlyThreshold_IsHonoured()
    {
        _config.GetEffectiveWelcomeAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(new WelcomeConfig
            {
                JoinSecurity = new JoinSecurityConfig
                {
                    ProfileScan = new ProfileScanConfig { Enabled = true, NotifyThreshold = 1.5m, NameOnlyBanThreshold = 3.0m }
                }
            }));
        NoSession();

        await GateScanAsync();

        await _scoring.Received(1).ScoreNameOnlyAsync(Arg.Any<UserIdentity>(), 3.0m, 1.5m, Arg.Any<CancellationToken>());
    }

    // ── A name-only verdict is never reused by a later scan ──

    [Test]
    public async Task LatestScanNameOnly_RecentlyScanned_DoesNotReuseCachedScore()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow, score: 4.2m));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.NameOnly);
        NoSession();

        await GateScanAsync();

        await _sessions.Received(1).GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LatestScanFullScan_RecentlyScanned_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow, score: 1.0m));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.FullScan);

        await GateScanAsync();

        await _sessions.DidNotReceiveWithAnyArgs().GetClientForChatAsync(default, default);
    }

    [Test]
    public async Task LatestScanNameOnly_ProfileUnchanged_IsFullyRescored()
    {
        // An empty profile reads back exactly as stored, so the diff alone would reuse the name-only score.
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow.AddMinutes(-5), score: 1.0m, personalChannelId: 0));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.NameOnly);
        ResolvingClient();

        await GateScanAsync();

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
```

`ProfileScanGateTests.cs`:
- line 51 and line 336 setups become `.ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<bool>())`.
- `ForceRescan_IsForwardedToScan` asserts `Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Is(true), Arg.Is(true)`.
- Add:

```csharp
    [Test]
    public async Task AdmittedScan_RequestsNameOnlyFallback()
    {
        SetUser(CreateUser(profileScannedAt: null));

        await ScanAsync(ProfileScanTrigger.Join);

        await _profileScanService.Received(1).ScanUserProfileAsync(
            Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Is(false), Arg.Is(true));
    }
```

(`ProfileScanDisabled_AllTriggersSkip` already pins "scanning disabled → no scan, so no name-only scan"; `FirstMessage_UntrustedNeverScannedBot_Skips` pins bots.)

`ProfileRescanJobTests.cs`: the line-32 setup gains a fifth matcher `Arg.Any<bool>()`; lines 58-59 become `ScanUserProfileAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Is(false))` (and `eight`), which pins that the job never asks for the fallback.

`MessageProcessingFirstMessageScanTests.cs`: the line-133 setup and both assertions (lines 245-248, 277-280) append `Arg.Any<bool>(), Arg.Any<bool>()` so they still match the gate's call (otherwise the `DidNotReceive` assertion passes vacuously).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanServiceNameOnlyTests"`
Expected: build error: `nameOnlyFallback` / `ScanTimeout` do not exist on `ProfileScanService`.

- [ ] **Step 3: Implement**

`IProfileScanService.cs`:

```csharp
    /// <summary>
    /// Scan a user's profile and take appropriate action (ban, report, or pass).
    /// </summary>
    /// <param name="user">Identity of the Telegram user to scan.</param>
    /// <param name="triggeringChat">Chat that triggered the scan (for reports). Null for background scans.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forceRescan">Skip both cached-score reuses (60s freshness window and unchanged-profile diff), e.g. after a rename.</param>
    /// <param name="nameOnlyFallback">When the profile cannot be read (no session, unresolvable, timeout, FLOOD_WAIT), score the
    /// name alone and act on it. Set by the eligibility gate only; admin-initiated rescans leave it off.</param>
    /// <returns>Scan result with extracted data, score, and outcome.</returns>
    Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct,
        bool forceRescan = false, bool nameOnlyFallback = false);
```

`ProfileScanGate.cs` line 105:

```csharp
        return await profileScanService.ScanUserProfileAsync(user, chat, ct, forceRescan, nameOnlyFallback: true);
```

`ProfileScanService.cs`:

1. Replace the static `ScanTimeout` field with:

```csharp
    /// <summary>
    /// Maximum time for the core scan (Telegram API calls, file downloads, AI scoring).
    /// Prevents hung DC connections from blocking the welcome flow indefinitely.
    /// </summary>
    internal TimeSpan ScanTimeout { get; init; } = TimeSpan.FromSeconds(45);
```

2. Put the fallback flag in the single-flight key (a fallback run must never be shared with a caller that did not ask for one, or vice versa) — add to the key comment "and so is the name-only fallback":

```csharp
    private readonly ConcurrentDictionary<(long UserId, bool ForceRescan, bool NameOnlyFallback), Lazy<Task<ProfileScanResult>>> _inFlight = new();

    public async Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        CancellationToken ct,
        bool forceRescan = false,
        bool nameOnlyFallback = false)
    {
        var key = (user.Id, forceRescan, nameOnlyFallback);
        Lazy<Task<ProfileScanResult>> candidate = null!;
        candidate = new Lazy<Task<ProfileScanResult>>(
            () => RunAndRemoveAsync(key, candidate, user, triggeringChat, forceRescan, nameOnlyFallback));
        var lazy = _inFlight.GetOrAdd(key, candidate);
        return await lazy.Value.WaitAsync(ct);
    }

    private async Task<ProfileScanResult> RunAndRemoveAsync(
        (long UserId, bool ForceRescan, bool NameOnlyFallback) key,
        Lazy<Task<ProfileScanResult>> self,
        UserIdentity user,
        ChatIdentity? triggeringChat,
        bool forceRescan,
        bool nameOnlyFallback)
    {
        try
        {
            return await ScanOnceAsync(user, triggeringChat, forceRescan, nameOnlyFallback, CancellationToken.None);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<(long, bool, bool), Lazy<Task<ProfileScanResult>>>(key, self));
        }
    }
```

(Keep the existing explanatory comments above `ScanUserProfileAsync`'s body and in the `finally`.)

3. Replace `ScanOnceAsync` with:

```csharp
    private async Task<ProfileScanResult> ScanOnceAsync(
        UserIdentity user,
        ChatIdentity? triggeringChat,
        bool forceRescan,
        bool nameOnlyFallback,
        CancellationToken ct)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var scanSource = triggeringChat is not null ? "welcome" : "rescan";

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();

        // ── Multi-chat dedup: skip if recently scanned ──
        var existingUser = await userRepo.GetByTelegramIdAsync(user.Id, ct);

        // Enrich identity if caller only provided a bare ID (e.g., rescan job)
        if (user.FirstName is null && user.LastName is null && user.Username is null)
            user = await sp.GetRequiredService<IUserIdentityService>().ResolveAsync(user.Id, ct);

        // Both reuse paths (this freshness window and the unchanged-profile check) are skipped on a
        // forced rescan, when a rename was recorded after the last scan, and when the last scan only
        // read the name. Joins and admin refresh record a rename without scanning, so the stored names
        // already match the live ones and only username_history still shows the change. A name-only
        // verdict is never reused: the next scan that can read the profile replaces it.
        var skipReuse = forceRescan
            || await RenamedSinceLastScanAsync(existingUser, sp, ct)
            || await LatestScanWasNameOnlyAsync(existingUser, sp, ct);

        if (!skipReuse && existingUser?.ProfileScannedAt is { } lastScan
            && DateTimeOffset.UtcNow - lastScan < ScanFreshnessWindow
            && existingUser.ProfileScanScore.HasValue)
        {
            logger.LogDebug("Profile scan for {User}: recently scanned ({LastScan}), reusing cached score {Score}",
                user.ToLogDebug(), lastScan, existingUser.ProfileScanScore);

            pipelineMetrics.RecordProfileScanSkipped("dedup");
            var cachedOutcome = await DetermineOutcomeAsync(existingUser.ProfileScanScore.Value, triggeringChat, sp, ct);
            return new ProfileScanResult(
                TelegramUserId: user.Id,
                Bio: existingUser.Bio,
                PersonalChannelId: existingUser.PersonalChannelId,
                PersonalChannelTitle: existingUser.PersonalChannelTitle,
                PersonalChannelAbout: existingUser.PersonalChannelAbout,
                HasPinnedStories: existingUser.HasPinnedStories,
                PinnedStoryCaptions: existingUser.PinnedStoryCaptions,
                IsScam: existingUser.IsScam,
                IsFake: existingUser.IsFake,
                IsVerified: existingUser.IsVerified,
                Score: existingUser.ProfileScanScore.Value,
                Outcome: cachedOutcome,
                AiReason: null,
                AiSignalsDetected: null,
                ExplicitDisplayText: false);
        }

        // ── Get User API client (prefer one with access to the triggering chat) ──
        var client = triggeringChat is not null
            ? await sessionManager.GetClientForChatAsync(triggeringChat.Id, ct)
            : await sessionManager.GetAnyClientAsync(ct);
        if (client == null)
        {
            logger.LogWarning("No User API client available for profile scan of {User}", user.ToLogDebug());
            pipelineMetrics.RecordProfileScanSkipped("no_session");
            return await FallBackToNameOnlyAsync(
                EmptyResult(user.Id, "No User API session available. Connect a session in Settings."),
                user, existingUser, triggeringChat, nameOnlyFallback, sp, ct);
        }

        // Top-level guard: WTelegram API calls don't accept CancellationToken, so a hung DC
        // connection (e.g., file download from DC -4) blocks indefinitely. Task.WhenAny races
        // the scan against a timeout — if the timeout wins, we abandon the scan and return
        // gracefully so the welcome flow continues.
        //
        // The scan task gets its own scope (via ScanWithOwnedScopeAsync) so that if the timeout
        // fires and this method returns, the abandoned task's scoped services stay alive until
        // the task completes or faults — preventing ObjectDisposedException on DbContexts.
        ProfileScanResult result;
        try
        {
            var scanTask = ScanWithOwnedScopeAsync(client, user, existingUser, triggeringChat, skipReuse, ct);

            var completedTask = await Task.WhenAny(scanTask, Task.Delay(ScanTimeout, CancellationToken.None));

            if (completedTask != scanTask)
            {
                logger.LogWarning(
                    "Profile scan timed out after {Timeout}s for {User} (WTelegram call hung, likely DC connection issue)",
                    ScanTimeout.TotalSeconds, user.ToLogDebug());

                // Observe the abandoned task to prevent UnobservedTaskException and log failures
                _ = scanTask.ContinueWith(
                    t => logger.LogDebug(t.Exception?.GetBaseException(),
                        "Abandoned profile scan for {UserId} faulted after timeout", user.Id),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

                pipelineMetrics.RecordProfileScanTimeout();
                result = EmptyResult(user.Id, $"Scan timed out after {ScanTimeout.TotalSeconds}s");
            }
            else
            {
                result = await scanTask;
                pipelineMetrics.RecordProfileScan(
                    OutcomeToTag(result.Outcome), scanSource,
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }
        catch (TelegramFloodWaitException ex)
        {
            logger.LogWarning("Rate limited during scan of {User} — {Message}, skipping (not excluding)",
                user.ToLogDebug(), ex.Message);
            result = EmptyResult(user.Id, ex.Message);
        }

        // A skip reason here means the profile could not be read (user not resolvable, full profile
        // not fetched, timeout, FLOOD_WAIT). A scan that read the profile never carries one.
        return result.SkipReason is null
            ? result
            : await FallBackToNameOnlyAsync(result, user, existingUser, triggeringChat, nameOnlyFallback, sp, ct);
    }
```

4. In `ScanUserProfileCoreAsync`, the `Users_GetFullUser` catch returns a skip reason:

```csharp
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get full user info for {User}", user.ToLogDebug());
            return EmptyResult(user.Id, "Could not fetch the user's full profile.");
        }
```

5. Add after `ActOnOutcomeAsync`:

```csharp
    /// <summary>
    /// The full scan could not read the profile. For a scan the gate admitted, score the name alone
    /// so the user is still filtered, store it as a NameOnly row and act on it like a scan.
    /// Otherwise, or when there is no name, no stored user, a bot, or no verdict, return the skip.
    /// </summary>
    private async Task<ProfileScanResult> FallBackToNameOnlyAsync(
        ProfileScanResult skipped,
        UserIdentity user,
        Models.TelegramUser? existingUser,
        ChatIdentity? triggeringChat,
        bool nameOnlyFallback,
        IServiceProvider sp,
        CancellationToken ct)
    {
        if (!nameOnlyFallback
            || existingUser is null
            || existingUser.IsBot
            || (user.FirstName is null && user.LastName is null && user.Username is null))
        {
            return skipped;
        }

        var configService = sp.GetRequiredService<IConfigService>();
        var profileScanConfig = (await configService.GetEffectiveWelcomeAsync(triggeringChat?.Id ?? 0, ct))?.JoinSecurity?.ProfileScan;
        var nameOnlyBanThreshold = profileScanConfig?.NameOnlyBanThreshold ?? ProfileScanConfig.DefaultNameOnlyBanThreshold;
        var notifyThreshold = profileScanConfig?.NotifyThreshold ?? ProfileScanConfig.DefaultNotifyThreshold;

        var scoreResult = await sp.GetRequiredService<IProfileScoringEngine>()
            .ScoreNameOnlyAsync(user, nameOnlyBanThreshold, notifyThreshold, ct);
        if (scoreResult is null)
        {
            logger.LogWarning("Name-only profile scan for {User} produced no verdict after: {SkipReason}. Nothing recorded",
                user.ToLogDebug(), skipped.SkipReason);
            return skipped;
        }

        await sp.GetRequiredService<ITelegramUserRepository>().UpdateProfileScanScoreAsync(user.Id, scoreResult.Score, ct);
        await PersistScanResultAsync(user.Id, scoreResult, ProfileScanSource.NameOnly, sp, ct);

        var result = EmptyResult(user.Id) with
        {
            Score = scoreResult.Score,
            Outcome = scoreResult.Outcome,
            AiReason = scoreResult.AiReason,
            AiSignalsDetected = scoreResult.AiSignals,
            ExplicitDisplayText = scoreResult.ExplicitDisplayText,
            PromotionalDisplayText = scoreResult.PromotionalDisplayText,
            Source = ProfileScanSource.NameOnly
        };

        logger.LogInformation("Profile scan for {User} could not read the profile ({SkipReason}); name-only scan scored {Score} ({Outcome})",
            user.ToLogInfo(), skipped.SkipReason, result.Score, result.Outcome);

        await ActOnOutcomeAsync(user, triggeringChat, result, sp, ct);
        return result;
    }

    /// <summary>
    /// Whether the user's latest scan only read the name. Only asked when a reuse path could apply.
    /// </summary>
    private static async Task<bool> LatestScanWasNameOnlyAsync(
        Models.TelegramUser? existingUser, IServiceProvider sp, CancellationToken ct)
    {
        if (existingUser?.ProfileScannedAt is null || !existingUser.ProfileScanScore.HasValue)
            return false;
        return await sp.GetRequiredService<IProfileScanResultsRepository>()
            .GetLatestSourceAsync(existingUser.TelegramUserId, ct) == ProfileScanSource.NameOnly;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScan|FullyQualifiedName~ProfileRescanJobTests" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageProcessingFirstMessageScanTests|FullyQualifiedName~JoinRenameRescanTests"`
Expected: 0 warnings; all PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi TelegramGroupsAdmin.UnitTests TelegramGroupsAdmin.IntegrationTests/Telegram/MessageProcessingFirstMessageScanTests.cs
git commit -m "feat(profile-scan): fall back to a name-only scan when the profile cannot be read"
```

---

### Task 6: Integration — a name-only scan writes its row

**Files:**
- Modify: `TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs` (new nested class after `IdentityService`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (new recipe section after "User identity service anchors")
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/NameOnlyScanTests.cs` (create)

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: `GoldenDatasetConstants.FlaggedNames.NameOnlyScanUserId` (`9333810782137`).

- [ ] **Step 1: Pin the anchor and write the failing test**

Check the anchor is still unreferenced (expect only the spec and plan docs):

```bash
grep -rln 9333810782137 TelegramGroupsAdmin.*Tests TelegramGroupsAdmin.Testing.Golden/*.cs docs
```
Expected: `docs/superpowers/specs/2026-10-03-flagged-name-masking-design.md` and `docs/superpowers/plans/2026-10-05-flagged-name-masking.md` only.

`GoldenDatasetConstants.cs`, after the `IdentityService` class:

```csharp
    /// <summary>Anchors for flagged name masking and the name-only scan (#552 part 2).</summary>
    public static class FlaggedNames
    {
        /// <summary>@loucurtsinger "Lou Curtsinger": not trusted, not a bot, no profile_scan_results rows, profile_scanned_at and profile_scan_score NULL (banned in Nov 2025, before scanning existed). NameOnlyScanTests' name-only scan writes the user's first row. Read-only otherwise.</summary>
        public const long NameOnlyScanUserId = 9333810782137;
    }
```

`TelegramGroupsAdmin.IntegrationTests/Telegram/Services/NameOnlyScanTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// A gate-admitted scan that finds no User API session scores the name alone. Real scan service,
/// scoring engine (prompt building and parsing), repositories and identity service on canonical
/// data; only the Telegram session manager and the AI provider are faked.
///
/// Canonical anchor (read-only before the scan): <see cref="GoldenDatasetConstants.FlaggedNames.NameOnlyScanUserId"/>
/// (@loucurtsinger), never scanned. The row the scan writes and the user's scan fields are the
/// assertion subject.
/// </summary>
[TestFixture]
public class NameOnlyScanTests
{
    private const long UserId = GoldenDatasetConstants.FlaggedNames.NameOnlyScanUserId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private string? _userPrompt;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _sessions = Substitute.For<ITelegramSessionManager>();
        var chat = Substitute.For<IChatService>();
        chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        chat.GetCompletionAsync(
                AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Do<string>(u => _userPrompt = u),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult
            {
                Content = """{"score": 1.0, "reason": "Ordinary personal name.", "signals_detected": [], "contains_nudity": false, "explicit_display_text": false, "promotional_display_text": false}"""
            });

        _provider = new ServiceCollection()
            .AddDataServices(_testHelper.ConnectionString)
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .AddScoped<IProfileScanResultsRepository, ProfileScanResultsRepository>()
            .AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>()
            .AddScoped<IUserIdentityService, UserIdentityService>()
            .AddSingleton(Substitute.For<IProfileScanGate>())
            .AddSingleton(chat)
            .AddSingleton(Substitute.For<IUrlPreFilterService>())
            .AddSingleton(Substitute.For<IUrlContentScrapingService>())
            .AddSingleton(Substitute.For<IStopWordsRepository>())
            .AddScoped<IProfileScoringEngine, ProfileScoringEngine>()
            .AddSingleton(Substitute.For<IConfigService>()) // no stored override → default thresholds
            .AddSingleton(Substitute.For<IBotModerationService>())
            .AddSingleton(Substitute.For<IReportsRepository>())
            .AddSingleton(Substitute.For<IAdminNotificationService>())
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _testHelper?.Dispose();
    }

    private ProfileScanService NewScanService() => new(
        _sessions,
        _provider!.GetRequiredService<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        Substitute.For<IImageProcessor>(),
        NullLogger<ProfileScanService>.Instance);

    [Test]
    public async Task NoSession_GateAdmittedScan_WritesNameOnlyRowAndUpdatesUser()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        Assert.Multiple(() =>
        {
            Assert.That(before.IsTrusted, Is.False);
            Assert.That(before.IsBot, Is.False);
            Assert.That(before.ProfileScannedAt, Is.Null);
            Assert.That(before.ProfileScanScore, Is.Null);
        });
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == UserId), Is.Zero);
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        _sessions.GetClientForChatAsync(chatId, Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);
        UserIdentity identity;
        await using (var scope = _provider!.CreateAsyncScope())
            identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ResolveAsync(UserId, CancellationToken.None);
        var start = DateTimeOffset.UtcNow;

        var result = await NewScanService().ScanUserProfileAsync(
            identity, ChatIdentity.FromId(chatId), CancellationToken.None, nameOnlyFallback: true);

        var row = await ctx.ProfileScanResults.AsNoTracking().SingleAsync(r => r.UserId == UserId);
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProfileScanSource.NameOnly));
            Assert.That(result.SkipReason, Is.Null);
            Assert.That(row.Source, Is.EqualTo((short)ProfileScanSource.NameOnly));
            Assert.That(row.Score, Is.EqualTo(1.0m));
            Assert.That(row.Outcome, Is.EqualTo((int)ProfileScanOutcome.Clean));
            Assert.That(row.RuleScore, Is.EqualTo(0.0m));
            Assert.That(row.AiScore, Is.EqualTo(1.0m));
            Assert.That(row.AiReason, Is.EqualTo("Ordinary personal name."));
            Assert.That(row.AiExplicitDisplayText, Is.False);
            Assert.That(row.AiPromotionalDisplayText, Is.False);
            Assert.That(after.ProfileScanScore, Is.EqualTo(1.0m));
            Assert.That(after.ProfileScannedAt, Is.GreaterThanOrEqualTo(start.AddSeconds(-1)));
            Assert.That(after.Bio, Is.EqualTo(before.Bio));
            Assert.That(_userPrompt, Does.Contain($"<display_name>{before.FirstName} {before.LastName}</display_name>"));
            Assert.That(_userPrompt, Does.Contain($"<username>{before.Username}</username>"));
            Assert.That(_userPrompt, Does.Contain("<bio>Unknown (could not be retrieved)</bio>"));
        });
    }
}
```

- [ ] **Step 2: Run to verify**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~NameOnlyScanTests"`
Expected: PASS (the behaviour landed in Tasks 1–5; this pins it end to end). If it fails, fix the production code, never the assertion; if the anchor's guards fail, STOP and report.

- [ ] **Step 3: Document the anchor**

`TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`, after the "User identity service anchors" section:

```markdown
### Flagged name anchors (canonical edit 2026-10-03)
Anchors are in code as `GoldenDatasetConstants.FlaggedNames` (#552 part 2). Tests read each anchor's flags back first.

| Constant | Anchor | Shape |
|---|---|---|
| `NameOnlyScanUserId` | 9333810782137 @loucurtsinger | not trusted, not a bot, no scan rows, `profile_scanned_at` NULL (banned before scanning existed). `NameOnlyScanTests`: the name-only scan writes the user's first row (the write is the assertion subject). Read-only otherwise |

Use when: a test needs a never-scanned, untrusted user whose first scan row is the subject.
```

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs TelegramGroupsAdmin.IntegrationTests/CLAUDE.md TelegramGroupsAdmin.IntegrationTests/Telegram/Services/NameOnlyScanTests.cs
git commit -m "test(profile-scan): pin the name-only scan row on canonical data"
```

---

### Task 7: Mask only while banned — verdict inputs in the views and the mapper

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Migrations/LegacyEnrichedViewSql.cs` (add `EnrichedMessagesV2`, `EnrichedReportsV2`)
- Modify: `TelegramGroupsAdmin.Data/Migrations/20261003220638_JoinUserIdentitiesInEnrichedViews.cs:18-19`
- Modify: `TelegramGroupsAdmin.Data/Models/UserIdentityView.cs`, `EnrichedMessageView.cs`, `EnrichedReportView.cs`
- Create (generated, then filled in): `TelegramGroupsAdmin.Data/Migrations/<ts>_AddNameVerdictInputsToUserIdentities.cs` (+ Designer, snapshot)
- Modify: `TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs`, `EnrichedReportMappings.cs:51-52, 109, 185`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/EnrichedMessageMappings.cs:37`, `MessageMappings.cs:15-29`, `TelegramGroupsAdmin.Telegram/Repositories/MessageHistoryRepository.cs:187-217`
- Modify: `TelegramGroupsAdmin.Core/Models/NameVerdict.cs`, `UserIdentity.cs` (doc comments)
- Test: `TelegramGroupsAdmin.UnitTests/Core/Repositories/Mappings/UserIdentityMappingTests.cs`, `EnrichedReportMappingsTests.cs`, `TelegramGroupsAdmin.UnitTests/Telegram/Repositories/Mappings/EnrichedMessageMappingsTests.cs`, `ChatAdminMappingsTests.cs`, `MessageMappingsTests.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/UserIdentitiesViewTests.cs`

**Interfaces:**
- Consumes: `profile_scan_results.ai_promotional_display_text` (Task 2).
- Produces: `UserIdentityMapping.ToIdentity(long id, string? firstName, string? lastName, string? username, bool isBot, bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned)`; `UserIdentityView.LatestScanPromotional` (`bool?`, `latest_scan_promotional`), `UserIdentityView.IsBanned` (`bool`, `is_banned`); `EnrichedMessageView.LatestScanPromotional` (`bool?`), `EnrichedMessageView.IsBanned` (`bool?`, `is_banned`); `EnrichedReportView.{Suspected,Target,ExamUser,ProfileUser}LatestScanPromotional` (`bool?`) and `...IsBanned` (`bool?`); `MessageMappings.ToModel(..., bool isBot, bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned, ...)`.

- [ ] **Step 1: Write the failing unit tests**

`UserIdentityMappingTests.cs`, replace the class body:

```csharp
    [TestCase(false, true, false, true, NameVerdict.Explicit)]
    [TestCase(false, true, true, true, NameVerdict.Explicit)]      // explicit beats promotional
    [TestCase(false, false, true, true, NameVerdict.Promotional)]
    [TestCase(false, false, false, true, NameVerdict.Clean)]
    [TestCase(false, true, false, false, NameVerdict.Clean)]       // flagged but not banned: shown normally
    [TestCase(false, false, true, false, NameVerdict.Clean)]
    [TestCase(false, false, false, false, NameVerdict.Clean)]
    [TestCase(false, null, null, true, NameVerdict.Unscanned)]
    [TestCase(false, null, null, false, NameVerdict.Unscanned)]
    [TestCase(true, true, true, true, NameVerdict.Unscanned)]      // bots are never judged
    public void ToIdentity_AppliesVerdictRule(
        bool isBot, bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned, NameVerdict expected)
    {
        var identity = UserIdentityMapping.ToIdentity(42, "A", null, null, isBot, latestScanExplicit, latestScanPromotional, isBanned);

        Assert.That(identity.Verdict, Is.EqualTo(expected));
        Assert.That(identity.DisplayName, Is.EqualTo("A"));
    }

    [Test]
    public void ToIdentity_SystemAccount_IsUnscanned()
    {
        var identity = UserIdentityMapping.ToIdentity(
            TelegramConstants.ServiceAccountUserId, "Telegram", null, null, false, true, true, true);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }
```

`EnrichedReportMappingsTests.cs`: in `ToImpersonationAlert_ExplicitFlags_MapToExplicitVerdicts` set `SuspectedIsBanned = true` and `TargetIsBanned = true, TargetLatestScanPromotional = false`; in `ToExamResult_ExplicitFlag_MapsToExplicitVerdict` set `ExamUserIsBanned = true`; in `ToProfileScanAlert_ExplicitFlag_MapsToExplicitVerdict` set `ProfileUserIsBanned = true`. Add:

```csharp
    [Test]
    public void ToProfileScanAlert_PromotionalFlag_MapsByBanState()
    {
        EnrichedReportView View(bool banned) => new()
        {
            Type = (short)ReportType.ProfileScanAlert,
            Context = """{"userId":44}""",
            ProfileFirstName = "Scanned", ProfileUserIsBot = false,
            ProfileUserLatestScanExplicit = false, ProfileUserLatestScanPromotional = true, ProfileUserIsBanned = banned
        };

        Assert.That(View(banned: true).ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Promotional));
        Assert.That(View(banned: false).ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Clean));
    }
```

`EnrichedMessageMappingsTests.cs`: the existing test sets `IsBanned = true`; add:

```csharp
    [Test]
    public void ToModel_FlaggedAuthorNotBanned_MapsToClean()
    {
        var view = new EnrichedMessageView
        {
            MessageId = 1, UserId = 42, ChatId = -100,
            FirstName = "Author", IsBot = false, LatestScanExplicit = false, LatestScanPromotional = true, IsBanned = false
        };

        Assert.That(view.ToModel().User.Verdict, Is.EqualTo(NameVerdict.Clean));
    }
```

`ChatAdminMappingsTests.cs`: the identity row becomes `new UserIdentityView { TelegramUserId = 42, FirstName = "Admin", LatestScanExplicit = true, LatestScanPromotional = false, IsBanned = true }`.

`MessageMappingsTests.cs`: the call passes `isBot: false, latestScanExplicit: true, latestScanPromotional: false, isBanned: true,`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Mappings"`
Expected: build error: no `ToIdentity` overload takes 8 arguments; `LatestScanPromotional` / `IsBanned` do not exist on the views.

- [ ] **Step 3: Freeze the current enriched view SQL**

Print the current definitions (identical at `ad942db9` and before this task):

```bash
git show ad942db9:TelegramGroupsAdmin.Data/Models/EnrichedMessageView.cs | sed -n '28,87p'
git show ad942db9:TelegramGroupsAdmin.Data/Models/EnrichedReportView.cs | sed -n '29,137p'
```

In `LegacyEnrichedViewSql.cs`, update the class summary's first sentence to "Frozen enriched_messages / enriched_reports definitions replayed by older migrations." and add two constants whose bodies are the printed raw strings verbatim (from `CREATE VIEW` to the closing `"""`):

```csharp
    /// <summary>
    /// enriched_messages joining user_identities with only latest_scan_explicit, as created by
    /// JoinUserIdentitiesInEnrichedViews (before AddNameVerdictInputsToUserIdentities).
    /// </summary>
    public const string EnrichedMessagesV2 = """
        CREATE VIEW enriched_messages AS
        SELECT
            -- Message columns
            m.message_id,
            m.user_id,
            m.chat_id,
            m.timestamp,
            m.message_text,
            m.photo_file_id,
            m.photo_file_size,
            m.urls,
            m.edit_date,
            m.content_hash,
            m.photo_local_path,
            m.photo_thumbnail_path,
            m.deleted_at,
            m.deletion_source,
            m.reply_to_message_id,
            m.media_type,
            m.media_file_id,
            m.media_file_size,
            m.media_file_name,
            m.media_mime_type,
            m.media_local_path,
            m.media_duration,
            m.content_check_skip_reason,
            m.similarity_hash,
            -- Chat enrichment (from managed_chats)
            c.chat_name,
            c.chat_icon_path,
            -- User enrichment (identity from user_identities, photo from telegram_users)
            ui.username AS user_name,
            ui.first_name,
            ui.last_name,
            ui.is_bot,
            ui.latest_scan_explicit,
            u.user_photo_path,
            -- Reply enrichment (from parent message + user_identities)
            parent_user.first_name AS reply_to_first_name,
            parent_user.last_name AS reply_to_last_name,
            parent_user.username AS reply_to_username,
            parent_user.telegram_user_id AS reply_to_user_id,
            parent_user.is_bot AS reply_to_is_bot,
            parent_user.latest_scan_explicit AS reply_to_latest_scan_explicit,
            parent.message_text AS reply_to_text,
            -- Translation (from message_translations, message-only not edits)
            t.id AS translation_id,
            t.translated_text,
            t.detected_language,
            t.confidence AS translation_confidence,
            t.translated_at
        FROM messages m
        LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
        LEFT JOIN user_identities ui ON m.user_id = ui.telegram_user_id
        LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
        LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id AND m.chat_id = parent.chat_id
        LEFT JOIN user_identities parent_user ON parent.user_id = parent_user.telegram_user_id
        LEFT JOIN message_translations t ON m.message_id = t.message_id AND m.chat_id = t.chat_id AND t.edit_id IS NULL;
        """;

    /// <summary>
    /// enriched_reports joining user_identities with only latest_scan_explicit, as created by
    /// JoinUserIdentitiesInEnrichedViews (before AddNameVerdictInputsToUserIdentities).
    /// </summary>
    public const string EnrichedReportsV2 = """
        CREATE VIEW enriched_reports AS
        SELECT
            -- Report base columns
            r.id,
            r.type,
            r.context,
            r.message_id,
            r.chat_id,
            r.report_command_message_id,
            r.reported_by_user_id,
            r.reported_by_user_name,
            r.reported_at,
            r.status,
            r.reviewed_by,
            r.reviewed_at,
            r.action_taken,
            r.admin_notes,
            r.web_user_id,

            -- Chat enrichment (all report types)
            c.chat_name,

            -- ImpersonationAlert: Suspected user (type = 1)
            suspected_ident.telegram_user_id AS suspected_user_id,
            suspected_ident.username AS suspected_username,
            suspected_ident.first_name AS suspected_first_name,
            suspected_ident.last_name AS suspected_last_name,
            suspected_ident.is_bot AS suspected_is_bot,
            suspected_ident.latest_scan_explicit AS suspected_latest_scan_explicit,
            suspected.user_photo_path AS suspected_photo_path,

            -- ImpersonationAlert: Target user (type = 1)
            target_ident.telegram_user_id AS target_user_id,
            target_ident.username AS target_username,
            target_ident.first_name AS target_first_name,
            target_ident.last_name AS target_last_name,
            target_ident.is_bot AS target_is_bot,
            target_ident.latest_scan_explicit AS target_latest_scan_explicit,
            target.user_photo_path AS target_photo_path,

            -- ExamResult: User (type = 2)
            exam_user_ident.telegram_user_id AS exam_user_id,
            exam_user_ident.username AS exam_username,
            exam_user_ident.first_name AS exam_first_name,
            exam_user_ident.last_name AS exam_last_name,
            exam_user_ident.is_bot AS exam_user_is_bot,
            exam_user_ident.latest_scan_explicit AS exam_user_latest_scan_explicit,
            exam_user.user_photo_path AS exam_photo_path,

            -- ProfileScanAlert: User (type = 3)
            profile_user_ident.telegram_user_id AS profile_user_id,
            profile_user_ident.username AS profile_username,
            profile_user_ident.first_name AS profile_first_name,
            profile_user_ident.last_name AS profile_last_name,
            profile_user_ident.is_bot AS profile_user_is_bot,
            profile_user_ident.latest_scan_explicit AS profile_user_latest_scan_explicit,
            profile_user.user_photo_path AS profile_photo_path,

            -- ContentReport: message author (type = 0)
            content_msg.user_id AS content_user_id,

            -- Reviewer (all types with web_user_id)
            reviewer.email AS reviewer_email

        FROM reports r

        -- Chat (always join)
        LEFT JOIN managed_chats c ON r.chat_id = c.chat_id

        -- ImpersonationAlert suspected user (only for type = 1)
        LEFT JOIN user_identities suspected_ident
            ON r.type = 1
            AND suspected_ident.telegram_user_id = (r.context->>'suspectedUserId')::bigint
        LEFT JOIN telegram_users suspected
            ON suspected.telegram_user_id = suspected_ident.telegram_user_id

        -- ImpersonationAlert target user (only for type = 1)
        LEFT JOIN user_identities target_ident
            ON r.type = 1
            AND target_ident.telegram_user_id = (r.context->>'targetUserId')::bigint
        LEFT JOIN telegram_users target
            ON target.telegram_user_id = target_ident.telegram_user_id

        -- ExamResult user (only for type = 2)
        LEFT JOIN user_identities exam_user_ident
            ON r.type = 2
            AND exam_user_ident.telegram_user_id = (r.context->>'userId')::bigint
        LEFT JOIN telegram_users exam_user
            ON exam_user.telegram_user_id = exam_user_ident.telegram_user_id

        -- ProfileScanAlert user (only for type = 3)
        LEFT JOIN user_identities profile_user_ident
            ON r.type = 3
            AND profile_user_ident.telegram_user_id = (r.context->>'userId')::bigint
        LEFT JOIN telegram_users profile_user
            ON profile_user.telegram_user_id = profile_user_ident.telegram_user_id

        -- ContentReport author (only for type = 0). Joins messages on its
        -- (message_id, chat_id) primary key. No telegram_users join — only the
        -- id is needed, for subject-user filtering.
        LEFT JOIN messages content_msg
            ON r.type = 0
            AND content_msg.chat_id = r.chat_id
            AND content_msg.message_id = r.message_id

        -- Reviewer (all types)
        LEFT JOIN users reviewer ON r.web_user_id = reviewer.id;
        """;
```

(Both bodies are the `ad942db9` `CreateViewSql` text unchanged; the `git show` output above lets you diff them.)

`20261003220638_JoinUserIdentitiesInEnrichedViews.cs` `Up`:

```csharp
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessagesV2);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedReportsV2);
```

(replacing the two `CreateViewSql` lines; keep the comment, and add "Replays the frozen V2 shape so later view changes cannot alter history.")

- [ ] **Step 4: Change the views**

`UserIdentityView.cs`: summary first sentence becomes "Keyless model of the user_identities view: a user's names plus the verdict inputs from their latest profile scan and their ban state." Replace `CreateViewSql` and add the properties:

```csharp
    public const string CreateViewSql = """
        CREATE VIEW user_identities AS
        SELECT u.telegram_user_id, u.first_name, u.last_name, u.username, u.is_bot,
               s.ai_explicit_display_text AS latest_scan_explicit,
               s.ai_promotional_display_text AS latest_scan_promotional,
               u.is_banned
        FROM telegram_users u
        LEFT JOIN LATERAL (
            SELECT r.ai_explicit_display_text, r.ai_promotional_display_text
            FROM profile_scan_results r
            WHERE r.user_id = u.telegram_user_id
            ORDER BY r.scanned_at DESC, r.id DESC
            LIMIT 1
        ) s ON true
        """;
```

```csharp
    /// <summary>NULL when the user has no profile scan.</summary>
    [Column("latest_scan_promotional")] public bool? LatestScanPromotional { get; set; }
    /// <summary>A flagged name is masked only while this is true.</summary>
    [Column("is_banned")] public bool IsBanned { get; set; }
```

`EnrichedMessageView.cs`: in `CreateViewSql`, after `ui.latest_scan_explicit,` insert

```sql
            ui.latest_scan_promotional,
            ui.is_banned,
```

and after the `LatestScanExplicit` property:

```csharp
    /// <summary>Promotional flag from the author's latest profile scan; NULL when unscanned.</summary>
    [Column("latest_scan_promotional")]
    public bool? LatestScanPromotional { get; set; }

    /// <summary>Author's ban state; NULL when the author has no telegram_users row.</summary>
    [Column("is_banned")]
    public bool? IsBanned { get; set; }
```

Update the SQL summary bullet to "user_identities (author names, is_bot, latest scan flags, ban state)".

`EnrichedReportView.cs`: after each `<alias>.latest_scan_explicit AS <prefix>_latest_scan_explicit,` line insert the two matching lines:

```sql
            suspected_ident.latest_scan_promotional AS suspected_latest_scan_promotional,
            suspected_ident.is_banned AS suspected_is_banned,
```
```sql
            target_ident.latest_scan_promotional AS target_latest_scan_promotional,
            target_ident.is_banned AS target_is_banned,
```
```sql
            exam_user_ident.latest_scan_promotional AS exam_user_latest_scan_promotional,
            exam_user_ident.is_banned AS exam_user_is_banned,
```
```sql
            profile_user_ident.latest_scan_promotional AS profile_user_latest_scan_promotional,
            profile_user_ident.is_banned AS profile_user_is_banned,
```

and after each `...LatestScanExplicit` property the two matching properties.

After `SuspectedLatestScanExplicit`:

```csharp
    /// <summary>Promotional flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("suspected_latest_scan_promotional")]
    public bool? SuspectedLatestScanPromotional { get; set; }

    /// <summary>The user's ban state; NULL when the user has no row.</summary>
    [Column("suspected_is_banned")]
    public bool? SuspectedIsBanned { get; set; }
```

After `TargetLatestScanExplicit`:

```csharp
    /// <summary>Promotional flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("target_latest_scan_promotional")]
    public bool? TargetLatestScanPromotional { get; set; }

    /// <summary>The user's ban state; NULL when the user has no row.</summary>
    [Column("target_is_banned")]
    public bool? TargetIsBanned { get; set; }
```

After `ExamUserLatestScanExplicit`:

```csharp
    /// <summary>Promotional flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("exam_user_latest_scan_promotional")]
    public bool? ExamUserLatestScanPromotional { get; set; }

    /// <summary>The user's ban state; NULL when the user has no row.</summary>
    [Column("exam_user_is_banned")]
    public bool? ExamUserIsBanned { get; set; }
```

After `ProfileUserLatestScanExplicit`:

```csharp
    /// <summary>Promotional flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("profile_user_latest_scan_promotional")]
    public bool? ProfileUserLatestScanPromotional { get; set; }

    /// <summary>The user's ban state; NULL when the user has no row.</summary>
    [Column("profile_user_is_banned")]
    public bool? ProfileUserIsBanned { get; set; }
```

Run: `dotnet ef migrations add AddNameVerdictInputsToUserIdentities --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`
Expected: a migration with empty `Up`/`Down` (views are keyless `ToView` entities) and an updated snapshot. Replace the bodies (add `using TelegramGroupsAdmin.Data.Models;`):

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // user_identities gains the verdict inputs (promotional flag, ban state). Both enriched
            // views select from it, so they drop first and come back with the new identity columns.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.CreateViewSql);
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.DropViewSql);
            migrationBuilder.Sql(LegacyUserIdentityViewSql.V1);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessagesV2);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedReportsV2);
        }
```

In `UserIdentityView`'s summary paragraph, add: "The next change to this view or the enriched views must freeze the current `CreateViewSql` constants for `AddNameVerdictInputsToUserIdentities` first."

- [ ] **Step 5: Apply the rule in the mapper and pass the inputs**

`UserIdentityMapping.cs`:

```csharp
/// <summary>
/// The one place a stored user row becomes a UserIdentity, and the one place the verdict rule lives:
/// a flagged name is masked only while the user is banned (the verdict decides how, the ban whether).
/// </summary>
public static class UserIdentityMapping
{
    public static UserIdentity ToIdentity(
        long id, string? firstName, string? lastName, string? username, bool isBot,
        bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned) =>
        new(id, firstName, lastName, username)
        {
            Verdict = isBot || TelegramConstants.IsSystemUser(id) || latestScanExplicit is null
                ? NameVerdict.Unscanned
                : !isBanned
                    ? NameVerdict.Clean
                    : latestScanExplicit.Value
                        ? NameVerdict.Explicit
                        : latestScanPromotional == true ? NameVerdict.Promotional : NameVerdict.Clean
        };

    /// <summary>
    /// Identity for a row read through a left join: the row's identity, or an id-only identity when
    /// the user has no row.
    /// </summary>
    public static UserIdentity ToIdentityOrIdOnly(this UserIdentityView? row, long id) =>
        row?.ToIdentity() ?? UserIdentity.FromId(id);

    public static UserIdentity ToIdentity(this UserIdentityView row) =>
        ToIdentity(row.TelegramUserId, row.FirstName, row.LastName, row.Username, row.IsBot,
            row.LatestScanExplicit, row.LatestScanPromotional, row.IsBanned);
}
```

`EnrichedReportMappings.cs`:

```csharp
            SuspectedUser = UserIdentityMapping.ToIdentity(alertContext.SuspectedUserId, view.SuspectedFirstName, view.SuspectedLastName, view.SuspectedUsername, view.SuspectedIsBot ?? false, view.SuspectedLatestScanExplicit, view.SuspectedLatestScanPromotional, view.SuspectedIsBanned ?? false),
            TargetUser = UserIdentityMapping.ToIdentity(alertContext.TargetUserId, view.TargetFirstName, view.TargetLastName, view.TargetUsername, view.TargetIsBot ?? false, view.TargetLatestScanExplicit, view.TargetLatestScanPromotional, view.TargetIsBanned ?? false),
```
```csharp
            User = UserIdentityMapping.ToIdentity(examContext.UserId, view.ExamFirstName, view.ExamLastName, view.ExamUsername, view.ExamUserIsBot ?? false, view.ExamUserLatestScanExplicit, view.ExamUserLatestScanPromotional, view.ExamUserIsBanned ?? false),
```
```csharp
            User = UserIdentityMapping.ToIdentity(alertContext.UserId, view.ProfileFirstName, view.ProfileLastName, view.ProfileUsername, view.ProfileUserIsBot ?? false, view.ProfileUserLatestScanExplicit, view.ProfileUserLatestScanPromotional, view.ProfileUserIsBanned ?? false),
```

`EnrichedMessageMappings.cs` line 37:

```csharp
                User: UserIdentityMapping.ToIdentity(view.UserId, view.FirstName, view.LastName, view.UserName, view.IsBot ?? false, view.LatestScanExplicit, view.LatestScanPromotional, view.IsBanned ?? false),
```

`MessageMappings.cs`: replace the `bool? latestScanExplicit,` parameter with

```csharp
            bool? latestScanExplicit,
            bool? latestScanPromotional,
            bool isBanned,
```

and the identity line with `User: UserIdentityMapping.ToIdentity(data.UserId, firstName, lastName, userName, isBot, latestScanExplicit, latestScanPromotional, isBanned),`.

`MessageHistoryRepository.cs` (`GetMessageAsync`): in the anonymous select, after `LatestScanExplicit = identity.LatestScanExplicit,` add

```csharp
                LatestScanPromotional = identity.LatestScanPromotional,
                IsBanned = (bool?)identity.IsBanned ?? false,
```

and in the `ToModel` call, after `latestScanExplicit: result.LatestScanExplicit,` add `latestScanPromotional: result.LatestScanPromotional, isBanned: result.IsBanned,`.

`NameVerdict.cs` summary: "What bot-written text shows for a user's name: the latest profile scan's name flags, applied only while the user is banned. Platform-neutral." `UserIdentity.Verdict` summary: "Name verdict from the latest profile scan, applied only while the user is banned (Clean otherwise). Defaults to Unscanned, which also covers Quartz payloads serialized before this member existed."

- [ ] **Step 6: Extend the view integration tests**

`UserIdentitiesViewTests.cs`: replace `EnrichedMessages_AuthorFlagsMatchUserIdentities`'s query and final assertion so all verdict inputs are compared:

```csharp
        var pairs = await (
            from m in ctx.EnrichedMessages
            join v in ctx.UserIdentities on m.UserId equals v.TelegramUserId
            select new
            {
                m.LatestScanExplicit, m.LatestScanPromotional, m.IsBanned, m.IsBot,
                ViewExplicit = v.LatestScanExplicit, ViewPromotional = v.LatestScanPromotional,
                ViewIsBanned = (bool?)v.IsBanned, ViewIsBot = v.IsBot
            })
            .ToListAsync();
```
```csharp
            Assert.That(pairs.Where(p => p.LatestScanExplicit != p.ViewExplicit
                || p.LatestScanPromotional != p.ViewPromotional
                || p.IsBanned != p.ViewIsBanned
                || p.IsBot != p.ViewIsBot), Is.Empty);
```

In `EnrichedReports_ProfileScanAlert_UserCarriesLatestScanFlag` add:

```csharp
        Assert.That(row.ProfileUserLatestScanPromotional, Is.EqualTo(expected.LatestScanPromotional));
        Assert.That(row.ProfileUserIsBanned, Is.EqualTo(expected.IsBanned));
```

Add:

```csharp
    [Test]
    public async Task View_BannedExplicitUser_ExposesVerdictInputs()
    {
        await using var ctx = _testHelper!.GetDbContext();

        var row = await ctx.UserIdentities.SingleAsync(
            v => v.TelegramUserId == GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId);

        Assert.Multiple(() =>
        {
            Assert.That(row.LatestScanExplicit, Is.True);
            Assert.That(row.LatestScanPromotional, Is.False);
            Assert.That(row.IsBanned, Is.True);
        });
    }
```

(`GetIdentitiesAsync_LatestScanWins_UnscannedAndBotsAreUnscanned` keeps expecting `Explicit` for @bagging_armado, who is banned.)

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Mappings|FullyQualifiedName~UserIdentity|FullyQualifiedName~BanCelebration" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~UserIdentitiesViewTests|FullyQualifiedName~MigrationWorkflowTests|FullyQualifiedName~BanCelebrationNameMaskingTests|FullyQualifiedName~ReportsRepository|FullyQualifiedName~MessageHistory"`
Expected: 0 warnings; all PASS. `MigrationWorkflowTests.RollbackSafety_ShouldRevertMostRecentMigration` runs this migration's `Down` and `Up` (it is the newest), and the golden template replays every migration from empty, so a wrong frozen copy fails here.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Data TelegramGroupsAdmin.Core TelegramGroupsAdmin.Telegram/Repositories TelegramGroupsAdmin.UnitTests TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/UserIdentitiesViewTests.cs
git commit -m "feat(identity): mask a flagged name only while the user is banned"
```

---

### Task 8: Canonical promotional anchors and masking end to end

**Files:**
- Modify: `TelegramGroupsAdmin.Testing.Golden/SQL/canonical/23_profile_scan_results.sql` (rows 526, 531)
- Modify: `TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs` (`FlaggedNames`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (flagged-name table; table-count row 23)
- Modify: `TelegramGroupsAdmin/TelegramGroupsAdmin.csproj:76-81` (`InternalsVisibleTo`)
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/PromotionalNameMaskingTests.cs` (create)
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/UserIdentitiesViewTests.cs`

**Interfaces:**
- Consumes: Task 7's view columns and verdict rule.
- Produces: `GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId` (`9143878698845`), `BannedPromotionalScanId` (`531`), `UnbannedPromotionalUserId` (`9213195802818`), `UnbannedPromotionalScanId` (`526`).

- [ ] **Step 1: Confirm the anchors are unreferenced and have the claimed shape**

```bash
for id in 9143878698845 9213195802818; do grep -rln $id TelegramGroupsAdmin.*Tests TelegramGroupsAdmin.Testing.Golden/*.cs docs; done
grep -c "9143878698845\|9213195802818" TelegramGroupsAdmin.Testing.Golden/SQL/canonical/23_profile_scan_results.sql
```
Expected: only the spec and this plan reference either id; the count is `2` (one scan row each: 531 and 526). `02_telegram_users.sql` shows 9143878698845 `is_banned = true`, `is_trusted = false`, and 9213195802818 `is_banned = false`, `is_trusted = false`. If any of this differs, STOP and report.

- [ ] **Step 2: Write the failing tests**

`GoldenDatasetConstants.FlaggedNames`, add:

```csharp
        /// <summary>@LisoBran "Liselotte Brandt": banned, not trusted; only scan row 531 (score 0.2, clean, AI fields filled in) has ai_promotional_display_text = true (canonical edit 2026-10-03). Read-only.</summary>
        public const long BannedPromotionalUserId = 9143878698845;
        /// <summary>@LisoBran's only scan row, flag-edited to ai_promotional_display_text = true (canonical edit 2026-10-03).</summary>
        public const long BannedPromotionalScanId = 531;
        /// <summary>@splendorfraying "Stargazer Snippet": not banned, not trusted; only scan row 526 (score 0.0) has ai_promotional_display_text = true (canonical edit 2026-10-03). Read-only.</summary>
        public const long UnbannedPromotionalUserId = 9213195802818;
        /// <summary>@splendorfraying's only scan row, flag-edited to ai_promotional_display_text = true (canonical edit 2026-10-03).</summary>
        public const long UnbannedPromotionalScanId = 526;
```

`TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`, in the `InternalsVisibleTo` item group:

```xml
      <InternalsVisibleTo Include="TelegramGroupsAdmin.IntegrationTests" />
```

`TelegramGroupsAdmin.IntegrationTests/Telegram/Services/PromotionalNameMaskingTests.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

/// <summary>
/// A promotional name is masked in what the bot posts in the group only while the user is banned,
/// and never in an admin DM. Real identity service (user_identities view + mapper), real config
/// service (the chat's effective masking), real ban celebration and admin notification services;
/// the Telegram transports and the notification audience lookups are faked.
///
/// Canonical anchors (flags read back first in every test):
/// - <see cref="GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId"/> (@LisoBran): banned,
///   only scan row 531 promotional (canonical edit 2026-10-03).
/// - <see cref="GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalUserId"/> (@splendorfraying):
///   not banned, only scan row 526 promotional (canonical edit 2026-10-03).
/// - <see cref="GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId"/> (@bagging_armado):
///   banned, latest row 534 explicit (read-only).
/// - Chat: Workshop Alumni (no welcome_config, so the global row applies; deliverable DM subscriber).
/// </summary>
[TestFixture]
public class PromotionalNameMaskingTests
{
    private const long ChatId = GoldenDatasetConstants.DmCelebrations.WorkshopAlumniChatId;
    private const long BannedPromotionalUserId = GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId;
    private const long UnbannedPromotionalUserId = GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalUserId;
    private const long BannedExplicitUserId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
    private const long AdminRecipientId = GoldenDatasetConstants.IdentityService.TrustedUserId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private IBotMessageService _messages = null!;
    private IUserNotificationService _userNotifications = null!;
    private IBotDmService _dms = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _messages = Substitute.For<IBotMessageService>();
        _messages.SendAndSaveAnimationAsync(Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<TelegramMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci => TelegramTestFactory.CreateMessage(messageId: 999, chatId: ci.ArgAt<long>(0)));
        var gifs = Substitute.For<IBanCelebrationGifRepository>();
        gifs.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationGif { Id = 1, FilePath = "ban-gifs/1.gif", FileId = "cached" });
        var captions = Substitute.For<IBanCelebrationCaptionRepository>();
        captions.ClaimNextForCycleAsync(Arg.Any<CancellationToken>())
            .Returns(new BanCelebrationCaption { Id = 1, Text = "{username} got banned!", DmText = "You got banned!" });
        _userNotifications = Substitute.For<IUserNotificationService>();

        // Admin DM path: one unlinked chat admin with DMs enabled, no web users.
        _dms = Substitute.For<IBotDmService>();
        var chatAdmins = Substitute.For<IChatAdminsRepository>();
        chatAdmins.GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>()).Returns([
            new ChatAdmin { Id = 1, ChatId = ChatId, User = UserIdentity.ForTest(AdminRecipientId), IsActive = true, BotDmEnabled = true }
        ]);
        var webUsers = Substitute.For<IUserRepository>();
        webUsers.GetWebUsersWithChatAccessAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns([]);
        var mappings = Substitute.For<ITelegramUserMappingRepository>();
        mappings.GetTelegramIdsByUserIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider);
        services.AddSingleton(new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString).Build());
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddCoreServices();

        // Real config service (effective welcome config → name masking)
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddHybridCache();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigService, ConfigService>();

        // Real identity service; no rescans
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddSingleton(Substitute.For<IProfileScanGate>());
        services.AddScoped<IUserIdentityService, UserIdentityService>();

        // Real ban celebration service
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IBanCelebrationSubscriberRepository, BanCelebrationSubscriberRepository>();
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton(gifs);
        services.AddSingleton(captions);
        services.AddSingleton(_messages);
        services.AddSingleton(_userNotifications);
        services.AddScoped<IBanCelebrationService, BanCelebrationService>();

        // Real admin notification service and DM dispatcher
        services.AddSingleton(_dms);
        services.AddSingleton(chatAdmins);
        services.AddSingleton(webUsers);
        services.AddSingleton(mappings);
        services.AddSingleton(Substitute.For<INotificationPreferencesRepository>());
        services.AddSingleton(Substitute.For<IEmailService>());
        services.AddSingleton(Substitute.For<IWebPushNotificationService>());
        services.AddSingleton(Substitute.For<IReportCallbackContextRepository>());
        services.AddScoped<NotificationDmDispatcher>();
        services.AddScoped<IAdminNotificationService, AdminNotificationService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task GuardAnchorAsync(long userId, long? expectedScanId, bool isExplicit, bool isPromotional, bool isBanned)
    {
        await using var ctx = _testHelper!.GetDbContext();
        var latest = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ScannedAt).ThenByDescending(r => r.Id)
            .FirstAsync();
        var user = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.Multiple(() =>
        {
            if (expectedScanId is { } scanId)
                Assert.That(latest.Id, Is.EqualTo(scanId), "anchor's latest scan row");
            Assert.That(latest.AiExplicitDisplayText, Is.EqualTo(isExplicit), "explicit flag");
            Assert.That(latest.AiPromotionalDisplayText, Is.EqualTo(isPromotional), "promotional flag");
            Assert.That(user.IsBanned, Is.EqualTo(isBanned), "ban state");
            Assert.That(user.IsTrusted, Is.False, "not trusted");
        });
    }

    private async Task<(UserIdentity Identity, NameMasking Masking, string ChatName, WelcomeConfig Config)> ResolveForChatAsync(long userId)
    {
        using var scope = _serviceProvider!.CreateScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>().ResolveAsync(userId, CancellationToken.None);
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var masking = await configService.GetNameMaskingAsync(ChatId);
        var config = await configService.GetEffectiveWelcomeAsync(ChatId);
        Assert.That(config, Is.Not.Null, "the global welcome_config applies to Workshop Alumni");
        var template = config!.Mode is WelcomeMode.DmWelcome or WelcomeMode.EntranceExam
            ? config.DmChatTeaserMessage
            : config.MainWelcomeMessage;
        Assert.That(template, Does.Contain("{username}"), "the chat's welcome template mentions the user");
        await using var ctx = _testHelper!.GetDbContext();
        var chatName = await ctx.ManagedChats.AsNoTracking()
            .Where(c => c.ChatId == ChatId).Select(c => c.ChatName).SingleAsync();
        return (identity, masking, chatName ?? ChatId.ToString(), config);
    }

    [Test]
    public async Task BannedPromotionalUser_WelcomeMessageInGroup_ShowsSpamLabel()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, GoldenDatasetConstants.FlaggedNames.BannedPromotionalScanId,
            isExplicit: false, isPromotional: true, isBanned: true);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(BannedPromotionalUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(masking, Is.EqualTo(NameMasking.On));
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Promotional));
            Assert.That(message.Text, Does.Contain(NameRedaction.Spam));
            Assert.That(message.Text, Does.Not.Contain(identity.DisplayName));
        });
    }

    [Test]
    public async Task BannedPromotionalUser_BanCelebrationCaptionAndSubscriberCopy_ShowSpamLabel()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, GoldenDatasetConstants.FlaggedNames.BannedPromotionalScanId,
            isExplicit: false, isPromotional: true, isBanned: true);
        using var scope = _serviceProvider!.CreateScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<IBanCelebrationSubscriberRepository>()
            .HasDeliverableSubscribersAsync(ChatId), Is.True, "Workshop Alumni has a deliverable DM subscriber");
        var sut = scope.ServiceProvider.GetRequiredService<IBanCelebrationService>();

        // The caller's copy is id-only; names and verdict come from the identity service.
        var sent = await sut.SendBanCelebrationAsync(
            ChatIdentity.FromId(ChatId), UserIdentity.FromId(BannedPromotionalUserId), isAutoBan: true);

        const string expected = NameRedaction.Spam + " got banned!";
        Assert.That(sent, Is.True);
        await _messages.Received(1).SendAndSaveAnimationAsync(
            ChatId, Arg.Any<InputFile>(), Arg.Is<TelegramMessage>(m => m!.Text == expected), Arg.Any<CancellationToken>());
        await _userNotifications.Received(1).EnqueueBanCelebrationAsync(
            Arg.Is<ChatIdentity>(c => c!.Id == ChatId), expected, 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BannedPromotionalUser_AdminNotificationDm_ShowsRealName()
    {
        await GuardAnchorAsync(BannedPromotionalUserId, GoldenDatasetConstants.FlaggedNames.BannedPromotionalScanId,
            isExplicit: false, isPromotional: true, isBanned: true);
        using var scope = _serviceProvider!.CreateScope();
        var identity = await scope.ServiceProvider.GetRequiredService<IUserIdentityService>()
            .ResolveAsync(BannedPromotionalUserId, CancellationToken.None);
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Promotional));

        await scope.ServiceProvider.GetRequiredService<IAdminNotificationService>().SendProfileScanAlertAsync(
            ChatIdentity.FromId(ChatId), identity, score: 3.0m, signals: "name_signal", aiReason: null, reportId: 1);

        var dmTexts = _dms.ReceivedCalls().SelectMany(c => c.GetArguments().OfType<string>()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(dmTexts, Has.Some.Contains(identity.DisplayName));
            Assert.That(dmTexts, Has.None.Contains(NameRedaction.Spam));
        });
    }

    [Test]
    public async Task UnbannedPromotionalUser_WelcomeMessageInGroup_ShowsRealName()
    {
        await GuardAnchorAsync(UnbannedPromotionalUserId, GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalScanId,
            isExplicit: false, isPromotional: true, isBanned: false);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(UnbannedPromotionalUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(masking, Is.EqualTo(NameMasking.On));
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Clean));
            Assert.That(message.Text, Does.Contain(identity.DisplayName));
            Assert.That(message.Text, Does.Not.Contain("[name removed"));
        });
    }

    [Test]
    public async Task BannedExplicitUser_WelcomeMessageInGroup_ShowsExplicitLabel()
    {
        // Row 534 is read-only, so its promotional flag stays false; precedence with both flags set
        // is pinned by UserIdentityMappingTests.
        await GuardAnchorAsync(BannedExplicitUserId, expectedScanId: 534,
            isExplicit: true, isPromotional: false, isBanned: true);
        var (identity, masking, chatName, config) = await ResolveForChatAsync(BannedExplicitUserId);

        var message = WelcomeMessageBuilder.FormatWelcomeMessage(config, identity, chatName, masking);

        Assert.Multiple(() =>
        {
            Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
            Assert.That(message.Text, Does.Contain(NameRedaction.Explicit));
            Assert.That(message.Text, Does.Not.Contain(NameRedaction.Spam));
        });
    }
}
```

`UserIdentitiesViewTests.cs`, add:

```csharp
    [Test]
    public async Task GetIdentitiesAsync_PromotionalNames_MaskOnlyWhileBanned()
    {
        var banned = GoldenDatasetConstants.FlaggedNames.BannedPromotionalUserId;
        var unbanned = GoldenDatasetConstants.FlaggedNames.UnbannedPromotionalUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var rows = await ctx.UserIdentities.Where(v => v.TelegramUserId == banned || v.TelegramUserId == unbanned)
            .ToDictionaryAsync(v => v.TelegramUserId);
        Assert.Multiple(() =>
        {
            Assert.That(rows[banned].LatestScanPromotional, Is.True);
            Assert.That(rows[banned].LatestScanExplicit, Is.False);
            Assert.That(rows[banned].IsBanned, Is.True);
            Assert.That(rows[unbanned].LatestScanPromotional, Is.True);
            Assert.That(rows[unbanned].IsBanned, Is.False);
        });
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var identities = await scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
            .GetIdentitiesAsync([banned, unbanned]);

        Assert.That(identities.Single(i => i.Id == banned).Verdict, Is.EqualTo(NameVerdict.Promotional));
        Assert.That(identities.Single(i => i.Id == unbanned).Verdict, Is.EqualTo(NameVerdict.Clean));
    }
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PromotionalNameMaskingTests|FullyQualifiedName~UserIdentitiesViewTests"`
Expected: the promotional tests FAIL on their guards ("promotional flag": expected True but was False); `BannedExplicitUser_WelcomeMessageInGroup_ShowsExplicitLabel` and the existing view tests PASS.

- [ ] **Step 4: Flag-edit the canonical rows**

```bash
python3 - <<'EOF'
path = "TelegramGroupsAdmin.Testing.Golden/SQL/canonical/23_profile_scan_results.sql"
old_cols = "INSERT INTO profile_scan_results (id, user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals) VALUES ("
new_cols = "INSERT INTO profile_scan_results (id, user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals, ai_promotional_display_text) VALUES ("
lines = open(path, encoding="utf-8").read().split("\n")
edited = 0
for i, line in enumerate(lines):
    if line.startswith(old_cols + "531, 9143878698845,") or line.startswith(old_cols + "526, 9213195802818,"):
        assert line.endswith(");"), line[-40:]
        lines[i] = new_cols + line[len(old_cols):-2] + ", true);"
        edited += 1
assert edited == 2, edited
open(path, "w", encoding="utf-8").write("\n".join(lines))
EOF
git diff --stat TelegramGroupsAdmin.Testing.Golden/SQL/canonical/23_profile_scan_results.sql
```
Expected: `1 file changed, 2 insertions(+), 2 deletions(-)`.

`TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`:
- Table-count row 23 becomes: `| 23 | profile_scan_results | 11 | Includes a mix of clean and flagged scans; row 534 carries an `explicit_display_text` value for the explicit-username masking tests; rows 526 and 531 carry `ai_promotional_display_text = true` (canonical edit 2026-10-03). Columns `ai_promotional_display_text` (default false) and `source` (0 = FullScan, 1 = NameOnly; every canonical row is 0). |`
- Flagged name table gains:

```markdown
| `BannedPromotionalUserId` / `BannedPromotionalScanId` | 9143878698845 @LisoBran, row 531 | banned, not trusted; its only scan row 531 (score 0.2, clean, AI fields filled in) **edited:** `ai_promotional_display_text = true`. `PromotionalNameMaskingTests`: spam label in group posts and the ban celebration caption + subscriber copy, real name in the admin DM |
| `UnbannedPromotionalUserId` / `UnbannedPromotionalScanId` | 9213195802818 @splendorfraying, row 526 | not banned, not trusted; its only scan row 526 (score 0.0) **edited:** `ai_promotional_display_text = true`. A flagged name of a user who is not banned is shown by real name |
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PromotionalNameMaskingTests|FullyQualifiedName~UserIdentitiesViewTests|FullyQualifiedName~ProfileScanResultsRepositoryTests|FullyQualifiedName~LoadCanonicalAsyncTests|FullyQualifiedName~BanCelebration"`
Expected: PASS. (The golden template is rebuilt from the embedded SQL.)

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Testing.Golden TelegramGroupsAdmin.IntegrationTests TelegramGroupsAdmin/TelegramGroupsAdmin.csproj
git commit -m "test(identity): pin promotional name masking on canonical anchors"
```

---

### Task 9: Scan history shows both flags and the source

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor:31-42`
- Test: `TelegramGroupsAdmin.ComponentTests/Components/ProfileScanHistoryDialogTests.cs` (create)

**Interfaces:**
- Consumes: `ProfileScanResultRecord.ExplicitDisplayText`, `PromotionalDisplayText`, `Source` (Task 2).

- [ ] **Step 1: Write the failing test**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>Test context for ProfileScanHistoryDialog: a substituted scan-results repository.</summary>
public class ProfileScanHistoryDialogTestContext : BunitContext
{
    protected IProfileScanResultsRepository Results { get; }

    protected ProfileScanHistoryDialogTestContext()
    {
        Results = Substitute.For<IProfileScanResultsRepository>();
        Services.AddSingleton(Results);
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>Component tests for ProfileScanHistoryDialog: source and name flag chips.</summary>
[TestFixture]
public class ProfileScanHistoryDialogTests : ProfileScanHistoryDialogTestContext
{
    [SetUp]
    public void Setup() => Results.ClearReceivedCalls();

    private static ProfileScanResultRecord Row(long id, ProfileScanSource source, bool explicitName, bool promotionalName) =>
        new(id, 42, DateTimeOffset.UtcNow.AddMinutes(-id), 3.0m, ProfileScanOutcome.HeldForReview, 0m, 3.0m,
            "reason", null, ExplicitDisplayText: explicitName, PromotionalDisplayText: promotionalName, Source: source);

    private Task<IRenderedComponent<MudDialogProvider>> OpenAsync(params ProfileScanResultRecord[] rows)
    {
        Results.GetByUserIdAsync(42, Arg.Any<CancellationToken>()).Returns(rows.ToList());
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        _ = dialogs.ShowAsync<ProfileScanHistoryDialog>("History", new DialogParameters<ProfileScanHistoryDialog>
        {
            { d => d.UserId, 42L },
            { d => d.UserDisplayName, "Sam Rivera" }
        });
        return Task.FromResult(provider);
    }

    [Test]
    public async Task NameOnlyPromotionalRow_ShowsSourceAndPromotionalChip()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.NameOnly, explicitName: false, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name only"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
        });
    }

    [Test]
    public async Task FullScanRowWithBothFlags_ShowsSourceAndBothChips()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.FullScan, explicitName: true, promotionalName: true));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Contain("Promotional name"));
        });
    }

    [Test]
    public async Task UnflaggedRow_ShowsNoNameChips()
    {
        var provider = await OpenAsync(Row(1, ProfileScanSource.FullScan, explicitName: false, promotionalName: false));

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full scan"));
            Assert.That(provider.Markup, Does.Not.Contain("Explicit name"));
            Assert.That(provider.Markup, Does.Not.Contain("Promotional name"));
        });
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~ProfileScanHistoryDialogTests"`
Expected: FAIL: markup does not contain "Name only" / "Promotional name".

- [ ] **Step 3: Implement**

In `ProfileScanHistoryDialog.razor`, inside the chips row after the score chip:

```razor
                                        <MudChip T="string"
                                                 Size="Size.Small"
                                                 Variant="Variant.Outlined"
                                                 Color="Color.Default">
                                            @(result.Source == ProfileScanSource.NameOnly ? "Name only" : "Full scan")
                                        </MudChip>
                                        @if (result.ExplicitDisplayText)
                                        {
                                            <MudChip T="string" Size="Size.Small" Color="Color.Error">Explicit name</MudChip>
                                        }
                                        @if (result.PromotionalDisplayText)
                                        {
                                            <MudChip T="string" Size="Size.Small" Color="Color.Warning">Promotional name</MudChip>
                                        }
```

and update the chips-row comment to `@* Chips row: outcome, score, source, name flags *@`. (`TelegramGroupsAdmin.Core.Models` is already imported through `_Imports.razor`, as `ProfileScanOutcome` is used here; if not, add `@using TelegramGroupsAdmin.Core.Models`.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~ProfileScanHistoryDialogTests|FullyQualifiedName~UserDetailDialog"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/ProfileScanHistoryDialog.razor TelegramGroupsAdmin.ComponentTests/Components/ProfileScanHistoryDialogTests.cs
git commit -m "feat(ui): show scan source and name flags in profile scan history"
```

---

### Task 10: Masked-username metric records the verdict

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs:58-60, 129-132`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs:92`
- Test: `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs`

**Interfaces:**
- Produces: `PipelineMetrics.RecordMaskedUsername(string trigger, NameVerdict verdict)`; tag `verdict` = `verdict.ToString().ToLowerInvariant()` (`explicit` / `promotional`).

- [ ] **Step 1: Write the failing test**

Add to the "Name Masking Tests" region of `BanCelebrationServiceTests` (add `using System.Diagnostics.Metrics;`):

```csharp
    [TestCase(NameVerdict.Promotional, "promotional")]
    [TestCase(NameVerdict.Explicit, "explicit")]
    public async Task Celebration_MaskedName_RecordsVerdictTag(NameVerdict verdict, string expectedTag)
    {
        var measurements = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "tga.pipeline.ban_celebration.masked_username_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Enqueue(tags.ToArray()));
        listener.Start();
        _mockIdentityService.ResolveAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(TestUserId, "Bad", "User", verdict: verdict));
        _mockConfigService.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        Assert.That(measurements, Has.Some.Matches<KeyValuePair<string, object?>[]>(tags =>
            tags.Contains(new KeyValuePair<string, object?>("trigger", "auto_ban"))
            && tags.Contains(new KeyValuePair<string, object?>("verdict", expectedTag))));
    }
```

(add `using System.Collections.Concurrent;`)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationServiceTests"`
Expected: FAIL: no measurement carries a `verdict` tag.

- [ ] **Step 3: Implement**

`PipelineMetrics.cs`: the counter description becomes `"Ban celebrations where the banned user's display name was masked, by trigger and name verdict"`, and:

```csharp
    public void RecordMaskedUsername(string trigger, NameVerdict verdict)
    {
        _banCelebrationMaskedUsernameTotal.Add(1, new TagList
        {
            { "trigger", trigger },
            { "verdict", verdict.ToString().ToLowerInvariant() }
        });
    }
```

(add `using TelegramGroupsAdmin.Core.Models;`)

`BanCelebrationService.cs` line 92:

```csharp
                pipelineMetrics.RecordMaskedUsername(isAutoBan ? "auto_ban" : "manual_ban", bannedUser.Verdict);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCelebrationServiceTests"`
Expected: 0 warnings; PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs
git commit -m "feat(metrics): tag masked ban celebration names with their verdict"
```

---

### Task 11: User-facing wording, docs and final verification

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:197-199` (mask caption)
- Modify: `TelegramGroupsAdmin/Docs/features/08-profile-scanning.md`
- Test: `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs` (`MaskingSwitch_CaptionExplainsScopeAndIsLinkedToTheSwitch`)

- [ ] **Step 1: Write the failing assertion**

In `MaskingSwitch_CaptionExplainsScopeAndIsLinkedToTheSwitch`, add after the existing caption assertions:

```csharp
            Assert.That(caption, Does.Contain("a banned user's name"));
```

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~MaskingSwitch_CaptionExplainsScope"`
Expected: FAIL.

- [ ] **Step 2: Update the caption**

`WelcomeSystemConfig.razor`, the caption text becomes:

```text
What the bot posts in chats, and ban celebration DMs, show "[name removed: explicit]" or "[name removed: spam]" instead of a banned user's name flagged by earlier profile scans, even while scanning is off. Admin DMs show real names.
```

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~WelcomeSystemConfigTests"`
Expected: PASS.

- [ ] **Step 3: Update `08-profile-scanning.md`**

1. After the Step 8 outcome table (before the `---` that closes "The 8-Step Scan Pipeline"), add:

```markdown
### When the Profile Can't Be Read: Name-Only Scan

If a scan the eligibility gate admitted cannot read the profile (no usable User API session, the user can't be resolved, the scan times out, or Telegram rate-limits it with `FLOOD_WAIT`), TGA scores the name alone so the user is still filtered. The name-only scan sends the same AI prompt with only the display name and username filled in and everything else marked unknown, stores a scan marked **Name only**, and acts on the score:

| Score | Outcome |
|---|---|
| Below the notify threshold | Clean |
| From the notify threshold up to the name-only ban threshold | Held for review (profile scan alert) |
| At or above the name-only ban threshold (default 4.5) | Auto-ban |

A name alone is weaker evidence than a whole profile, so it has its own, higher ban threshold. No name-only scan runs when the rule-based checks already decided, for bots, when scanning is off for the chat, when the Profile Scan AI feature is not configured, or for the periodic rescan job and manual rescans. If the AI call fails, nothing is recorded. The next scan that can read the profile replaces the name-only result.
```

2. In the AI response list, replace the `explicit_display_text` bullet with:

```markdown
- **explicit_display_text** -- whether the display name or @username itself reads as explicit sexual content
- **promotional_display_text** -- whether the display name or @username pitches to the reader (sells, solicits, recruits, lures, or points somewhere else) instead of naming someone

Both name flags are used by [name masking](#masking-flagged-names).
```

3. Replace "Both thresholds are configurable per chat." with "The ban, notify and name-only ban thresholds are all configurable per chat."

4. In "Masking Flagged Names", replace the first two paragraphs and the table with:

```markdown
Some spam accounts put the pitch or the explicit content in the name itself. Every scan, full or name-only, also judges whether the display name or @username is explicit or promotional and stores both flags with the scan. A name that says who or what the account is (a person, a nickname, a farm, shop, studio, podcast or project) stays clean; a name that speaks to the reader is flagged.

A flagged name is masked only while the user is banned. When the latest scan flagged a banned user's name, every message the bot posts in a chat shows a fixed label instead of the name:

| Flag | Shown as |
|---|---|
| Explicit name | `[name removed: explicit]` |
| Promotional name | `[name removed: spam]` |

If both flags are set, the explicit label is shown.
```

and replace the paragraph starting "A flag belongs to the account, not the chat." with:

```markdown
A flag belongs to the account, not the chat: while the user is banned, the name is masked in every chat where masking is on, including chats that do not scan profiles themselves. A name held for review is shown normally until an admin decides: a ban masks it from then on, a dismissal never does, and an unban shows the name again. A later scan that no longer flags the name lifts the mask.
```

5. In "Profile Scan History Dialog", add after the score chip bullet:

```markdown
- **Source chip** -- **Full scan** or **Name only**
- **Name flag chips** -- **Explicit name** and/or **Promotional name** when the scan flagged the name
```

6. In the per-chat settings table, add after "Admin Notify Threshold":

```markdown
| Name-only ban threshold | 4.5 | A scan that could only read the name auto-bans at this score; below it, scores at or above the notify threshold go to review. Must be at least the notify threshold |
```

and in the "Mask flagged names" row replace "instead of a flagged name" with "instead of a banned user's flagged name".

- [ ] **Step 4: Full verification**

Run, in order (integration suite runs in the foreground; it takes about a minute):

```bash
dotnet build TelegramGroupsAdmin.sln
dotnet test TelegramGroupsAdmin.UnitTests
dotnet test TelegramGroupsAdmin.ComponentTests
dotnet test TelegramGroupsAdmin.IntegrationTests
grep -rlnE "Welcome System|Profile Scan \\(User API\\)|Mask flagged names|Profile Scan History" TelegramGroupsAdmin.E2ETests/Tests
```
Expected: build with 0 warnings; every suite PASS. The `grep` finds no E2E test that opens the Welcome System form or the scan history dialog (true at planning time), so no E2E run is needed. If it lists a test class, run only that class: `dotnet test TelegramGroupsAdmin.E2ETests --filter "FullyQualifiedName~<ClassName>"`. Never run the full E2E suite.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor TelegramGroupsAdmin/Docs/features/08-profile-scanning.md TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -m "docs(profile-scan): document name-only scans and masking while banned"
```

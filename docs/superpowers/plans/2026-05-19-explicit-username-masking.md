# Explicit Username Masking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an AI-driven `explicit_display_text` signal during profile scans, persist it on `profile_scan_results`, and use it in `BanCelebrationService` to swap the `{username}` placeholder for an admin-configurable redaction string in public chat posts.

**Architecture:** Extend the existing two-layer profile scan AI response with one new boolean. Persist it on the scan row. At ban-celebration time, `BanCelebrationService` reads the latest scan via `IProfileScanResultsRepository.GetLatestByUserIdAsync` (no direct DbContext access) and the masking toggle from `WelcomeConfig.JoinSecurity.ProfileScan` via `IConfigService.GetEffectiveWelcomeAsync`. Three independent enable gates (BanCelebration enabled, ProfileScan enabled, MaskExplicitUsername toggle); fallback is always "show DisplayName."

**Tech Stack:** .NET 10, EF Core 10 (code-first), PostgreSQL 18, MudBlazor 9, NUnit, NSubstitute, bUnit, OpenTelemetry.

**Spec:** `docs/superpowers/specs/2026-05-19-explicit-username-masking-design.md`

---

## File Map

**Modified:**
- `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs` - prompt addition + JSON contract + `ProfileScanAIResponse` field
- `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs` - `AiScoringResult` field + 3 call sites
- `TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs` - new field
- `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanResult.cs` - new field
- `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs` - new column property
- `TelegramGroupsAdmin.Data/AppDbContext.cs` - HasDefaultValue Fluent config
- `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs` - new field
- `TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs` - both directions
- `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` - insert call site
- `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs` - two new fields
- `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs` - new ctor dep + masking logic
- `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor` - two new MudBlazor controls
- `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs` - two new counters
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs` - new cases
- `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs` - new masking cases
- `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs` - new UI cases
- `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs` - new masking integration cases (raw INSERT, justified)
- `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs` - round-trip case
- `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/23_profile_scan_results.sql` - canonical extension (1 new row)

**Created:**
- `TelegramGroupsAdmin.Data/Migrations/<timestamp>_AddExplicitDisplayTextToProfileScanResults.cs` (and `.Designer.cs`) - EF migration (auto-generated)
- `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs` - new integration test file

---

## Task 1: Add `ExplicitDisplayText` to AI response record + scoring engine internal record

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs` (line 250-254)
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs` (lines 26-33, 73, 97, 243)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs`

- [ ] **Step 1: Write failing test for the new field round-trip through scoring engine**

Add this test to `ProfileScoringEngineTests.cs` (place near the existing nudity tests around line 538):

```csharp
[Test]
public async Task ScoreAsync_AiReturnsExplicitDisplayTextTrue_ExplicitDisplayTextIsTrue()
{
    EnableAiWithResponse(
        """{"score": 4.5, "reason": "explicit username", "signals_detected": ["explicit_handle"], "contains_nudity": false, "explicit_display_text": true}""");

    var result = await _engine.ScoreAsync(
        profile: BuildProfile(),
        images: [],
        imageLabels: null,
        banThreshold: 4.0m,
        notifyThreshold: 2.0m,
        cancellationToken: CancellationToken.None);

    Assert.That(result.ExplicitDisplayText, Is.True);
}

[Test]
public async Task ScoreAsync_AiReturnsExplicitDisplayTextFalse_ExplicitDisplayTextIsFalse()
{
    EnableAiWithResponse(
        """{"score": 1.0, "reason": "fine name", "signals_detected": [], "contains_nudity": false, "explicit_display_text": false}""");

    var result = await _engine.ScoreAsync(
        profile: BuildProfile(),
        images: [],
        imageLabels: null,
        banThreshold: 4.0m,
        notifyThreshold: 2.0m,
        cancellationToken: CancellationToken.None);

    Assert.That(result.ExplicitDisplayText, Is.False);
}

[Test]
public async Task ScoreAsync_AiOmitsExplicitDisplayTextField_DefaultsToFalse()
{
    EnableAiWithResponse(
        """{"score": 1.0, "reason": "fine name", "signals_detected": [], "contains_nudity": false}""");

    var result = await _engine.ScoreAsync(
        profile: BuildProfile(),
        images: [],
        imageLabels: null,
        banThreshold: 4.0m,
        notifyThreshold: 2.0m,
        cancellationToken: CancellationToken.None);

    Assert.That(result.ExplicitDisplayText, Is.False);
}

[Test]
public async Task ScoreAsync_RuleBasedFastPathBan_ExplicitDisplayTextIsFalse()
{
    // Rule-based fast-path bans don't invoke the AI, so ExplicitDisplayText
    // must default to false (the AI never evaluated the display text).
    var scamProfile = BuildProfile() with { IsScam = true };

    var result = await _engine.ScoreAsync(
        profile: scamProfile,
        images: [],
        imageLabels: null,
        banThreshold: 4.0m,
        notifyThreshold: 2.0m,
        cancellationToken: CancellationToken.None);

    Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    Assert.That(result.ExplicitDisplayText, Is.False);
}
```

- [ ] **Step 2: Run tests, verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
  --filter "FullyQualifiedName~ProfileScoringEngineTests.ScoreAsync_AiReturnsExplicitDisplayText|FullyQualifiedName~ScoreAsync_AiOmitsExplicitDisplayTextField|FullyQualifiedName~ScoreAsync_RuleBasedFastPathBan_ExplicitDisplayTextIsFalse"
```

Expected: compile errors on `result.ExplicitDisplayText` (property doesn't exist yet).

- [ ] **Step 3: Add field to `ProfileScanAIResponse`**

In `ProfileScanPrompts.cs`, replace the existing record at lines 250-254:

```csharp
internal record ProfileScanAIResponse(
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("signals_detected")] string[]? SignalsDetected,
    [property: JsonPropertyName("contains_nudity")] bool ContainsNudity,
    [property: JsonPropertyName("explicit_display_text")] bool ExplicitDisplayText = false);
```

- [ ] **Step 4: Add field to `AiScoringResult` and update three call sites in `ProfileScoringEngine.cs`**

Replace the private record at lines 26-33:

```csharp
/// <summary>Result from AI vision analysis (Layer 2).</summary>
private record AiScoringResult(
    decimal Score,
    string? Reason,
    string[]? Signals,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false)
{
    public static readonly AiScoringResult Empty = new(0.0m, null, null, ContainsNudity: false, ExplicitDisplayText: false);
}
```

Replace the rule-based fast-path return at lines 66-74:

```csharp
return new ScoringResult(
    Score: Cap(ruleScore),
    Outcome: ProfileScanOutcome.Banned,
    RuleScore: ruleScore,
    AiScore: 0.0m,
    AiReason: "Rule-based detection triggered ban threshold",
    AiSignals: null,
    ContainsNudity: false,
    ExplicitDisplayText: false);
```

Replace the AI-path return at lines 90-97:

```csharp
return new ScoringResult(
    Score: totalScore,
    Outcome: outcome,
    RuleScore: ruleScore,
    AiScore: aiResult.Score,
    AiReason: aiResult.Reason,
    AiSignals: aiResult.Signals,
    ContainsNudity: aiResult.ContainsNudity,
    ExplicitDisplayText: aiResult.ExplicitDisplayText);
```

Replace the AI JSON deserialization at line 243 (inside the AI scoring helper):

```csharp
return new AiScoringResult(
    Score: score,
    Reason: response.Reason,
    Signals: response.SignalsDetected,
    ContainsNudity: response.ContainsNudity,
    ExplicitDisplayText: response.ExplicitDisplayText);
```

- [ ] **Step 5: Add field to `ScoringResult`**

Replace `TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs` body:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>Result from the full two-layer scoring pipeline.</summary>
public record ScoringResult(
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string[]? AiSignals,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false);
```

- [ ] **Step 6: Run tests, verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
  --filter "FullyQualifiedName~ProfileScoringEngineTests"
```

Expected: all existing + 4 new tests pass.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScoringEngine.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/ScoringResult.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScoringEngineTests.cs
git commit -F- <<'EOF'
feat(profile-scan): add explicit_display_text AI signal to scoring engine

New boolean on ProfileScanAIResponse threaded through AiScoringResult
and ScoringResult. Defaults to false. Rule-based fast-path returns
false (AI never ran).

EOF
```

---

## Task 2: Add `ExplicitDisplayText` to `ProfileScanResult` + prompt text addition

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanResult.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs` (system prompt + technical contract)

- [ ] **Step 1: Add field to `ProfileScanResult`**

Replace `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanResult.cs` body:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Result of a profile scan containing all extracted data, computed score, and outcome.
/// </summary>
public record ProfileScanResult(
    long TelegramUserId,
    string? Bio,
    long? PersonalChannelId,
    string? PersonalChannelTitle,
    string? PersonalChannelAbout,
    bool HasPinnedStories,
    string? PinnedStoryCaptions,
    bool IsScam,
    bool IsFake,
    bool IsVerified,
    decimal Score,
    ProfileScanOutcome Outcome,
    string? AiReason,
    string[]? AiSignalsDetected,
    bool ContainsNudity = false,
    bool ExplicitDisplayText = false,
    string? SkipReason = null);
```

- [ ] **Step 2: Update prompt - technical contract JSON shape**

In `ProfileScanPrompts.cs`, replace the line inside `GetTechnicalContract()` that lists the JSON shape (currently line 40):

```csharp
        Respond with valid JSON in this exact format:
        {"score": 0.0-5.0, "reason": "clear explanation", "signals_detected": ["signal1", "signal2"], "contains_nudity": true/false, "explicit_display_text": true/false}
```

- [ ] **Step 3: Update prompt - add the EXPLICIT DISPLAY-TEXT FLAG block in `GetBehavioralGuardrails`**

In `ProfileScanPrompts.cs`, modify `GetBehavioralGuardrails()` to append the new section after the existing NUDITY FLAG block. The full updated method body:

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

        ══════════════════════════════════════
         EXPLICIT DISPLAY-TEXT FLAG
        ══════════════════════════════════════

        Set "explicit_display_text" to true ONLY when the user's
        <display_name>, first/last name, or <username> contains
        text that itself reads as explicit content. Examples:
        - Sexual solicitation phrases ("looking for F buddy",
          "DM me horny", "fuck friends wanted")
        - Explicit slurs or graphic sexual terminology embedded
          in the visible name string
        - Sexual roleplay handles ("sub4daddy", "kinky_milf")
        - @-handles that are themselves explicit slurs

        Do NOT set this flag for:
        - Suggestive but non-explicit names ("BeachBabe92",
          "lonely_girl")
        - Names that are merely lowercase or aesthetic
        - Bios containing explicit content - only the visible
          name string matters (display name + username)
        - Photos containing explicit content - that's the
          "contains_nudity" flag, separate concern

        This flag triggers username masking in public chat posts
        (e.g., ban-celebration captions). The display name and
        username should be safe to render in front of group
        members when this flag is false.
        """;
```

- [ ] **Step 4: Update prompt - user prompt JSON shape line at line 242**

In `ProfileScanPrompts.cs`, update the final `Respond with JSON:` line in `BuildUserPrompt`:

```csharp
            Respond with JSON: {"score": 0.0-5.0, "reason": "...", "signals_detected": [...], "contains_nudity": true/false, "explicit_display_text": true/false}
```

- [ ] **Step 5: Verify the project still builds**

```bash
dotnet build TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj
```

Expected: success (0 warnings related to the change).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanResult.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanPrompts.cs
git commit -F- <<'EOF'
feat(profile-scan): teach AI prompt to flag explicit display text

Adds the EXPLICIT DISPLAY-TEXT FLAG section to the system prompt
guardrails and extends the JSON contract with explicit_display_text.
Plumbs the field through ProfileScanResult.

EOF
```

---

## Task 3: Database column + EF migration

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (around line 952)
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_AddExplicitDisplayTextToProfileScanResults.cs` (auto-generated)

- [ ] **Step 1: Add column property to `ProfileScanResultDto`**

Add this property at the end of the existing properties in `ProfileScanResultDto.cs` (before the navigation property at line 48):

```csharp
    /// <summary>AI flagged the visible display name or @username as explicit text</summary>
    [Column("ai_explicit_display_text")]
    public bool AiExplicitDisplayText { get; set; }
```

- [ ] **Step 2: Add Fluent default value in `AppDbContext.OnModelCreating`**

In `AppDbContext.cs`, find the `ProfileScanResults: decimal precision for scores` block around line 951-960 and append after the existing `AiScore` precision config:

```csharp
        modelBuilder.Entity<ProfileScanResultDto>()
            .Property(p => p.AiExplicitDisplayText)
            .HasDefaultValue(false);
```

- [ ] **Step 3: Generate the migration**

```bash
cd /Users/keisenmenger/Repos/personal/TelegramGroupsAdmin
dotnet ef migrations add AddExplicitDisplayTextToProfileScanResults \
  -p TelegramGroupsAdmin.Data \
  -s TelegramGroupsAdmin
```

Expected: a new file under `TelegramGroupsAdmin.Data/Migrations/` named `<timestamp>_AddExplicitDisplayTextToProfileScanResults.cs` plus its `.Designer.cs` and a model snapshot diff.

- [ ] **Step 4: Review the generated migration**

Open the generated `<timestamp>_AddExplicitDisplayTextToProfileScanResults.cs`. Confirm the `Up` method is a single `migrationBuilder.AddColumn<bool>(...)` for `ai_explicit_display_text` with `defaultValue: false, nullable: false`. Confirm `Down` is `migrationBuilder.DropColumn(...)`. Reject the migration (delete it and re-design) if EF generated a `DropColumn` + `AddColumn` pair (it should not for a simple add, but per `Data/CLAUDE.md` always review).

- [ ] **Step 5: Apply the migration to a local dev DB and verify**

```bash
dotnet run --project TelegramGroupsAdmin -- --migrate-only
```

Expected output includes a line indicating the new migration was applied successfully.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/ProfileScanResultDto.cs \
        TelegramGroupsAdmin.Data/AppDbContext.cs \
        TelegramGroupsAdmin.Data/Migrations/*AddExplicitDisplayTextToProfileScanResults*
git commit -F- <<'EOF'
feat(db): add ai_explicit_display_text column to profile_scan_results

NOT NULL DEFAULT false. Existing rows pick up the default
automatically; no backfill required. Reads via the existing
ix_profile_scan_results_user_id_scanned_at index.

EOF
```

---

## Task 4: Domain model + repository mapping

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs`

- [ ] **Step 1: Add `ExplicitDisplayText` to the domain record**

Replace `TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a profile scan result event.
/// Each scan produces one record with full scoring detail.
/// </summary>
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
    bool ExplicitDisplayText = false);
```

- [ ] **Step 2: Update both mapping directions with named args**

Replace `TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for profile scan result records.
/// </summary>
public static class ProfileScanResultMappings
{
    extension(DataModels.ProfileScanResultDto data)
    {
        public UiModels.ProfileScanResultRecord ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            ScannedAt: data.ScannedAt,
            Score: data.Score,
            Outcome: (ProfileScanOutcome)data.Outcome,
            RuleScore: data.RuleScore,
            AiScore: data.AiScore,
            AiReason: data.AiReason,
            AiSignals: data.AiSignals,
            ExplicitDisplayText: data.AiExplicitDisplayText);
    }

    extension(UiModels.ProfileScanResultRecord ui)
    {
        public DataModels.ProfileScanResultDto ToDto() => new()
        {
            Id = ui.Id,
            UserId = ui.UserId,
            ScannedAt = ui.ScannedAt,
            Score = ui.Score,
            Outcome = (int)ui.Outcome,
            RuleScore = ui.RuleScore,
            AiScore = ui.AiScore,
            AiReason = ui.AiReason,
            AiSignals = ui.AiSignals,
            AiExplicitDisplayText = ui.ExplicitDisplayText
        };
    }
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/ProfileScanResultRecord.cs \
        TelegramGroupsAdmin.Telegram/Repositories/Mappings/ProfileScanResultMappings.cs
git commit -F- <<'EOF'
feat(profile-scan): wire ExplicitDisplayText through domain record + mappings

ProfileScanResultRecord gains the field with default false. Both
mapping directions use named args per project convention.

EOF
```

---

## Task 5: Wire `ExplicitDisplayText` into `ProfileScanService` insert call

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` (around lines 370-377)

- [ ] **Step 1: Update the insert call site in `ProfileScanService`**

Find the `InsertAsync(new ProfileScanResultRecord(...))` block in `ProfileScanService.cs`. The existing constructor call sets these fields (around lines 370-377):

```csharp
        ScannedAt: DateTimeOffset.UtcNow,
        Score: scoreResult.Score,
        Outcome: scoreResult.Outcome,
        RuleScore: scoreResult.RuleScore,
        AiScore: scoreResult.AiScore,
        AiReason: scoreResult.AiReason,
        AiSignals: scoreResult.AiSignals is { Length: > 0 }
            ? string.Join(", ", scoreResult.AiSignals) : null
```

Append the new field at the end of the constructor argument list:

```csharp
        ScannedAt: DateTimeOffset.UtcNow,
        Score: scoreResult.Score,
        Outcome: scoreResult.Outcome,
        RuleScore: scoreResult.RuleScore,
        AiScore: scoreResult.AiScore,
        AiReason: scoreResult.AiReason,
        AiSignals: scoreResult.AiSignals is { Length: > 0 }
            ? string.Join(", ", scoreResult.AiSignals) : null,
        ExplicitDisplayText: scoreResult.ExplicitDisplayText
```

- [ ] **Step 2: Update the `ProfileScanResult` construction (around line 379-383)**

Find the construction of the in-memory `ProfileScanResult` returned from `ScanProfileAsync` (around lines 379-383). The existing block:

```csharp
        var result = new ProfileScanResult(
            user.Id, bio, personalChannelId, channelTitle, channelAbout,
            hasPinnedStories, pinnedStoryCaptions, isScam, isFake, isVerified,
            scoreResult.Score, scoreResult.Outcome, scoreResult.AiReason, scoreResult.AiSignals,
            scoreResult.ContainsNudity);
```

Convert to named args for clarity (and to include the new field):

```csharp
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
            ExplicitDisplayText: scoreResult.ExplicitDisplayText);
```

- [ ] **Step 3: Update the "skip" early-return ProfileScanResult construction**

Find the early-return construction around line 843 (the `SkipReason` branch). The existing:

```csharp
            0.0m, ProfileScanOutcome.Clean, null, null, ContainsNudity: false, skipReason);
```

becomes (with named args throughout):

```csharp
            Score: 0.0m,
            Outcome: ProfileScanOutcome.Clean,
            AiReason: null,
            AiSignalsDetected: null,
            ContainsNudity: false,
            ExplicitDisplayText: false,
            SkipReason: skipReason);
```

The full call may need additional named-arg conversion for the preceding positional arguments - convert the whole constructor call to named args. After the change, no positional booleans remain at this call site.

- [ ] **Step 4: Build the solution**

```bash
dotnet build
```

Expected: success.

- [ ] **Step 5: Run profile scan unit tests to verify no regressions**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
  --filter "FullyQualifiedName~UserApi"
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs
git commit -F- <<'EOF'
feat(profile-scan): persist ExplicitDisplayText to DB on scan completion

ProfileScanService now passes the new field through to the
repository insert and the in-memory ProfileScanResult. Named args
throughout per project convention.

EOF
```

---

## Task 6: Add new fields to `ProfileScanConfig`

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`

- [ ] **Step 1: Add the two new fields**

Replace `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`:

```csharp
namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for User API profile scanning on join.
/// </summary>
public class ProfileScanConfig
{
    /// <summary>
    /// Whether profile scanning is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    public const decimal DefaultBanThreshold = 4.0m;
    public const decimal DefaultNotifyThreshold = 2.0m;

    /// <summary>
    /// Score threshold for automatic ban (0.0-5.0)
    /// </summary>
    public decimal BanThreshold { get; set; } = DefaultBanThreshold;

    /// <summary>
    /// Score threshold for admin notification/review (0.0-5.0)
    /// </summary>
    public decimal NotifyThreshold { get; set; } = DefaultNotifyThreshold;

    /// <summary>
    /// Whether to scan user profiles when they join a chat
    /// </summary>
    public bool ScanOnJoin { get; set; } = true;

    /// <summary>
    /// Whether to re-scan when Bot API profile fields change (name/username)
    /// </summary>
    public bool ScanOnProfileChange { get; set; } = true;

    /// <summary>
    /// When true, replace the banned user's display name in public chat posts
    /// (e.g., ban-celebration captions) with <see cref="ExplicitUsernameRedactionText"/>
    /// if the most recent profile scan flagged the display text as explicit.
    /// </summary>
    public bool MaskExplicitUsername { get; set; } = true;

    /// <summary>
    /// Text substituted for the banned user's display name in public chat posts
    /// when <see cref="MaskExplicitUsername"/> is true and the AI flagged the
    /// display text as explicit.
    /// </summary>
    public string ExplicitUsernameRedactionText { get; set; } = "[explicit username redacted]";
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs
git commit -F- <<'EOF'
feat(config): add MaskExplicitUsername toggle + redaction text to ProfileScanConfig

Two new fields nested under WelcomeConfig.JoinSecurity.ProfileScan.
Defaults: masking on, "[explicit username redacted]" as the swap text.
Stored as JSONB; existing chat configs pick up defaults automatically.

EOF
```

---

## Task 7: `BanCelebrationService` reads the latest scan + applies masking

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs`

- [ ] **Step 1: Write failing unit tests for the three masking branches**

Add these tests to the existing `BanCelebrationServiceTests.cs` unit test file. The substituted `IProfileScanResultsRepository` is the new dependency.

```csharp
[Test]
public async Task SendBanCelebrationAsync_AiFlaggedAndMaskingOn_CaptionContainsRedactionText()
{
    var scan = new ProfileScanResultRecord(
        Id: 1,
        UserId: TestUserId,
        ScannedAt: DateTimeOffset.UtcNow,
        Score: 4.5m,
        Outcome: ProfileScanOutcome.Banned,
        RuleScore: 0.0m,
        AiScore: 4.5m,
        AiReason: "explicit handle",
        AiSignals: "explicit_handle",
        ExplicitDisplayText: true);

    _scanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
        .Returns(scan);

    EnableProfileScanConfig(maskExplicitUsername: true, redactionText: "[explicit username redacted]");
    SeedOneGifAndOneCaption("{username} got banned!");

    await _service.SendBanCelebrationAsync(
        chat: TestChat,
        bannedUser: TestBannedUser,
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains("[explicit username redacted]")
                         && !s.Contains(TestBannedUser.DisplayName)),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SendBanCelebrationAsync_AiFlaggedButMaskingOff_CaptionContainsDisplayName()
{
    var scan = new ProfileScanResultRecord(
        Id: 1,
        UserId: TestUserId,
        ScannedAt: DateTimeOffset.UtcNow,
        Score: 4.5m,
        Outcome: ProfileScanOutcome.Banned,
        RuleScore: 0.0m,
        AiScore: 4.5m,
        AiReason: "explicit handle",
        AiSignals: "explicit_handle",
        ExplicitDisplayText: true);

    _scanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
        .Returns(scan);

    EnableProfileScanConfig(maskExplicitUsername: false, redactionText: "[explicit username redacted]");
    SeedOneGifAndOneCaption("{username} got banned!");

    await _service.SendBanCelebrationAsync(
        chat: TestChat,
        bannedUser: TestBannedUser,
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains(TestBannedUser.DisplayName)
                         && !s.Contains("[explicit username redacted]")),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SendBanCelebrationAsync_NoScanRecord_CaptionContainsDisplayName()
{
    _scanRepository.GetLatestByUserIdAsync(TestUserId, Arg.Any<CancellationToken>())
        .Returns((ProfileScanResultRecord?)null);

    EnableProfileScanConfig(maskExplicitUsername: true, redactionText: "[explicit username redacted]");
    SeedOneGifAndOneCaption("{username} got banned!");

    await _service.SendBanCelebrationAsync(
        chat: TestChat,
        bannedUser: TestBannedUser,
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    await _mockMessageService.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains(TestBannedUser.DisplayName)
                         && !s.Contains("[explicit username redacted]")),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}
```

The test class needs a new `_scanRepository` field initialised in `SetUp` as `Substitute.For<IProfileScanResultsRepository>()` and passed into the `BanCelebrationService` constructor. The `EnableProfileScanConfig` helper configures the substituted `IConfigService.GetEffectiveWelcomeAsync` to return a `WelcomeConfig` whose `JoinSecurity.ProfileScan` carries the requested values. Add this helper if it doesn't already exist:

```csharp
private void EnableProfileScanConfig(bool maskExplicitUsername, string redactionText)
{
    var welcomeConfig = new WelcomeConfig
    {
        Enabled = true,
        JoinSecurity = new JoinSecurityConfig
        {
            ProfileScan = new ProfileScanConfig
            {
                Enabled = true,
                MaskExplicitUsername = maskExplicitUsername,
                ExplicitUsernameRedactionText = redactionText
            }
        }
    };

    _configService.GetEffectiveWelcomeAsync(TestChatId, Arg.Any<CancellationToken>())
        .Returns(welcomeConfig);
}
```

- [ ] **Step 2: Run tests, verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
  --filter "FullyQualifiedName~BanCelebrationServiceTests.SendBanCelebrationAsync_AiFlagged|FullyQualifiedName~BanCelebrationServiceTests.SendBanCelebrationAsync_NoScanRecord"
```

Expected: compile errors on the new constructor parameter / repository field.

- [ ] **Step 3: Add `IProfileScanResultsRepository` dependency to `BanCelebrationService`**

In `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs`, replace the existing primary constructor parameter list (lines 24-34) with:

```csharp
public class BanCelebrationService(
    IConfigService configService,
    IBanCelebrationCache celebrationCache,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationCaptionRepository captionRepository,
    IProfileScanResultsRepository scanRepository,
    IBotMessageService messageService,
    IBotDmService dmDeliveryService,
    IUserActionsRepository userActionsRepository,
    IOptions<AppOptions> appOptions,
    ILogger<BanCelebrationService> logger) : IBanCelebrationService
```

Add the matching `using TelegramGroupsAdmin.Telegram.Repositories;` if not already present (it is - confirm during edit).

- [ ] **Step 4: Apply the masking logic in `SendBanCelebrationAsync`**

In `BanCelebrationService.cs`, find the existing block around lines 87-95 that builds the chat caption. Replace this section:

```csharp
            // Get today's ban count for this chat
            var banCount = await GetTodaysBanCountAsync(cancellationToken);

            // Build the chat caption with placeholders replaced
            var chatCaption = ReplacePlaceholders(
                caption.Text,
                bannedUser.DisplayName,
                chat.ChatName ?? chat.Id.ToString(),
                banCount);
```

with:

```csharp
            // Get today's ban count for this chat
            var banCount = await GetTodaysBanCountAsync(cancellationToken);

            // Determine whether the AI flagged the user's display text as explicit,
            // and whether per-chat config says to mask it in the public caption.
            var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat.Id, cancellationToken);
            var profileScanCfg = welcomeConfig?.JoinSecurity?.ProfileScan ?? new ProfileScanConfig();
            var latestScan = await scanRepository.GetLatestByUserIdAsync(bannedUser.Id, cancellationToken);
            var aiFlagged = latestScan?.ExplicitDisplayText ?? false;
            var maskUsername = profileScanCfg.MaskExplicitUsername && aiFlagged;
            var displayedName = maskUsername
                ? profileScanCfg.ExplicitUsernameRedactionText
                : bannedUser.DisplayName;

            // Build the chat caption with placeholders replaced
            var chatCaption = ReplacePlaceholders(
                caption.Text,
                displayedName,
                chat.ChatName ?? chat.Id.ToString(),
                banCount);
```

Add `using TelegramGroupsAdmin.Configuration.Models.Welcome;` at the top of the file if not already present.

- [ ] **Step 5: Run unit tests, verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj \
  --filter "FullyQualifiedName~BanCelebrationServiceTests"
```

Expected: all existing + 3 new tests pass.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs \
        TelegramGroupsAdmin.UnitTests/Services/BanCelebrationServiceTests.cs
git commit -F- <<'EOF'
feat(ban-celebration): mask explicit display name in public chat caption

BanCelebrationService now reads the latest profile scan via
IProfileScanResultsRepository and the masking toggle via
IConfigService.GetEffectiveWelcomeAsync. When AI flagged the
display text AND the per-chat MaskExplicitUsername toggle is on,
the {username} placeholder substitutes the redaction text instead
of bannedUser.DisplayName. Admin DM caption (uses literal "You")
is unaffected.

EOF
```

---

## Task 8: Admin UI controls in `WelcomeSystemConfig.razor`

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor` (line 179-182 area)

- [ ] **Step 1: Add the new MudSwitch and MudTextField after `ScanOnProfileChange`**

In `WelcomeSystemConfig.razor`, find the existing `ScanOnProfileChange` switch (lines 179-182):

```razor
                                        <MudSwitch @bind-Value="_config.JoinSecurity.ProfileScan.ScanOnProfileChange"
                                                   Color="Color.Primary"
                                                   Disabled="!_config.JoinSecurity.ProfileScan.Enabled">
                                            Re-scan on profile change
                                        </MudSwitch>
```

(The exact closing markup may differ; preserve surrounding indentation.) Immediately after the closing `</MudSwitch>` of `ScanOnProfileChange`, add:

```razor
                                        <MudSwitch @bind-Value="_config.JoinSecurity.ProfileScan.MaskExplicitUsername"
                                                   Color="Color.Primary"
                                                   Disabled="!_config.JoinSecurity.ProfileScan.Enabled">
                                            Mask explicit usernames in public ban posts
                                        </MudSwitch>

                                        <MudTextField @bind-Value="_config.JoinSecurity.ProfileScan.ExplicitUsernameRedactionText"
                                                      Label="Redaction text"
                                                      HelperText="Shown in place of the user's name when the AI flags it as explicit"
                                                      MaxLength="80"
                                                      Disabled="@(!_config.JoinSecurity.ProfileScan.Enabled
                                                                  || !_config.JoinSecurity.ProfileScan.MaskExplicitUsername)" />
```

- [ ] **Step 2: Verify the project builds**

```bash
dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor
git commit -F- <<'EOF'
feat(ui): add masking toggle + redaction text to welcome settings

New MudSwitch and MudTextField under the existing ProfileScan
settings block, gated by ProfileScan.Enabled and (for the text
field) by MaskExplicitUsername. The save round-trip is wholesale
config assign so no field-level copy adjustment needed.

EOF
```

---

## Task 9: Component test extensions for the new UI controls

**Files:**
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`

- [ ] **Step 1: Write failing component tests**

Add the following tests to `WelcomeSystemConfigTests.cs`, following the file's existing pattern (substitute `IConfigService`, render, interact, assert). These tests target only the component's own logic per `[[tga_feedback_component_test_scope]]`.

```csharp
[Test]
public void WelcomeSystemConfig_RendersMaskExplicitUsernameSwitchWhenProfileScanEnabled()
{
    var welcome = WelcomeConfig.Default;
    welcome.JoinSecurity.ProfileScan = new ProfileScanConfig
    {
        Enabled = true,
        MaskExplicitUsername = true,
        ExplicitUsernameRedactionText = "[explicit username redacted]"
    };
    ConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(welcome);

    var cut = Render<WelcomeSystemConfig>(parameters => parameters
        .Add(p => p.Chat, TestChatModel));

    var maskSwitch = cut.FindAll("input[type=checkbox]")
        .Single(el => el.GetAttribute("aria-label")?.Contains("Mask explicit usernames", StringComparison.OrdinalIgnoreCase) == true
                   || el.Parent?.TextContent.Contains("Mask explicit usernames") == true);

    Assert.That(maskSwitch.HasAttribute("disabled"), Is.False);
}

[Test]
public void WelcomeSystemConfig_DisablesMaskingSwitchWhenProfileScanDisabled()
{
    var welcome = WelcomeConfig.Default;
    welcome.JoinSecurity.ProfileScan = new ProfileScanConfig
    {
        Enabled = false,
        MaskExplicitUsername = true,
        ExplicitUsernameRedactionText = "[explicit username redacted]"
    };
    ConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(welcome);

    var cut = Render<WelcomeSystemConfig>(parameters => parameters
        .Add(p => p.Chat, TestChatModel));

    var maskSwitch = cut.FindAll("input[type=checkbox]")
        .Single(el => el.Parent?.TextContent.Contains("Mask explicit usernames") == true);

    Assert.That(maskSwitch.HasAttribute("disabled"), Is.True);
}

[Test]
public void WelcomeSystemConfig_DisablesRedactionTextFieldWhenMaskingOff()
{
    var welcome = WelcomeConfig.Default;
    welcome.JoinSecurity.ProfileScan = new ProfileScanConfig
    {
        Enabled = true,
        MaskExplicitUsername = false,
        ExplicitUsernameRedactionText = "[explicit username redacted]"
    };
    ConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(welcome);

    var cut = Render<WelcomeSystemConfig>(parameters => parameters
        .Add(p => p.Chat, TestChatModel));

    var redactionInput = cut.FindAll("input")
        .Single(el => el.GetAttribute("aria-label")?.Contains("Redaction text", StringComparison.OrdinalIgnoreCase) == true
                   || el.Parent?.TextContent.Contains("Redaction text") == true);

    Assert.That(redactionInput.HasAttribute("disabled"), Is.True);
}

[Test]
public async Task WelcomeSystemConfig_Save_PassesMaskingFieldsThrough()
{
    var welcome = WelcomeConfig.Default;
    welcome.JoinSecurity.ProfileScan = new ProfileScanConfig
    {
        Enabled = true,
        MaskExplicitUsername = false,
        ExplicitUsernameRedactionText = "custom redaction"
    };
    ConfigService.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(welcome);

    var cut = Render<WelcomeSystemConfig>(parameters => parameters
        .Add(p => p.Chat, TestChatModel));

    var saveButton = cut.Find("button:contains('Save')"); // adjust selector to match existing pattern
    await saveButton.ClickAsync(new MouseEventArgs());

    await ConfigService.Received(1).SaveWelcomeAsync(
        Arg.Any<ChatIdentity>(),
        Arg.Is<WelcomeConfig>(c =>
            c.JoinSecurity.ProfileScan.MaskExplicitUsername == false
         && c.JoinSecurity.ProfileScan.ExplicitUsernameRedactionText == "custom redaction"),
        Arg.Any<Actor>(),
        Arg.Any<CancellationToken>());
}
```

If the existing test file's render and substitution helpers differ from the sketch above, prefer the file's idioms. The point is: rendered output for the disabled-state assertions, `Received()` calls on the substituted `IConfigService` for the save assertion.

- [ ] **Step 2: Run tests, verify they pass**

```bash
dotnet test TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj \
  --filter "FullyQualifiedName~WelcomeSystemConfigTests"
```

Expected: all existing + new tests pass. If a selector mismatches because of MudBlazor's rendered DOM specifics, adjust the `FindAll` / `Find` arguments to target the new fields directly (e.g., by `MudSwitch` label proximity).

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -F- <<'EOF'
test(welcome-ui): cover masking toggle + redaction text controls

Component tests assert only the component's own behavior: disabled
states gated by parent toggles, and that save round-trips the new
fields through the substituted IConfigService. Service internals
not retested.

EOF
```

---

## Task 10: Metrics counters for AI flag + masking application

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs`

- [ ] **Step 1: Add two new counters and recording methods to `PipelineMetrics`**

In `PipelineMetrics.cs`, add two new private counter fields after the existing `_profileScanSkippedTotal`:

```csharp
    private readonly Counter<long> _profileScanExplicitUsernameTotal;
    private readonly Counter<long> _banCelebrationMaskedUsernameTotal;
```

Add their creation in the constructor (after the existing `_profileScanSkippedTotal` line ~48):

```csharp
        _profileScanExplicitUsernameTotal = _meter.CreateCounter<long>(
            "tga.pipeline.profile_scan.explicit_username_total",
            description: "Profile scans where AI flagged the visible display text as explicit, by outcome");

        _banCelebrationMaskedUsernameTotal = _meter.CreateCounter<long>(
            "tga.pipeline.ban_celebration.masked_username_total",
            description: "Ban celebrations where the banned user's display name was masked, by trigger");
```

Add the two recording methods (after the existing `RecordProfileScanSkipped`):

```csharp
    public void RecordExplicitUsernameDetection(string outcome)
    {
        _profileScanExplicitUsernameTotal.Add(1, new TagList { { "outcome", outcome } });
    }

    public void RecordMaskedUsername(string trigger)
    {
        _banCelebrationMaskedUsernameTotal.Add(1, new TagList { { "trigger", trigger } });
    }
```

- [ ] **Step 2: Wire `RecordExplicitUsernameDetection` into `ProfileScanService`**

In `ProfileScanService.cs`, after the scan-result insert call but before returning the `ProfileScanResult` (around line 388), add:

```csharp
        if (scoreResult.ExplicitDisplayText)
        {
            var outcomeTag = scoreResult.Outcome switch
            {
                ProfileScanOutcome.Banned => "banned",
                ProfileScanOutcome.HeldForReview => "held_for_review",
                _ => "clean"
            };
            pipelineMetrics.RecordExplicitUsernameDetection(outcomeTag);
        }
```

The `pipelineMetrics` dependency may or may not already be injected; if not, add it as a constructor parameter (`PipelineMetrics pipelineMetrics`) and register it in DI (it should already be a singleton in `ServiceCollectionExtensions`; confirm by grepping `AddSingleton<PipelineMetrics>` before adding a registration).

- [ ] **Step 3: Wire `RecordMaskedUsername` into `BanCelebrationService`**

In `BanCelebrationService.cs`, immediately after the `var maskUsername = ...` line added in Task 7, add:

```csharp
            if (maskUsername)
            {
                pipelineMetrics.RecordMaskedUsername(isAutoBan ? "auto_ban" : "manual_ban");
            }
```

Add `PipelineMetrics pipelineMetrics` to the constructor's primary parameter list.

- [ ] **Step 4: Build and run all unit tests**

```bash
dotnet build
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj
```

Expected: success; all unit tests pass. Pre-existing unit tests for `ProfileScanService` and `BanCelebrationService` may need an additional `PipelineMetrics` substitute in their setup. If a test fails because `pipelineMetrics` is null in the SUT, add `Substitute.For<PipelineMetrics>()` to the SUT construction in that test's `SetUp`.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs \
        TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs \
        TelegramGroupsAdmin.UnitTests/
git commit -F- <<'EOF'
feat(metrics): add explicit_username detection + masked_username counters

Two new bounded-tag counters on PipelineMetrics:
- tga.pipeline.profile_scan.explicit_username_total{outcome}
- tga.pipeline.ban_celebration.masked_username_total{trigger}

ProfileScanService records the AI-flag counter when the new field
is true; BanCelebrationService records the masking counter when the
swap actually fires in chat.

EOF
```

---

## Task 11: Extend canonical fixture with a flagged scan row

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/23_profile_scan_results.sql`

- [ ] **Step 1: Add one new flagged scan row**

The canonical file currently ends with:

```sql
INSERT INTO profile_scan_results (id, user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals) VALUES (533, 9922735795237, '2026-04-30 15:42:53.888433+00', 0.0, 0, 0.0, 0.0, NULL, NULL);
SELECT pg_catalog.setval('profile_scan_results_id_seq', 533, true);
```

Replace those two lines with:

```sql
INSERT INTO profile_scan_results (id, user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals) VALUES (533, 9922735795237, '2026-04-30 15:42:53.888433+00', 0.0, 0, 0.0, 0.0, NULL, NULL);

-- Flagged display-text scan row for tests that exercise BanCelebrationService masking
-- and IProfileScanResultsRepository.GetLatestByUserIdAsync flagged-read behavior.
-- Anchored to canonical user 9220500615182 (already has a high-score scan, ID 530).
-- Newer scanned_at than ID 530 so this row wins "latest" lookups.
INSERT INTO profile_scan_results (id, user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals, ai_explicit_display_text) VALUES (534, 9220500615182, '2026-05-01 09:00:00.000000+00', 4.6, 2, 0.0, 4.6, 'Display name itself reads as explicit solicitation.', 'explicit_display_text, manufactured profile signals', true);

SELECT pg_catalog.setval('profile_scan_results_id_seq', 534, true);
```

- [ ] **Step 2: Verify canonical loads cleanly with the new migration applied**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ConfigService" --no-restore
```

Expected: tests run, canonical baseline loads without SQL errors. (Any unrelated test failure would not be caused by this change since the column has `DEFAULT false` and existing INSERTs that don't mention it will use the default.)

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/23_profile_scan_results.sql
git commit -F- <<'EOF'
test(canonical): add flagged display-text scan row for user 9220500615182

New canonical row (ID 534) anchored to an existing canonical user with
ai_explicit_display_text=true, scanned_at later than the existing scan
for that user so GetLatestByUserIdAsync returns the flagged row.

Used by:
- ProfileScanResultsRepositoryTests.GetLatestByUserIdAsync_FlaggedRow_Returned
- BanCelebrationServiceTests integration masking case (raw INSERT
  remains the documented exception there; canonical row exists for
  any future test that clones canonical)

EOF
```

---

## Task 12: New integration test file for `ProfileScanResultsRepository`

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs`

- [ ] **Step 1: Create the new integration test file**

Inspect `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ExamSessionRepositoryTests.cs` first to align the fixture pattern with the project's existing canonical-clone repository tests. Then write `ProfileScanResultsRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for ProfileScanResultsRepository covering the new
/// ai_explicit_display_text column.
///
/// Setup-data source:
/// - Write path test: SUT InsertAsync is the assertion subject (allowed).
/// - Read path tests: canonical extension (user 9220500615182, scan ID 534).
/// </summary>
[TestFixture]
public class ProfileScanResultsRepositoryTests
{
    private const long CanonicalFlaggedUserId = 9220500615182L;
    private const long CanonicalFlaggedScanId = 534L;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IProfileScanResultsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromCanonicalTemplateAsync();

        _serviceProvider = _testHelper.BuildServiceProvider();
        _repository = _serviceProvider.GetRequiredService<IProfileScanResultsRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();

        if (_testHelper is not null)
            await _testHelper.DisposeAsync();
    }

    [Test]
    public async Task InsertAsync_WithExplicitDisplayTextTrue_PersistsTrue()
    {
        var record = new ProfileScanResultRecord(
            Id: 0,   // DB-generated
            UserId: 99999999999L,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 4.7m,
            Outcome: ProfileScanOutcome.Banned,
            RuleScore: 0.0m,
            AiScore: 4.7m,
            AiReason: "test reason",
            AiSignals: "test_signal",
            ExplicitDisplayText: true);

        var insertedId = await _repository!.InsertAsync(record, CancellationToken.None);
        var roundTripped = await _repository.GetLatestByUserIdAsync(99999999999L, CancellationToken.None);

        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.Id, Is.EqualTo(insertedId));
        Assert.That(roundTripped.ExplicitDisplayText, Is.True);
    }

    [Test]
    public async Task InsertAsync_WithExplicitDisplayTextFalse_PersistsFalse()
    {
        var record = new ProfileScanResultRecord(
            Id: 0,
            UserId: 99999999988L,
            ScannedAt: DateTimeOffset.UtcNow,
            Score: 1.0m,
            Outcome: ProfileScanOutcome.Clean,
            RuleScore: 0.0m,
            AiScore: 1.0m,
            AiReason: null,
            AiSignals: null,
            ExplicitDisplayText: false);

        await _repository!.InsertAsync(record, CancellationToken.None);
        var roundTripped = await _repository.GetLatestByUserIdAsync(99999999988L, CancellationToken.None);

        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.ExplicitDisplayText, Is.False);
    }

    [Test]
    public async Task GetLatestByUserIdAsync_CanonicalFlaggedUser_ReturnsFlaggedRow()
    {
        var latest = await _repository!.GetLatestByUserIdAsync(
            CanonicalFlaggedUserId,
            CancellationToken.None);

        Assert.That(latest, Is.Not.Null,
            $"Canonical row for user {CanonicalFlaggedUserId} not found - check 23_profile_scan_results.sql");
        Assert.That(latest!.Id, Is.EqualTo(CanonicalFlaggedScanId));
        Assert.That(latest.ExplicitDisplayText, Is.True);
    }

    [Test]
    public async Task GetLatestByUserIdAsync_NoScanForUser_ReturnsNull()
    {
        var latest = await _repository!.GetLatestByUserIdAsync(
            userId: 12121212121L,
            CancellationToken.None);

        Assert.That(latest, Is.Null);
    }
}
```

The `CreateDatabaseFromCanonicalTemplateAsync` and `BuildServiceProvider` helpers should already exist on `MigrationTestHelper` based on the existing repository tests. If their method names differ in your codebase, mirror whichever helper pattern `ExamSessionRepositoryTests` uses.

- [ ] **Step 2: Run the new test file**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ProfileScanResultsRepositoryTests"
```

Expected: 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs
git commit -F- <<'EOF'
test(integration): cover ProfileScanResultsRepository new column round-trip

New integration test file. Write-path tests call InsertAsync directly
(SUT-write is the assertion subject). Read-path test reads the
flagged canonical row added in the previous commit. Null-case test
covers the never-scanned user fallback.

EOF
```

---

## Task 13: Extend `BanCelebrationServiceTests` (integration) with masking cases

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs`

- [ ] **Step 1: Add a private helper that raw-INSERTs a profile_scan_results row**

This is the rare-exception path documented in the spec: the test class uses empty-template (not canonical), so canonical extension doesn't reach this fixture, and `IProfileScanResultsRepository.InsertAsync` is not allowed for setup. Add this helper to the test class:

```csharp
/// <summary>
/// Raw INSERT (rare exception): this test class is a documented
/// canonical-clone exception (see SetUp comment). The masking
/// scenario requires a profile_scan_results row but the class
/// uses empty-template, so canonical extension doesn't help.
/// IProfileScanResultsRepository.InsertAsync is NOT used because
/// the scan row is prerequisite setup, not the assertion subject.
/// </summary>
private async Task SeedExplicitFlaggedScanAsync(long userId, bool explicitFlag)
{
    var sql = """
        INSERT INTO profile_scan_results
            (user_id, scanned_at, score, outcome, rule_score, ai_score, ai_reason, ai_signals, ai_explicit_display_text)
        VALUES
            (@userId, NOW(), 4.5, 2, 0.0, 4.5, 'test', 'test_signal', @explicitFlag)
        """;

    await using var conn = _testHelper!.OpenConnection();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("explicitFlag", explicitFlag);
    await cmd.ExecuteNonQueryAsync();
}
```

The exact connection-opening helper depends on the existing test infrastructure (the file already uses `_testHelper` to manage the database). If `OpenConnection()` doesn't exist, use whichever existing pattern this class uses to get an `NpgsqlConnection` (e.g., resolve `IConfiguration` for the connection string or grab the DbContext's connection).

- [ ] **Step 2: Add three new test cases**

Add these tests to the file:

```csharp
[Test]
public async Task SendBanCelebrationAsync_AiFlaggedAndMaskingOn_MasksUsernameInChatCaption()
{
    // Arrange: scan row with ExplicitDisplayText=true + per-chat config with masking on
    await SeedExplicitFlaggedScanAsync(TestUserId, explicitFlag: true);
    await EnableBanCelebrationConfigAsync();
    await SetWelcomeProfileScanMaskingAsync(maskingEnabled: true,
        redactionText: "[explicit username redacted]");
    await SeedOneGifAsync();
    await SeedOneCaptionAsync("{username} got banned!");

    // Act
    var sent = await _service!.SendBanCelebrationAsync(
        chat: ChatIdentity.FromId(TestChatId),
        bannedUser: new UserIdentity(TestUserId, TestUserName),
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    // Assert: mocked message service received the masked caption
    Assert.That(sent, Is.True);
    await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains("[explicit username redacted]")
                         && !s.Contains(TestUserName)),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SendBanCelebrationAsync_AiFlaggedButMaskingOff_LeavesDisplayNameInCaption()
{
    await SeedExplicitFlaggedScanAsync(TestUserId, explicitFlag: true);
    await EnableBanCelebrationConfigAsync();
    await SetWelcomeProfileScanMaskingAsync(maskingEnabled: false,
        redactionText: "[explicit username redacted]");
    await SeedOneGifAsync();
    await SeedOneCaptionAsync("{username} got banned!");

    await _service!.SendBanCelebrationAsync(
        chat: ChatIdentity.FromId(TestChatId),
        bannedUser: new UserIdentity(TestUserId, TestUserName),
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains(TestUserName)
                         && !s.Contains("[explicit username redacted]")),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SendBanCelebrationAsync_NoScanRow_LeavesDisplayNameInCaption()
{
    // No SeedExplicitFlaggedScanAsync call - user has never been scanned
    await EnableBanCelebrationConfigAsync();
    await SetWelcomeProfileScanMaskingAsync(maskingEnabled: true,
        redactionText: "[explicit username redacted]");
    await SeedOneGifAsync();
    await SeedOneCaptionAsync("{username} got banned!");

    await _service!.SendBanCelebrationAsync(
        chat: ChatIdentity.FromId(TestChatId),
        bannedUser: new UserIdentity(TestUserId, TestUserName),
        isAutoBan: true,
        cancellationToken: CancellationToken.None);

    await _mockMessageService!.Received(1).SendAndSaveAnimationAsync(
        TestChatId,
        Arg.Any<InputFile>(),
        Arg.Is<string>(s => s.Contains(TestUserName)
                         && !s.Contains("[explicit username redacted]")),
        ParseMode.Markdown,
        Arg.Any<CancellationToken>());
}
```

The `EnableBanCelebrationConfigAsync`, `SetWelcomeProfileScanMaskingAsync`, `SeedOneGifAsync`, and `SeedOneCaptionAsync` helpers either exist already in the file (the existing tests use a similar pattern) or need to be added. They route through `IConfigService.SaveBanCelebrationAsync`, `IConfigService.SaveWelcomeAsync`, `IBanCelebrationGifRepository.AddAsync`, and `IBanCelebrationCaptionRepository.AddAsync` respectively - all part of the documented empty-template-plus-production-repo-seeding exception this class uses.

- [ ] **Step 3: Run the new tests**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~BanCelebrationServiceTests.SendBanCelebrationAsync_AiFlagged|FullyQualifiedName~BanCelebrationServiceTests.SendBanCelebrationAsync_NoScanRow"
```

Expected: 3 new tests pass.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs
git commit -F- <<'EOF'
test(integration): cover ban-celebration masking end-to-end

Three new cases: AI flagged + masking on (caption uses redaction
text); AI flagged + masking off (caption keeps DisplayName); no
scan row (caption keeps DisplayName). Raw INSERT for the scan row
is the rare exception per spec - test class is the documented
canonical-clone exception (empty-template setup).

EOF
```

---

## Task 14: Extend `ConfigServiceIntegrationTests` with JSONB round-trip case

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs`

- [ ] **Step 1: Add the round-trip test**

Add to the existing file (find a suitable spot near existing `SaveWelcomeAsync` round-trip tests):

```csharp
[Test]
public async Task SaveWelcomeAsync_WithMaskingFields_RoundTripsThroughJsonb()
{
    var chat = ChatIdentity.FromId(-100123456999L);
    var actor = Actor.System;
    var welcome = WelcomeConfig.Default;
    welcome.JoinSecurity.ProfileScan = new ProfileScanConfig
    {
        Enabled = true,
        MaskExplicitUsername = false,
        ExplicitUsernameRedactionText = "custom redaction value"
    };

    await _configService!.SaveWelcomeAsync(chat, welcome, actor, CancellationToken.None);

    var reloaded = await _configService.GetEffectiveWelcomeAsync(chat.Id, CancellationToken.None);

    Assert.That(reloaded, Is.Not.Null);
    Assert.That(reloaded!.JoinSecurity.ProfileScan.MaskExplicitUsername, Is.False);
    Assert.That(reloaded.JoinSecurity.ProfileScan.ExplicitUsernameRedactionText, Is.EqualTo("custom redaction value"));
}

[Test]
public async Task GetEffectiveWelcomeAsync_NoSavedConfig_ReturnsDefaultsForMaskingFields()
{
    var freshChat = ChatIdentity.FromId(-100987654321L);

    var effective = await _configService!.GetEffectiveWelcomeAsync(freshChat.Id, CancellationToken.None);

    Assert.That(effective, Is.Not.Null);
    Assert.That(effective!.JoinSecurity.ProfileScan.MaskExplicitUsername, Is.True,
        "Default for MaskExplicitUsername should be true");
    Assert.That(effective.JoinSecurity.ProfileScan.ExplicitUsernameRedactionText,
        Is.EqualTo("[explicit username redacted]"));
}
```

- [ ] **Step 2: Run the new tests**

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ConfigServiceIntegrationTests.SaveWelcomeAsync_WithMaskingFields|FullyQualifiedName~ConfigServiceIntegrationTests.GetEffectiveWelcomeAsync_NoSavedConfig"
```

Expected: 2 new tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs
git commit -F- <<'EOF'
test(config): round-trip masking fields through JSONB save/load

Two cases: custom non-default values survive Save+GetEffective; a
brand-new chat with no saved config gets the C# property defaults
(masking on, default redaction text).

EOF
```

---

## Task 15: Final verification - full test suite + dev-mode smoke

**Files:** none

- [ ] **Step 1: Run the entire unit + component + integration suites**

```bash
dotnet test
```

Expected: all tests pass. Integration suite should complete in ~50s; full solution may take a few minutes total.

- [ ] **Step 2: Run a dev migration-only boot to confirm app startup is clean**

```bash
dotnet run --project TelegramGroupsAdmin -- --migrate-only
```

Expected: success, no migration errors, includes a log line for `AddExplicitDisplayTextToProfileScanResults` being applied (idempotently on subsequent runs).

- [ ] **Step 3: Final commit guard - confirm a clean tree**

```bash
git status
```

Expected: working tree clean. If there are stray changes, decide whether they belong to this feature or were unintended.

---

## Self-Review Results

**Spec coverage check:**
- §1 Architecture - Tasks 1, 2, 4, 5, 7 (AI signal flowing through), Task 7 (config gate + repo lookup at celebration). ✅
- §2 AI contract change - Tasks 1, 2. ✅
- §3 Persistence - Tasks 3, 4, 5. ✅
- §4 Configuration + UI + service ctor - Tasks 6, 7, 8. ✅
- §5 Tests (unit + component + integration) - Tasks 1 (unit), 7 (unit), 9 (component), 11-14 (integration including canonical extension). ✅
- §6 Metrics - Task 10. ✅

**Placeholder scan:**
- No "TBD", "TODO", "implement later", or "similar to Task N" placeholders.
- Each TDD step includes the actual code, exact command, and expected outcome.
- Where a helper method (e.g., `OpenConnection`, `EnableProfileScanConfig`) depends on the test class's existing patterns, the plan explicitly says "mirror the existing pattern" rather than leaving it abstract.

**Type consistency:**
- `ExplicitDisplayText` consistently named across `ProfileScanAIResponse`, `AiScoringResult`, `ScoringResult`, `ProfileScanResult`, `ProfileScanResultRecord`, and `ProfileScanResultDto.AiExplicitDisplayText` (the DB-mapped name).
- `MaskExplicitUsername` and `ExplicitUsernameRedactionText` consistently named in `ProfileScanConfig` and in all UI/service references.
- `RecordExplicitUsernameDetection(string outcome)` and `RecordMaskedUsername(string trigger)` signatures consistent across declaration and call sites.

**Scope:**
- Plan covers one feature end-to-end; no decomposition needed.
- 15 tasks, each independently committable. Foundational work (Tasks 1-5) lands first; behavior change (6-8) builds on it; tests and metrics (9-14) follow.

# Explicit Username Masking in Public Ban Posts - Design Spec

**Date:** 2026-05-19
**Status:** Draft, awaiting review
**Owner:** Kass

## Problem

Profile scans already detect explicit/risk-laden profiles and, when the AI flags visible nudity in profile imagery, the user's profile photo is censored via `CensorProfilePhotoAsync`. What is *not* yet covered: users whose display name or `@username` themselves contain explicit text (e.g. "looking for F buddy", "horny milf"). When such a user is banned, the existing ban-celebration GIF posted to the group chat carries that explicit text verbatim in the caption (`{username}` placeholder substitution in `BanCelebrationService.SendBanCelebrationAsync`, line 91-95). The censoring covers the photo but the name still reaches every group member.

This spec adds an AI-driven signal that says "the visible display text is itself explicit, mask it from public surfaces" and uses it to swap the offending substring in ban-celebration captions for an admin-configurable redaction string.

## Scope

**In scope:**
- One new AI judgment in the existing profile-scan response: `explicit_display_text: bool`.
- Persistence of that flag on the `profile_scan_results` table.
- Read of the latest scan's flag in `BanCelebrationService` at celebration time, and replacement of `{username}` with a redaction string when flagged.
- New per-chat configuration: `MaskExplicitUsername` (bool) and `ExplicitUsernameRedactionText` (string), nested under the existing `WelcomeConfig.JoinSecurity.ProfileScan` config tree.
- Admin UI additions to `WelcomeSystemConfig.razor` alongside the existing `ScanOnJoin` and `ScanOnProfileChange` controls.
- Unit, component, and integration test coverage.
- Two new OpenTelemetry counters.

**Out of scope (explicit non-goals):**
- Admin DM / admin alert text. Per Kass's scope decision (2026-05-19), admin-facing surfaces stay verbatim so admins can track state. Only group-facing posts are masked.
- Welcome flow messages, impersonation alerts, or any other group-facing surface that mentions the user. If those exist and need masking later, they're follow-up work.
- Re-scanning historical users to backfill the flag. Existing scan rows default to `false`; the periodic re-scan job picks them up naturally over time.
- A configurable per-chat *prompt* override.
- Migration rollback testing (project is forward-only).

## Constraints

- Single-instance homelab deployment. No distributed-system patterns.
- .NET 10, EF Core code-first, PostgreSQL 18, MudBlazor 9.
- Repositories never expose `*Dto` types; mapping extensions live in each consuming project's `Repositories/Mappings/` folder per `Data/CLAUDE.md`.
- Services never call `AppDbContext` directly - all data access goes through repositories (`global_feedback_services_via_repository`).
- Named arguments for all `bool` parameters (`tga_feedback_named_bool_params`).
- Integration tests get setup data from canonical extension (the rule); raw `INSERT` only as a rare exception (`tga_feedback_no_inline_test_data_injection`, updated 2026-05-19).

## Architecture

```
WelcomeConfig                                  ← JSONB blob in configs table
└── JoinSecurity (JoinSecurityConfig)
    └── ProfileScan (ProfileScanConfig)        ← natural home for the new toggles
        ├── Enabled
        ├── BanThreshold, NotifyThreshold
        ├── ScanOnJoin, ScanOnProfileChange
        ├── MaskExplicitUsername (NEW, bool, default true)
        └── ExplicitUsernameRedactionText (NEW, string,
                                  default "[explicit username redacted]")
```

```
ProfileScanService.ScanProfileAsync
   │
   ├─ Layer 1: rule-based scoring (no AI)
   ├─ Layer 2: ProfileScoringEngine.ScoreAsync
   │     └─ AI returns JSON including new field "explicit_display_text"
   │     │
   │     └─ AI response → AiScoringResult → ScoringResult → ProfileScanResult
   │          (new bool ExplicitDisplayText threaded through each)
   │
   ├─ Persist scan: IProfileScanResultsRepository.InsertAsync
   │     └─ writes the new ai_explicit_display_text column
   │
   └─ If banned: HandleBanAsync → BotModerationService.BanUserAsync
        └─ ban hook fires → BanCelebrationService.SendBanCelebrationAsync
              │
              ├─ Load WelcomeConfig via IConfigService.GetEffectiveWelcomeAsync
              ├─ Load latest scan via IProfileScanResultsRepository.GetLatestByUserIdAsync
              ├─ Decide: mask = ps.MaskExplicitUsername && latest?.ExplicitDisplayText
              ├─ Substitute {username} with redaction text OR DisplayName
              └─ Send GIF + (possibly masked) caption to chat
```

**Three independent enable gates (kill-switches):**
1. `BanCelebrationConfig.Enabled` - no celebration at all when off.
2. `WelcomeConfig.JoinSecurity.ProfileScan.Enabled` - no scan ran, no flag set.
3. `ProfileScanConfig.MaskExplicitUsername` - admin opts out of masking specifically.

**Fallbacks:**
- No scan record → no masking; `DisplayName` shown verbatim.
- AI didn't flag → no masking. False positives can only mask, never reveal.
- `MaskExplicitUsername=false` → no masking even if AI flagged.

## AI contract change

### Prompt addition

In `ProfileScanPrompts.BuildSystemPrompt`, add a new block alongside the existing "NUDITY FLAG" section (around line 168):

```
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
- Bios containing explicit content - only the *visible
  name string* matters (display name + username)
- Photos containing explicit content - that's the
  "contains_nudity" flag, separate concern

This flag triggers username masking in public chat posts
(e.g., ban-celebration captions). The display name and
username should be safe to render in front of group
members when this flag is false.
```

### JSON contract

Updated response schema referenced in `GetTechnicalContract` (line 40) and `BuildUserPrompt` (line 242):

```json
{
  "score": 0.0-5.0,
  "reason": "...",
  "signals_detected": [...],
  "contains_nudity": true|false,
  "explicit_display_text": true|false
}
```

### Type chain

```
ProfileScanAIResponse (ProfileScanPrompts.cs line 250)
   │  + [JsonPropertyName("explicit_display_text")]
   │    bool ExplicitDisplayText  (defaults to false on missing key)
   ▼
AiScoringResult (private record in ProfileScoringEngine.cs line 26)
   │  + bool ExplicitDisplayText = false
   ▼
ScoringResult (ScoringResult.cs line 6)
   │  + bool ExplicitDisplayText = false
   ▼
ProfileScanResult (ProfileScanResult.cs line 8)
      + bool ExplicitDisplayText = false
```

Three call sites in `ProfileScoringEngine.ScoreAsync` need updating:
- Line 73 (rule-based fast-path return): `ExplicitDisplayText: false` - AI never ran, so we can't know.
- Line 97 (AI-path return): `ExplicitDisplayText: aiResult.ExplicitDisplayText`.
- Line 243 (AI JSON deserialization): named-arg construction:

```csharp
return new AiScoringResult(
    Score: score,
    Reason: response.Reason,
    Signals: response.SignalsDetected,
    ContainsNudity: response.ContainsNudity,
    ExplicitDisplayText: response.ExplicitDisplayText);
```

**Named-arg rule:** every new or modified `bool` argument across this feature (deserializer constructors, repository inserts, celebration mask check, test helpers) uses named arguments. No positional bools.

## Persistence

### Database column

`profile_scan_results` table gets one new column:

```
ai_explicit_display_text  boolean  NOT NULL  DEFAULT false
```

The default covers existing rows automatically - no backfill. The existing index `ix_profile_scan_results_user_id_scanned_at` already supports the "latest scan per user" lookup; no new index needed.

### EF Core wiring

**`ProfileScanResultDto.cs`** - one new property:

```csharp
[Column("ai_explicit_display_text")]
public bool AiExplicitDisplayText { get; set; }
```

**`AppDbContext.cs` `OnModelCreating`** - add the default-value Fluent config in the existing `ProfileScanResultDto` block around line 952:

```csharp
modelBuilder.Entity<ProfileScanResultDto>()
    .Property(p => p.AiExplicitDisplayText)
    .HasDefaultValue(false);
```

**Migration:** `dotnet ef migrations add AddExplicitDisplayTextToProfileScanResults -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`. Review the generated migration to confirm it produces a simple `AddColumn` (per `Data/CLAUDE.md`'s warning about EF sometimes generating DROP+CREATE).

### Domain model

`ProfileScanResultRecord` (used by `IProfileScanResultsRepository`) gets one new positional field with default `false` so existing test fixtures continue to work without modification:

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
    bool ExplicitDisplayText = false);
```

### Mappings

Both `ToModel()` and `ToDto()` in `ProfileScanResultMappings.cs` get the new field with named args. Full revised mappings:

```csharp
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
```

### Repository surface

`IProfileScanResultsRepository` already exposes `InsertAsync` and `GetLatestByUserIdAsync`. The new field rides through the existing methods via the mapping change - no new interface members.

### Insert call site

`ProfileScanService` line 370-377 builds the `ProfileScanResultRecord` for persistence. Add one named-arg line: `ExplicitDisplayText: scoreResult.ExplicitDisplayText`.

## Configuration

### `ProfileScanConfig` - two new fields

Add to `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`:

```csharp
/// <summary>
/// When true, replace the banned user's display name in
/// public chat posts (e.g., ban-celebration captions) with
/// <see cref="ExplicitUsernameRedactionText"/> if the most
/// recent profile scan flagged the display text as explicit.
/// </summary>
public bool MaskExplicitUsername { get; set; } = true;

/// <summary>
/// Text substituted for the banned user's display name in
/// public chat posts when <see cref="MaskExplicitUsername"/>
/// is true and the AI flagged the display text as explicit.
/// </summary>
public string ExplicitUsernameRedactionText { get; set; } = "[explicit username redacted]";
```

`WelcomeConfig` is JSONB-serialized; existing chats pick up the C# property defaults automatically when fields are missing from stored JSON. No config-data migration needed.

### `BanCelebrationService` - constructor change

Add one new constructor parameter: `IProfileScanResultsRepository scanRepo`. The interface is already registered in DI via `ProfileScanService`'s existing dependency - no new DI registration.

### Read flow at celebration time

After the existing `BanCelebrationConfig` gate (line 46-69 of `BanCelebrationService.SendBanCelebrationAsync`), and before building the caption (line 91-95):

```csharp
var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat.Id, cancellationToken);
var profileScanCfg = welcomeConfig?.JoinSecurity?.ProfileScan ?? new ProfileScanConfig();

var latestScan = await scanRepo.GetLatestByUserIdAsync(bannedUser.Id, cancellationToken);
var aiSaysExplicit = latestScan?.ExplicitDisplayText ?? false;

var maskUsername = profileScanCfg.MaskExplicitUsername && aiSaysExplicit;
var displayedName = maskUsername
    ? profileScanCfg.ExplicitUsernameRedactionText
    : bannedUser.DisplayName;
```

The existing `ReplacePlaceholders` call at line 91-95 then uses `displayedName` in place of `bannedUser.DisplayName`. The DM caption path (line 295) continues to use literal `"You"` and is unaffected.

## Admin UI

### `WelcomeSystemConfig.razor` additions

The existing ProfileScan block (lines 141-182) is the home. After the `ScanOnProfileChange` switch, add two controls inside the same disabled-gated section:

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
              Disabled="!_config.JoinSecurity.ProfileScan.Enabled
                        || !_config.JoinSecurity.ProfileScan.MaskExplicitUsername" />
```

### Save round-trip copy

`WelcomeSystemConfig.razor` manually copies fields between draft state and the saved config (line 579 area for `MaxKicksBeforeBan`). Both new fields must be added to that copy block in both directions (load → draft, draft → save). Missing this step is a silent bug - the form binds correctly but values never persist.

## Metrics

Per `Core/CLAUDE.md`'s metrics pattern (domain-scoped singleton class, `tga.` prefix, `_total` suffix, bounded tags).

**`PipelineMetrics`** - one new counter:

```csharp
private readonly Counter<long> _profileScanExplicitUsernameTotal =
    _meter.CreateCounter<long>(
        "tga.profile_scan.explicit_username_total",
        description: "Profile scans where AI flagged the visible display text as explicit");

public void RecordExplicitUsernameDetection(string outcome)
{
    _profileScanExplicitUsernameTotal.Add(1, new TagList
    {
        { "outcome", outcome }   // "clean" | "held_for_review" | "banned"
    });
}
```

Called from `ProfileScanService` after the scan completes when `result.ExplicitDisplayText == true`.

**`PipelineMetrics`** (or a sibling celebration class - implementer's call based on existing conventions) - second counter:

```csharp
private readonly Counter<long> _banCelebrationMaskedUsernameTotal =
    _meter.CreateCounter<long>(
        "tga.ban_celebration.masked_username_total",
        description: "Ban celebrations where the banned user's display name was masked");

public void RecordMaskedUsername(string trigger)
{
    _banCelebrationMaskedUsernameTotal.Add(1, new TagList
    {
        { "trigger", trigger }   // "auto_ban" | "manual_ban"
    });
}
```

Called from `BanCelebrationService` when masking actually fires. Bounded enum tags; no user/chat IDs (cardinality rule).

## Tests

### Unit

| Test file | Cases |
|---|---|
| `ProfileScoringEngineTests.cs` (existing - extend) | AI response with `"explicit_display_text": true` lands `ExplicitDisplayText == true` on `ScoringResult`. Same with `false`. Missing JSON field → `false` (deserializer default). Rule-based fast-path → `false` (AI never ran). |
| `BanCelebrationServiceTests.cs` (existing unit test, not the integration one - extend) | Three branches with substituted `IProfileScanResultsRepository.GetLatestByUserIdAsync`: (a) returns flagged scan + `MaskExplicitUsername=true` → caption text contains the redaction string; (b) returns flagged scan + `MaskExplicitUsername=false` → caption contains `DisplayName`; (c) returns null → caption contains `DisplayName`. |

### Component

`WelcomeSystemConfigTests.cs` (existing - extend). Scope per [[tga_feedback_component_test_scope]]: only the component's own logic.
- New `MaskExplicitUsername` switch renders; is disabled when parent `ProfileScan.Enabled` is false.
- New redaction-text field is disabled when `MaskExplicitUsername` is false.
- Save round-trips both new fields: invoke save and assert `Received().Call` on substituted `IConfigService.SaveWelcomeAsync` with the expected `WelcomeConfig` shape - do not retest the service internals.

### Integration

Decision tree: canonical extension is the rule; raw `INSERT` is the rare exception; SUT writes only when testing the SUT write itself.

| Test | File | Setup-data source |
|---|---|---|
| Repo `InsertAsync` write path | `IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs` (new file) | SUT write is the assertion. Call `InsertAsync` directly. |
| Repo `GetLatestByUserIdAsync` read path | same new file | **Canonical extension.** Add a flagged scan row to canonical for a stable test-user constant. |
| `BanCelebrationService` masking behavior | `IntegrationTests/Telegram/Services/BanCelebrationServiceTests.cs` (existing - extend) | **Raw `INSERT`** with a justification comment. This class is the documented canonical-clone exception (empty-template setup; canonical's 74 captions + 92 GIFs would contaminate RNG selection). Canonical extension doesn't help; `IProfileScanResultsRepository.InsertAsync` would violate the SUT-writes-as-setup prohibition. |
| `ConfigService` round-trips new fields | `IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs` (existing - extend) | Save + Get are both SUT behaviors under test. Legitimate round-trip pattern. |
| Canonical schema includes new column | Automatic via migration replay | n/a - migration runs on canonical baseline build. |

Justification comment to include in the `BanCelebrationServiceTests` raw INSERT:

```csharp
// Raw INSERT (rare exception): this test class is a documented
// canonical-clone exception (see SetUp comment). The masking
// scenario requires a profile_scan_results row but the class
// uses empty-template, so canonical extension doesn't help.
// IProfileScanResultsRepository.InsertAsync is NOT used because
// the scan row is prerequisite setup, not the assertion subject.
```

### Tests that should keep passing (regression guard)

All existing `ProfileScoringEngineTests` and `BanCelebrationServiceTests` cases. New positional `bool` defaults to `false` so existing JSON fixtures (`"contains_nudity": false`, no `explicit_display_text` field) remain valid.

### Out of scope for testing

- AI judgment quality - that's prompt engineering, not a unit test concern.
- Migration rollback - project is forward-only.
- A dedicated `ProfileScanService` integration test - unit + repo integration coverage is sufficient.

## Implementation order (suggested for the writing-plans phase)

1. AI contract: prompt update, response record field, type chain through `AiScoringResult` → `ScoringResult` → `ProfileScanResult`. Unit tests in `ProfileScoringEngineTests`.
2. DB schema: `ProfileScanResultDto` field + `OnModelCreating` default + migration. `ProfileScanResultRecord` field + mappings. `ProfileScanService` insert call site update.
3. Config + UI: `ProfileScanConfig` fields, `WelcomeSystemConfig.razor` controls + save-roundtrip copy. Component tests.
4. `BanCelebrationService`: constructor change, read + decide logic, ReplacePlaceholders update. Unit tests.
5. Integration tests: new repo test file (canonical extension + InsertAsync test), existing celebration test extension (raw INSERT with justification), existing config service test extension.
6. Metrics: two new counters wired into `ProfileScanService` and `BanCelebrationService`.

Steps 1-2 are foundation. Steps 3-4 are the behavior change. Step 5 is regression coverage. Step 6 is observability.

## Risks and rollbacks

- **AI false positives** (flagging non-explicit names as explicit) → masking applies when it shouldn't. Impact: a banned user's name is hidden from group members; admins still see it in DMs. Low harm.
- **AI false negatives** (missing genuinely explicit names) → no masking. Existing behavior; no regression.
- **Schema migration failure** → no celebrations until rolled back. Rollback path: revert the migration + redeploy. Integration tests catch this before merge.
- **Old JSONB configs missing new fields** → C# property defaults apply. No deserialization break.
- **Existing tests** → all should pass; new field defaults to `false` and existing fixtures don't set it.

## References

- Related global feedback: `[[global_feedback_services_via_repository]]`, `[[tga_feedback_named_bool_params]]`, `[[tga_feedback_no_inline_test_data_injection]]` (updated 2026-05-19), `[[tga_feedback_component_test_scope]]`.
- Related runtime ref: `[[tga_reference_integration_test_runtime]]` (~50s integration suite, foreground-friendly).
- Codebase docs: `TelegramGroupsAdmin.Data/CLAUDE.md` (mapping pattern, migration warnings), `TelegramGroupsAdmin.Core/CLAUDE.md` (metrics pattern), `.claude/rules/telegram-bot-architecture.md` (service layering).

# Post-Review Fixes — Config Layer Restoration & AI Relocation PR

**Date:** 2026-04-27
**Branch:** `refactor/restore-core-relocate-config-and-ai`
**Scope:** Address all blockers and architectural findings from the multi-agent
`/review-all` review and the test-suite run on the in-flight config/AI relocation
branch. Fixes 6 failing tests, closes a regression-induced audit-emission gap,
unifies repository-through-service architecture for ContentDetection, and tidies
up two consumer-pattern drifts.
**Predecessor spec:** `docs/superpowers/specs/2026-04-25-config-and-ai-relocation-design.md`
**PR target:** `develop` (per project workflow — never PR feature branches to `master`).

---

## Context

The 7-task `refactor/restore-core-relocate-config-and-ai` branch (predecessor
spec above) is feature-complete. Build is green (0 errors / 0 warnings under
`TreatWarningsAsErrors=true`). The full test suite passes 2987 of 2993 tests
(99.8%). Six failures and a small set of architectural concerns surfaced
together from a multi-agent review and the test run. They must clear before the
PR can open against `develop`.

The deeper finding underneath the failures: Task 6's migration of ContentDetection
consumers off the generic `IConfigService.GetEffectiveAsync<T>(ConfigType, …)`
API onto direct `IContentDetectionConfigRepository` injection closed the
generic-API hole but lost the audit emission that the old typed path provided.
Today, ContentDetection writes are silent in `audit_log` while every other
config emits `AuditEventType.ConfigurationChanged` on save/delete. That is a
real regression introduced by this branch and must be fixed before merge.

This spec walks the fixes in implementation order. Items 1 and 2 are must-fix
for tests to go green. Item 8 is the architectural fix for the audit-gap
regression and is the largest item by site count. Items 3-6 are smaller cleanups.

---

## Problem Statement

### Test failures (must-fix to merge)

1. **4 `ConfigServiceIntegrationTests` fail with PostgreSQL FK violation
   `FK_audit_log_users_actor_web_user_id`.** The audit row's `actor_web_user_id`
   FK references a non-existent synthetic user.
2. **2 `WelcomeSystemConfigTests` ComponentTests fail with "SaveAsync was not
   called".** Tests reach into private component state through a mock callback
   that the component never invokes because MudForm validation gates the save.

### Architectural findings

3. **Audit-emission regression on ContentDetection writes (the load-bearing
   issue).** 4 admin write sites currently call
   `IContentDetectionConfigRepository.UpdateGlobalConfigAsync` /
   `UpdateChatConfigAsync` directly, bypassing `ConfigService` and emitting zero
   audit rows. Plus the bot hot path (~10 backend services) reads the repo
   directly and bypasses the HybridCache layer that every other config enjoys.
4. **HybridCache factory parameter discarded at all 17 call sites in
   `ConfigService.cs`.** The lambda signature uses `_` instead of the factory's
   provided `CancellationToken`. Per Microsoft's HybridCache documentation, the
   factory's token represents the *combined* cancellation across concurrent
   callers — using an outer captured token can cancel waiting callers who didn't
   ask to cancel. Behavior-preserving fix in non-cancelled paths.
5. **Theoretical lost-update race on multiplexed `moderation_config` JSONB
   column** (`SaveWarningSystemAsync` / `SaveInviteCommandAsync`). Has zero
   production callers today. Forward-staged for issue #196.
6. **`UrlFiltersConfig.razor` uses divergent actor-resolution pattern.** Manually
   constructs the `Actor` from `AuthenticationStateProvider` claims with an
   `Actor.FromSystem("unknown")` fallback that masks misconfigured auth instead
   of failing fast. Other 8 settings pages use the canonical
   `[CascadingParameter] WebUserIdentity?` + `WebUser!.ToActor()`.
7. **One `ConfigRepositoryIntegrationTests` symmetric coverage gap:**
   `DeleteWarningSystem_PreservesInviteCommandSibling` exists but
   `DeleteInviteCommand_PreservesWarningSystemSibling` does not. The
   multiplexed-column delete is the highest-risk path of the moderation
   write/delete suite.

---

## Item 1 — Fix 4 failing `ConfigServiceIntegrationTests` (FK violation)

### Decision

Seed `web_users` via the existing `GoldenDataset.SeedAsync(...)` golden dataset
in `[SetUp]`, then attribute the test's `Actor` to `GoldenDataset.Users.User1_Id`
(`owner@example.com`, Owner role). Keep `Actor.FromWebUser(...)` — switching to
`Actor.FromSystem(...)` would dilute the test's intent (the security guarantee
that real-user saves are auditable end-to-end, including the FK).

### Reuse

- `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs:406` —
  `SeedAsync(AppDbContext, IDataProtectionProvider?)`. Loads embedded SQL in FK
  dependency order.
- `GoldenDataset.cs:22-23` — `User1_Id`, `User1_Email` constants.
- Canonical SetUp pattern at
  `TelegramGroupsAdmin.IntegrationTests/Notifications/NotificationRepositoriesTests.cs`
  lines 76-86.

### Files to modify

`TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs`
(single file; ~90 lines edited).

### Changes

1. **In `[SetUp]`** — after `_testHelper.CreateDatabaseAndApplyMigrationsAsync()`
   and after building the service provider, before resolving `_sut`:
   ```csharp
   var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
   var dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
   await using (var context = await contextFactory.CreateDbContextAsync())
   {
       await GoldenDataset.SeedAsync(context, dataProtectionProvider);
   }
   ```

2. **Add a private static `TestActor` field** at the top of the fixture (DRY
   across the 4 tests):
   ```csharp
   private static readonly Actor TestActor =
       Actor.FromWebUser(GoldenDataset.Users.User1_Id, GoldenDataset.Users.User1_Email);
   ```

3. **Replace** the four inline
   `Actor.FromWebUser("integration-test-user", "u@example.com")` (and variants)
   call sites at lines 91, 113, 129, 148 with `TestActor`.

### Verification

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ConfigServiceIntegrationTests"
```
Expected: 4 of 4 previously-failing tests pass; full integration project remains
green.

---

## Item 2 — Fix 2 failing `WelcomeSystemConfigTests` ComponentTests

### Decision

Rewrite both tests to assert on rendered Markup (`cut.Markup`, `cut.Find`,
`cut.FindAll`) instead of capturing a `SaveWelcomeAsync` mock callback.
Component tests should verify rendering and component-internal logic, not
config-persistence side effects. The remaining 29 of 31 tests in the fixture
already follow this canonical pattern. Verified that all four properties bind
two-way to MudBlazor controls (`@bind-Value` on MudTextField/MudNumericField,
`Checked` on MudSwitch), so values flow into the rendered DOM.

### Files to modify

`TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`
(single file).

### Changes

1. **Rename** test 1 from `Save_PersistsTrustedBypass_ThroughConfigService` to
   `LoadConfig_TrustedBypassPopulated_RendersCustomTemplates` (name now matches
   what it actually tests). Test 2's name is accurate; keep it.

2. **Rewrite both bodies** to drop the `SaveWelcomeAsync` mock setup and the
   `cut.Instance.SaveConfiguration()` plumbing. Replace with `cut.WaitForAssertion`
   against `cut.Markup` (free-text matches) or `cut.Find` / `cut.FindAll` (typed
   selectors for numeric / boolean values to avoid false positives).

3. **Test 1 (renamed) — verify the form renders the custom TrustedBypass
   templates**:
   - Mock `GetWelcomeAsync` to return a config with
     `MainWelcomeMessage = "Welcome {username}!"` (load-as-is branch) and a
     populated `TrustedBypass` (`AnnouncementMessageAdmin = "admin custom"`,
     `AnnouncementMessageTrusted = "trusted custom"`,
     `AnnouncementTtlSeconds = 45`).
   - Render.
   - `cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("admin custom")))`
     and likewise for `"trusted custom"`. For the numeric TTL, prefer a typed
     selector against the MudNumericField bound to `AnnouncementTtlSeconds`.

4. **Test 2 — verify migration branch preserves TrustedBypass and JoinSecurity
   in the rendered form**:
   - Mock returns legacy config with empty `MainWelcomeMessage` (migration
     branch at lines 570-587 of `WelcomeSystemConfig.razor`), populated
     `TrustedBypass` (`"legacy admin template"`, `"legacy trusted template"`),
     and `JoinSecurity.Cas.Enabled = true`.
   - `cut.WaitForAssertion` that the rendered TrustedBypass announcement fields
     show the legacy values AND the JoinSecurity CAS toggle renders as enabled.

5. **Remove the `SaveWelcomeAsync` mock setup blocks** (current lines 577-580,
   620-623) — no longer needed.

### Verification

```bash
dotnet test TelegramGroupsAdmin.ComponentTests/TelegramGroupsAdmin.ComponentTests.csproj \
  --filter "FullyQualifiedName~WelcomeSystemConfigTests"
```
Expected: all 31 tests pass. Spot-check by intentionally breaking the migration
branch (e.g., reset `_config.TrustedBypass` instead of preserving) and confirm
the test fails — proves the assertion catches a real regression.

---

## Item 8 — Route ALL ContentDetection access through `IConfigService`

This is the largest item by site count and the regression fix for the audit gap.
Item 7 is fully absorbed into Item 8 (every dead `@inject IConfigService`
becomes a live one).

### Decision

Add a typed `ContentDetectionConfig` surface to `IConfigService` that mirrors
the 8 other configs. Migrate every production caller (admin razors AND bot
hot-path services) onto it. After migration, `IContentDetectionConfigRepository`
is injected only by `ConfigService` itself.

Verified scope (~25 sites):

#### Admin write razors (4 — closes the audit gap)

| File | Line(s) | From | To |
|---|---|---|---|
| `Components/Shared/ContentDetection/DetectionOverview.razor` | 604, 608 | `ContentDetectionRepository.UpdateGlobalConfigAsync` / `UpdateChatConfigAsync` | `ConfigService.SaveContentDetectionAsync(chat, _config, actor)` |
| `Components/Shared/ContentDetection/ConfigDialogWrapper.razor` | 115, 119 | same | same |
| `Components/Shared/ContentDetection/CriticalChecks.razor` | 255 | `UpdateGlobalConfigAsync` | `SaveContentDetectionAsync` |
| `Components/Shared/ContentDetection/UrlFiltersConfig.razor` | 330 | `UpdateGlobalConfigAsync` | `SaveContentDetectionAsync` |

#### Admin delete site (newly catalogued — missing from prior plan)

| File | Line | From | To |
|---|---|---|---|
| `Components/Shared/ChatManagement/ChatConfigModal.razor` | 482 | `SpamConfigRepository.DeleteChatConfigAsync(ChatInfo.Record.Identity.Id)` | `ConfigService.DeleteContentDetectionAsync(chatIdentity, actor)` |

#### Admin read razors (10)

9 sub-tab components in `Components/Shared/ContentDetection/`:
`ContentDetectionBayes`, `Image`, `InvisibleChars`, `Similarity`, `Spacing`,
`StopWords`, `Video`, `Translation`, `OpenAI`. Plus three additional read
sites:

- `ContentTester.razor:429` — `GetEffectiveConfigAsync(0)`
- `DetectionOverview.razor:584` — `GetEffectiveConfigAsync(chatId)` (note:
  this same file's lines 604/608 are admin writes already listed above)
- `CriticalChecks.razor:181` — `GetEffectiveConfigAsync(0)` (note: line 255
  is the admin write listed above)

Each currently calls `ContentDetectionRepository.GetGlobalConfigAsync()` /
`ContentDetectionRepository.GetByChatIdAsync(Chat!.Id)` /
`GetEffectiveConfigAsync(...)`. Migrate to:
- `ConfigService.GetContentDetectionAsync(0)` for global
- `ConfigService.GetContentDetectionAsync(Chat.Id)` for chat-specific
- `ConfigService.GetEffectiveContentDetectionAsync(chatId)` for effective reads

After migration: drop `@inject IContentDetectionConfigRepository ContentDetectionRepository`
from each. The dead `@inject IConfigService ConfigService` (Item 7) becomes
live. Drop `@using` directives for `Configuration.Repositories` only if no other
type from the namespace remains.

`Chats.razor:10` (cascading parameter source for `ChatConfigModal`'s
`SpamConfigRepository`) — replace with `[Inject] IConfigService ConfigService`
and update the cascading parameter signature. Verify whether the cascade is
still needed after the modal migrates to `ConfigService` directly; if not,
remove it entirely.

#### Bot hot-path backend services (~10)

| File | Lines | Injection style |
|---|---|---|
| `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs` | 26, 39 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Checks/ImageContentCheckV2.cs` | 33 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Checks/VideoContentCheckV2.cs` | 33 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Services/ImpersonationDetectionService.cs` | 41, 60 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Services/UserAutoTrustService.cs` | 25, 32 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Handlers/TranslationHandler.cs` | 23, 29 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Handlers/FileScanningHandler.cs` | 31, 38 | ctor inject |
| `TelegramGroupsAdmin.ContentDetection/Handlers/LanguageWarningHandler.cs` | 53 | scoped `GetRequiredService` |
| `TelegramGroupsAdmin.ContentDetection/Processors/MessageEditProcessor.cs` | 144 | scoped `GetRequiredService` |
| `TelegramGroupsAdmin.ContentDetection/Services/DetectionActionService.cs` | 40 | scoped `GetRequiredService` |

Layering check (verified): `ContentDetection` already references
`Configuration`, so injecting `IConfigService` is legal — no circular
reference. Confirm exact directory paths during implementation in case any
file moved (e.g., `Handlers/` vs. `Services/`).

`ContentCheckCoordinator.cs:86` already calls `IConfigService.GetCriticalCheckNamesAsync`
— **no change needed.**

### Performance note (verified)

`ContentDetectionConfigRepository` does **not** cache today (verified — zero
cache references in the file). Every bot hot-path read currently hits Postgres
on every check. After migration, reads use `IConfigService`'s 15-minute
HybridCache. **Net positive for bot hot path**, not a regression. Admin writes
correctly bust the cache via the existing `InvalidateAsync` helper:
- per-chat write removes both `cfg_content_detection_{chatId}` and
  `cfg_effective_content_detection_{chatId}`
- global write (chatId == 0) uses the `effective_content_detection` tag

### `IConfigService` additions

Mirror the verified pattern (e.g., Welcome at `ConfigService.cs:30-50`):

```csharp
ValueTask<ContentDetectionConfig?> GetContentDetectionAsync(long chatId, CancellationToken ct = default);
ValueTask<ContentDetectionConfig?> GetEffectiveContentDetectionAsync(long chatId, CancellationToken ct = default);
Task SaveContentDetectionAsync(ChatIdentity chat, ContentDetectionConfig config, Actor initiator, CancellationToken ct = default);
Task DeleteContentDetectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);
```

Keep `GetAllContentDetectionConfigsAsync` and `GetCriticalCheckNamesAsync` as
direct delegations (current `ConfigService.cs:288-292`) — they serve different
use cases and have one caller each (`DetectionOverview.razor` for the bulk
admin listing; `ContentCheckCoordinator.cs:86` for fast-path critical-check
name extraction).

### `ConfigService` implementation

Same six-method pattern as Welcome. Use `factoryCt` from the start (per Item 4),
not `_`, so the new sites don't need to be revisited:

```csharp
public ValueTask<ContentDetectionConfig?> GetContentDetectionAsync(long chatId, CancellationToken ct = default)
    => cache.GetOrCreateAsync($"cfg_content_detection_{chatId}",
        async factoryCt => chatId == 0
            ? await contentDetectionRepository.GetGlobalConfigAsync(factoryCt)
            : await contentDetectionRepository.GetByChatIdAsync(chatId, factoryCt),
        CacheOptions, cancellationToken: ct);

public ValueTask<ContentDetectionConfig?> GetEffectiveContentDetectionAsync(long chatId, CancellationToken ct = default)
    => cache.GetOrCreateAsync($"cfg_effective_content_detection_{chatId}",
        async factoryCt => await contentDetectionRepository.GetEffectiveConfigAsync(chatId, factoryCt),
        CacheOptions, tags: ["effective_content_detection"], cancellationToken: ct);

public async Task SaveContentDetectionAsync(ChatIdentity chat, ContentDetectionConfig config, Actor initiator, CancellationToken ct = default)
{
    if (chat.Id == 0)
        await contentDetectionRepository.UpdateGlobalConfigAsync(config, ct);
    else
        await contentDetectionRepository.UpdateChatConfigAsync(chat.Id, config, ct);
    await EmitAuditAsync("ContentDetection", chat, initiator, ct);
    await InvalidateAsync("content_detection", chat.Id, ct);
    logger.LogInformation("ContentDetection config saved for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
}

public async Task DeleteContentDetectionAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default)
{
    await contentDetectionRepository.DeleteChatConfigAsync(chat.Id, ct);
    await EmitAuditAsync("ContentDetection (deleted)", chat, initiator, ct);
    await InvalidateAsync("content_detection", chat.Id, ct);
    logger.LogInformation("ContentDetection config deleted for {Chat} by {Actor}", chat.DisplayName, initiator.DisplayName);
}
```

(`IContentDetectionConfigRepository.DeleteChatConfigAsync` exists at line 44 of
the interface — verified.)

### Tests for Item 8

Mirror the existing thin pattern (1-2 unit tests per typed config method;
one integration test for the audit-gap regression).

- **`ConfigServiceTests.cs`** — add 3 unit tests:
  1. `SaveContentDetectionAsync_DelegatesAndEmitsAudit_AndInvalidatesCache`
  2. `DeleteContentDetectionAsync_DelegatesAndEmitsAudit_AndInvalidatesCache`
  3. `GetContentDetectionAsync_ChatIdZero_RoutesToGlobal` (multiplexed-routing
     edge case; the chatId == 0 branch in the lambda)
- **`ConfigServiceIntegrationTests.cs`** — add 1 integration test:
  `SaveContentDetectionAsync_AppendsAuditLogRow` (the audit-gap regression
  proof; uses `TestActor` from Item 1).
- **Existing `ContentDetectionConfigRepositoryTests`** — unaffected; leave.

### Verification for Item 8

- `dotnet build TelegramGroupsAdmin.sln` — must remain 0 errors / 0 warnings.
- New unit + integration tests pass.
- Manual smoke (deferred to merged-PR smoke verification): toggle a Bayes
  filter via `ContentDetectionBayes` panel inside `DetectionOverview`. Inspect
  `audit_log`: exactly one new row with `event_type = ConfigurationChanged`,
  `value` containing `"ContentDetection"` and the chat display name,
  `actor_web_user_id` matching the logged-in admin.
- Grep audit:
  ```bash
  grep -rn "IContentDetectionConfigRepository" --include="*.cs" --include="*.razor" \
    --exclude-dir="*Tests" --exclude-dir="bin" --exclude-dir="obj"
  ```
  Expected: shows only the interface, the implementation, the DI registration,
  and `ConfigService.cs`. Zero direct callers in razors or bot services.

---

## Item 6 — Normalize `UrlFiltersConfig.razor` actor pattern

### Decision

Switch to the canonical
`[CascadingParameter] private WebUserIdentity? WebUser { get; set; }` +
`var actor = WebUser!.ToActor();` pattern used by every other settings page.
Remove the duplicated 11-line manual claim-extraction block. Bundle this edit
with Item 8's edit to the same file.

### Files to modify

`TelegramGroupsAdmin/Components/Shared/ContentDetection/UrlFiltersConfig.razor`
(single file — same file Item 8 edits).

### Changes

1. **Remove** `@inject AuthenticationStateProvider AuthStateProvider` (line 12).
2. **Remove** `@using Microsoft.AspNetCore.Components.Authorization` (line 4) if
   no other type from that namespace remains. Verify with grep before removing.
3. **Add** `[CascadingParameter] private WebUserIdentity? WebUser { get; set; }`
   to the `@code` block, near the existing parameter declarations. Match
   placement convention from `FileScanningSettings.razor:476`.
4. **In `SaveConfiguration` (~line 437)**: replace the 5-line claim-extraction
   block with `var actor = WebUser!.ToActor();`. (Note: the same method already
   moves to `SaveContentDetectionAsync` per Item 8, so write the actor variable
   inline at the call site.)
5. **In `SaveWhitelist` (~line 534)**: same replacement.

### Verification

`dotnet build` — 0 errors / 0 warnings (`TreatWarningsAsErrors` enforces this).
Manual smoke: save a hard-block / soft-block / whitelist URL filter — verify the
audit row's `actor_web_user_id` matches the logged-in admin (deferred to
merged-PR smoke).

---

## Item 4 — Fix HybridCache factory token plumbing

### Decision

For each existing `cache.GetOrCreateAsync(...)` call site in `ConfigService.cs`,
rename the lambda's `_` discard to `factoryCt` and pass it to the repository
call instead of the outer captured `ct`. The outer
`cancellationToken: ct` argument stays — that's how HybridCache learns the
caller's cancellation intent. Behavior-preserving fix in non-cancelled paths.

The new ContentDetection lambdas in Item 8 use this corrected pattern from the
start, so this item only touches the **17 pre-existing sites** identified in
verification.

### Files to modify

`TelegramGroupsAdmin.Configuration/Services/ConfigService.cs` — 17 sites at
lines 30, 35, 60, 65, 90, 95, 120, 125, 150, 155, 180, 185, 210, 215, 240, 245,
270 (verified).

### Pattern transformation

```csharp
// Before
public ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
    => cache.GetOrCreateAsync($"cfg_welcome_{chatId}",
        async _ => await repository.GetWelcomeAsync(chatId, ct),
        CacheOptions, cancellationToken: ct);

// After
public ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
    => cache.GetOrCreateAsync($"cfg_welcome_{chatId}",
        async factoryCt => await repository.GetWelcomeAsync(chatId, factoryCt),
        CacheOptions, cancellationToken: ct);
```

### Verification

- `dotnet build TelegramGroupsAdmin.sln` — 0 errors / 0 warnings.
- Re-run `ConfigServiceTests` and `ConfigServiceIntegrationTests` — all pass
  (fix is behavior-preserving for non-cancelled paths; no new tests needed —
  cancellation-propagation behavior change is observable only in concurrent-
  cancel scenarios that aren't worth synthesizing).

---

## Item 5 — Add symmetric `DeleteInviteCommand_PreservesWarningSystemSibling` integration test

### Decision

Add the missing symmetric Delete integration test. Do **not** add the
`GetEffective` integration tests for the other 5 configs that the reviewer also
flagged — merge logic is fully covered by 36 unit tests in
`ConfigRepositoryMergeTests.cs`, EF round-trip is covered for all 8 configs in
`ConfigRepositoryIntegrationTests.cs` (`SaveAndGet_<X>_RoundTripPreservesAllFields`
at lines 73, 111, 141, 171, 187, 219, 245, 271), and the `GetEffective` pipeline
is integration-proven once per path shape (Welcome for non-multiplexed,
WarningSystem for multiplexed). Adding 15 more tests would re-exercise identical
code with substituted type names — negative ROI.

The Delete sibling is genuinely high-risk: the multiplexed-column delete writes
back the JSON wrapper without the deleted slot but with the sibling preserved,
and zeros out the column when both slots become null. The existing test covers
WarningSystem-side; InviteCommand-side could fail in isolation.

### Files to modify

`TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigRepositoryIntegrationTests.cs`
— add one method modeled after `DeleteWarningSystem_PreservesInviteCommandSibling`
(~line 547) with the swap.

### Changes

```csharp
[Test]
public async Task DeleteInviteCommand_PreservesWarningSystemSibling()
{
    await _repo!.SaveWarningSystemAsync(TestChat,
        new WarningSystemConfig { AutoBanThreshold = 7, AutoBanReason = "preserve me" });
    await _repo.SaveInviteCommandAsync(TestChat,
        new InviteCommandConfig { Enabled = true, DeleteResponseAfterSeconds = 99 });

    await _repo.DeleteInviteCommandAsync(TestChat);

    var warning = await _repo.GetWarningSystemAsync(TestChat.Id);
    var invite = await _repo.GetInviteCommandAsync(TestChat.Id);

    Assert.Multiple(() =>
    {
        Assert.That(warning, Is.Not.Null, "WarningSystem sibling should remain after InviteCommand delete");
        Assert.That(warning!.AutoBanThreshold, Is.EqualTo(7));
        Assert.That(warning.AutoBanReason, Is.EqualTo("preserve me"));
        Assert.That(invite, Is.Null, "InviteCommand should be deleted");
    });
}
```

### Verification

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
  --filter "FullyQualifiedName~DeleteInviteCommand_PreservesWarningSystemSibling"
```
Expected: passes. Spot-check by intentionally breaking the production code (e.g.,
have `DeleteInviteCommandAsync` set `record.ModerationConfig = null` unconditionally
instead of via the wrapper) and confirm the test fails.

---

## Item 3 — Document multiplexed-column race for issue #196

### Decision

Don't fix today; don't delete the methods (issue #196 is the imminent caller).
Add a forward-pointing code comment that flags the lost-update risk so the #196
implementer sees it before wiring concurrent admin UI saves.

Verified: zero production callers of `SaveWarningSystemAsync`,
`SaveInviteCommandAsync`, `DeleteWarningSystemAsync`, `DeleteInviteCommandAsync`
today. Per CLAUDE.md ("never plan for backwards compatibility unless asked to")
the methods could be deleted, but the tracked imminent caller (#196) makes
re-adding the method bodies later more work than leaving them with a comment now.

### Files to modify

`TelegramGroupsAdmin.Configuration/Repositories/ConfigRepository.cs` —
`SaveWarningSystemAsync` (line 703-724) and `SaveInviteCommandAsync` (line
793-814).

### Change

Add a single XML doc remark (or inline comment block) above each Save method:

```csharp
// CONCURRENCY NOTE (issue #196): When this method is wired up to admin UI,
// the read-modify-write pattern below has a theoretical lost-update window
// for the multiplexed moderation_config JSONB column. Two concurrent admin
// saves to the same chat — one updating WarningSystem, the other updating
// InviteCommand — can race: both read the same baseline wrapper at t=0,
// each writes back its own slot, and the later commit silently drops the
// earlier sibling's update. Single-instance bot + per-circuit admin UI makes
// this rare in practice but not impossible. Mitigations when this matters:
// add a RowVersion/xmin concurrency token to ConfigRecordDto and retry on
// DbUpdateConcurrencyException, or wrap the read+write in a serializable
// transaction.
```

### Verification

Static review only. No code behavior change; no tests needed.

---

## Implementation order

1. **Item 1** — integration test FK fix (must-fix to merge)
2. **Item 2** — component test rewrites (must-fix to merge)
3. **Item 8** — full ContentDetection-through-service migration (~25 sites;
   closes the audit-gap regression). Use `factoryCt` from the start so Item 4
   doesn't need to revisit any new code.
4. **Item 6** — `UrlFiltersConfig.razor` actor cleanup (bundle with Item 8's
   edit to that same file)
5. **Item 4** — fix the 17 existing `_` discard sites in `ConfigService.cs`
6. **Item 5** — symmetric Delete integration test
7. **Item 3** — concurrency comment

After all items: `dotnet test TelegramGroupsAdmin.sln` — confirm 2993+ tests pass
with 0 build warnings. Open PR against `develop` (never `master`).

---

## Memory persistence (post-approval, save when implementation begins)

Save two rules to context-keep before starting Item 8:

1. **`global_feedback_repository_through_service`** — Repositories exist only
   to be called by services. All consumers (razors, hot-path services, jobs)
   go through the service layer so cross-cutting concerns (audit emission,
   cache invalidation) happen in one place. Reads can technically bypass the
   service, but for consistency route them through too.
2. **`global_feedback_context_keep_pause`** — When context-keep MCP is
   unavailable, pause and ask the user to bring it back up before proceeding.
   Do NOT silently fall back to the file-based memory system; context-keep is
   the canonical store and the file system is only a CLAUDE.md bootstrap
   pointer.

---

## End-to-end verification

After all 7 items are merged into the branch:

```bash
# Build clean (TreatWarningsAsErrors=true enforces zero warnings)
dotnet build TelegramGroupsAdmin.sln

# Full test suite (~5-8 min for integration)
dotnet test TelegramGroupsAdmin.sln
# Expected: 2993+ tests pass, 0 fail

# Audit grep — confirm Item 8 surface boundary
grep -rn "IContentDetectionConfigRepository" --include="*.cs" --include="*.razor" \
  --exclude-dir="bin" --exclude-dir="obj"
# Expected: only interface, implementation, DI registration, ConfigService.cs,
# and test files. Zero direct callers in razors or non-Configuration backend services.

# Validate database migrations still apply
dotnet run --project TelegramGroupsAdmin --migrate-only
# Expected: clean exit, no schema drift
```

Then open PR against `develop` per project workflow.

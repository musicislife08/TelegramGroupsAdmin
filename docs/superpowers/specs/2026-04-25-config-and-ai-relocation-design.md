# Config Layer Restoration — ConfigService & AI Services Relocation

**Date:** 2026-04-25
**Scope:** Restore project-layer separation and consolidate config domain ownership.
Relocates `ConfigService` and AI services out of `Core`, expands `IConfigRepository`
into a typed-method contract that owns all data layer concerns, wires the dead
mapping layer for every config flowing through the service, moves bot token
encryption from the service layer into the repository, and adds audit emission on
every config save/delete.
**Folds in:** #341 (`TelegramBotConfigData` mapping wiring), #342 (`ConfigService`
encryption layering), #453 (mapping layer dead for 8 configs).
**Spawns:** #458 (DB-side merge optimization, future work).

## Problem Statement

`TelegramGroupsAdmin` has three open issues (#341, #342, #453) that are all
symptoms of a deeper architectural inversion:

1. `ConfigService` lives at `TelegramGroupsAdmin.Core/Services/ConfigService.cs`
   even though it is a configuration concern. The reason it ended up in `Core`:
   `Core` references `Configuration` (csproj line 31), which is itself the
   inversion — `Core` is supposed to be the lean shared-primitives layer that
   only `Data` sits below.
2. `ConfigService` does data-layer work directly: `JsonSerializer.Serialize<T>(model)`
   bypasses the dead `*ConfigData` DTO and `*ConfigMappings` layers; column
   dispatch (`SetConfigColumn`/`GetConfigColumn`) lives in the service; bot token
   encryption (`IDataProtectionProvider`) is injected into the service layer.
3. The mapping layer (`Configuration/Mappings/*ConfigMappings.cs`) is dead at
   runtime for every config except `ContentDetectionConfig` (which has its own
   wired path). For 7 of the 8 configs flowing through `ConfigService`, the
   mappings either don't exist yet or exist but are never invoked.
4. Bot token methods on `ConfigService` (lines 250-308 of
   `Core/Services/ConfigService.cs`) directly handle encryption — different
   pattern from the four sibling encrypted fields (`ApiKeys`,
   `PassphraseEncrypted`, `VapidPrivateKeyEncrypted`, `UserApiHashEncrypted`)
   which all have encryption at the repository layer (`SystemConfigRepository`).
5. Seven AI service files at `Core/Services/AI/*` (`AIServiceFactory`,
   `AITranslationService`, `IChatService`, `SemanticKernelChatService`,
   `FeatureTestService`, plus their interfaces and value types) also import
   `Configuration.*` namespaces. These are the other half of the
   `Core → Configuration` inversion. They cannot be left in `Core` if we want
   the inversion removed.
6. There is no audit log emitted on config saves. `IAuditService` already exists
   in `Core/Services/AuditService.cs` and is consumed by Razor settings pages
   (`FileScanningSettings.razor`, `EmailInfrastructureSettings.razor`, etc.) but
   `ConfigService` itself never calls it.

## Goals

- **Restore project-layer purity.** `Core → Configuration` reference goes away.
  `Configuration → Core` becomes the legitimate downward edge. No project ref
  cycles in the final state.
- **Establish the boundary rule "services have no data-layer dependencies."**
  Services don't import `Microsoft.EntityFrameworkCore.*`, don't import
  `Microsoft.AspNetCore.DataProtection.*`, don't reference `*ConfigData` DTOs,
  don't call `JsonSerializer` directly. Repositories own that surface end to end.
- **Wire the mapping layer for every config flowing through `ConfigService`.**
  `model.ToData() → JsonSerializer.Serialize` on save; `JsonSerializer.Deserialize → dto.ToModel()`
  on get. This is invoked inside the repository so consumers never see DTOs.
- **Move bot token encryption from the service layer into the repository,**
  matching the existing `SystemConfigRepository.UserApiHash` /
  `VapidPrivateKey` / `ApiKeys` pattern. The bot token stays in its dedicated
  encrypted column for this PR (in-JSON encrypted properties is a separate
  initiative — out of scope here).
- **Type-safe config API.** `ConfigType` enum dispatch and generic
  `GetAsync<T>(ConfigType, long)` / `SaveAsync<T>(ConfigType, ChatIdentity, T)`
  retire entirely. Replaced with typed methods (`GetWelcomeAsync`,
  `SaveWelcomeAsync`, etc.) on both `IConfigRepository` and `IConfigService`.
- **Audit emission on every save and delete.** `ConfigService` injects
  `IAuditService` and emits `AuditEventType.ConfigurationChanged` on every
  mutation, with the `Actor` threaded through method parameters from the caller.
- **Comprehensive test coverage.** Round-trip mapping unit tests for every
  config; per-config merge unit tests; repository integration tests against
  real PostgreSQL (TestContainers) for save/get round-trip and bot token
  encryption verification; `ConfigService` integration test verifying the
  audit_logs table grows on save.

## Non-Goals

- **In-JSON encryption mechanism.** Bot token stays in its dedicated
  `TelegramBotTokenEncrypted` column. The in-JSONB encrypted-property pattern
  is separate future work — would require a new `JsonConverter` or attribute
  marker, recursive scan in `TableExportService.cs`, restore re-encryption
  logic, and a per-field migration for the existing column. Not this PR.
- **DB-side merge optimization.** Effective-config per-field merge stays in
  app code (one merge implementation per config in `ConfigRepository`).
  Tracked separately in #458 — that work moves the merge into PostgreSQL via
  JSONB operators or per-config views.
- **Splitting `Configs.Moderation` into separate columns.** `WarningSystemConfig`
  and `InviteCommandConfig` stay multiplexed in the `Moderation` JSON column
  via a `ModerationConfigData` wrapper DTO. Splitting requires a schema
  migration and is not justified by the architectural concerns here.
- **Backwards compatibility for the `ConfigType` enum.** It retires entirely.
  All ~30+ call sites migrate to typed methods. No transitional support.
- **AI services internal refactors.** AI services relocate from `Core/Services/AI/`
  to a new `TelegramGroupsAdmin.AI` project but their internal logic does not
  change. Behavior preserved.

## Target Architecture

### Project Dependency Graph (Final State)

```
Data            → (no project refs, NuGet only)
Core            → Data                                — primitives: ChatIdentity, value types, IAuditService, AuditLog*
Configuration   → Core, Data                           — IConfigService + ConfigService + IConfigRepository + ConfigRepository + DTOs + Mappings + AI configs
AI              → Configuration, Core, Data            — NEW project. AI service factories, translation, chat completion, feature tests.
ContentDetection → Configuration, Core, Data           (peer of AI/BackgroundJobs)
BackgroundJobs  → Configuration, Core, Data            (peer)
Telegram        → AI, ContentDetection, BackgroundJobs, Configuration, Core, Data
Host            → all of the above
```

### Layering Rules (Enforced)

1. References point downward only — no horizontal or upward references.
2. `Core` no longer references `Configuration`.
3. Each project keeps interfaces and implementations co-located in its own
   domain.
4. `Configuration` is the single home for all configuration concerns: services,
   repositories, models, DTOs, mappings, AI configuration models.
5. `AI` is the home for AI service abstractions and implementations
   (factory, translation, chat completion, feature tests).

### Boundary Rules (Enforced)

1. Services have no `Microsoft.EntityFrameworkCore.*` usings.
2. Services have no `Microsoft.AspNetCore.DataProtection.*` usings — encryption
   is repo-internal.
3. Services have no `*ConfigData` DTO references — only domain models.
4. Repository public methods accept and return domain models only. DTOs and
   mappings are repo-internal implementation details.
5. JSON serialization, DTO mapping, encryption, column dispatch, and per-field
   merge all happen inside the repository.

### Identity-Object Threading Rule

- **Mutations** (`Save*`, `Delete*`) take `ChatIdentity` for log context. Repository
  pulls `chat.Id` for the SQL filter and uses `chat.DisplayName` only in info-level
  log lines. Service emits an audit event using `chat.DisplayName` as identifying
  context.
- **Reads** (`Get*`, `GetEffective*`) take primitive `long chatId`. Reads do not
  audit and should not info-log; forcing callers to construct or fetch a full
  `ChatIdentity` is wasted work for context that is never used.
- **Only the Data project is exempt from rich identity threading** (its logging
  is debug-level and operates on raw column values).

The compiler enforces this rule by signature: a save site cannot accidentally
pass an under-populated identity, because the type system requires `ChatIdentity`.

## Domain Surface

### `IConfigRepository` (Configuration project)

```csharp
public interface IConfigRepository
{
    // --- Reads: primitive long, no audit, no info logs ---
    ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default);
    ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default);

    ValueTask<LogConfig?> GetLogAsync(long chatId, CancellationToken ct = default);
    ValueTask<LogConfig?> GetEffectiveLogAsync(long chatId, CancellationToken ct = default);

    ValueTask<BotProtectionConfig?> GetBotProtectionAsync(long chatId, CancellationToken ct = default);
    ValueTask<BotProtectionConfig?> GetEffectiveBotProtectionAsync(long chatId, CancellationToken ct = default);

    ValueTask<TelegramBotConfig?> GetTelegramBotAsync(long chatId, CancellationToken ct = default);
    ValueTask<TelegramBotConfig?> GetEffectiveTelegramBotAsync(long chatId, CancellationToken ct = default);

    ValueTask<ServiceMessageDeletionConfig?> GetServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);
    ValueTask<ServiceMessageDeletionConfig?> GetEffectiveServiceMessageDeletionAsync(long chatId, CancellationToken ct = default);

    ValueTask<WarningSystemConfig?> GetWarningSystemAsync(long chatId, CancellationToken ct = default);
    ValueTask<WarningSystemConfig?> GetEffectiveWarningSystemAsync(long chatId, CancellationToken ct = default);

    ValueTask<InviteCommandConfig?> GetInviteCommandAsync(long chatId, CancellationToken ct = default);
    ValueTask<InviteCommandConfig?> GetEffectiveInviteCommandAsync(long chatId, CancellationToken ct = default);

    ValueTask<BanCelebrationConfig?> GetBanCelebrationAsync(long chatId, CancellationToken ct = default);
    ValueTask<BanCelebrationConfig?> GetEffectiveBanCelebrationAsync(long chatId, CancellationToken ct = default);

    // --- Mutations: ChatIdentity for log context, repo pulls .Id internally ---
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, CancellationToken ct = default);

    // ... same Save/Delete shape for the other 7 configs ...

    // --- Bot token (encrypted, no chat scope, truly global) ---
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, CancellationToken ct = default);
}
```

### `IConfigService` (Configuration project)

```csharp
public interface IConfigService
{
    // --- Reads: primitive long ---
    ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default);
    ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default);

    // ... same for the other 7 configs ...

    // --- Mutations: ChatIdentity + Actor (audit + info log) ---
    Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, Actor initiator, CancellationToken ct = default);
    Task DeleteWelcomeAsync(ChatIdentity chat, Actor initiator, CancellationToken ct = default);

    // ... same for the other 7 configs ...

    // --- Bot token ---
    ValueTask<string?> GetBotTokenAsync(CancellationToken ct = default);
    Task SaveBotTokenAsync(string botToken, Actor initiator, CancellationToken ct = default);

    // --- ContentDetection helpers (delegate to IContentDetectionConfigRepository, retained) ---
    Task<IEnumerable<ChatConfigInfo>> GetAllContentDetectionConfigsAsync(CancellationToken ct = default);
    Task<HashSet<string>> GetCriticalCheckNamesAsync(long chatId, CancellationToken ct = default);
}
```

### Retired Surface

- `ConfigType` enum (no caller routing on it remains).
- `IConfigService.GetAsync<T>(ConfigType, long chatId)` and `SaveAsync<T>(ConfigType, ChatIdentity, T)`.
- `IConfigService.DeleteAsync(ConfigType, ChatIdentity)`.
- `IConfigRepository.GetAsync(long chatId)` and `UpsertAsync(ConfigRecordDto record)`
  (the anemic CRUD shape).
- `ConfigService.SetConfigColumn` / `GetConfigColumn` switch dispatch (replaced
  by typed column access inside the repo).
- `ConfigService.MergeConfigs<T>` reflection helper (replaced by per-config
  typed merge methods on the repo).
- `ConfigService` constructor dependency on `IDataProtectionProvider` (encryption
  moves to the repo).
- `ConfigRecord.cs` (was unused; deleted in cleanup commit).

## Repository Internals

`ConfigRepository` constructor:

```csharp
public ConfigRepository(
    IDbContextFactory<AppDbContext> contextFactory,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigRepository> logger)
```

### Save Method Template

```csharp
public async Task SaveWelcomeAsync(ChatIdentity chat, WelcomeConfig config, CancellationToken ct = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(ct);

    var dto = config.ToData();                                // mapping (internal)
    var json = JsonSerializer.Serialize(dto, _jsonOptions);   // serialization (internal)

    var record = await context.Configs.FirstOrDefaultAsync(c => c.ChatId == chat.Id, ct)
                 ?? new ConfigRecordDto { ChatId = chat.Id, CreatedAt = DateTimeOffset.UtcNow };
    record.Welcome = json;
    record.UpdatedAt = DateTimeOffset.UtcNow;

    if (context.Entry(record).State == EntityState.Detached)
        await context.Configs.AddAsync(record, ct);

    await context.SaveChangesAsync(ct);
    _logger.LogInformation("Saved Welcome config for {Chat}", chat.DisplayName);
}
```

### Get Method Template

```csharp
public async ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(ct);
    var json = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == chatId)
        .Select(c => c.Welcome)
        .FirstOrDefaultAsync(ct);

    if (json is null) return null;

    try
    {
        var dto = JsonSerializer.Deserialize<WelcomeConfigData>(json, _jsonOptions);
        return dto?.ToModel();
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Failed to deserialize Welcome config for chat {ChatId}", chatId);
        return null;
    }
}
```

### Effective Method Template (One DB Roundtrip, App-Side Merge)

```csharp
public async ValueTask<WelcomeConfig?> GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(ct);

    // Single roundtrip — projected select returns global + chat rows
    var rows = await context.Configs
        .AsNoTracking()
        .Where(c => c.ChatId == 0 || c.ChatId == chatId)
        .Select(c => new { c.ChatId, c.Welcome })
        .ToListAsync(ct);

    var globalJson = rows.FirstOrDefault(r => r.ChatId == 0)?.Welcome;
    var chatJson = chatId == 0 ? null : rows.FirstOrDefault(r => r.ChatId == chatId)?.Welcome;

    var globalModel = globalJson is null
        ? null
        : JsonSerializer.Deserialize<WelcomeConfigData>(globalJson, _jsonOptions)?.ToModel();
    var chatModel = chatJson is null
        ? null
        : JsonSerializer.Deserialize<WelcomeConfigData>(chatJson, _jsonOptions)?.ToModel();

    return MergeWelcome(globalModel, chatModel);
}

private static WelcomeConfig? MergeWelcome(WelcomeConfig? global, WelcomeConfig? chat)
{
    if (chat is null) return global;
    if (global is null) return chat;
    // Per-config merge: chat's non-default values override global; chat's null/default fields fall through.
    // Each config has its own typed merge implementation matching current MergeConfigs<T> reflection behavior.
    return /* ... per-config merge ... */;
}
```

### Bot Token Methods

`GetBotTokenAsync` and `SaveBotTokenAsync` follow the existing
`SystemConfigRepository.GetUserApiHashAsync` / `SetUserApiHashAsync` pattern
exactly: read/write the `TelegramBotTokenEncrypted` column directly and
encrypt/decrypt with `_dataProtectionProvider.CreateProtector(DataProtectionPurposes.TelegramBotToken)`.
The cipher format and `DataProtectionPurpose` constant do not change, so
existing encrypted tokens in production continue to decrypt without migration.

## Service Internals

`ConfigService` constructor:

```csharp
public ConfigService(
    IConfigRepository repository,
    IContentDetectionConfigRepository contentDetectionRepository,
    IAuditService auditService,
    HybridCache cache,
    ILogger<ConfigService> logger)
```

### Save Method Template

```csharp
public async Task SaveWelcomeAsync(
    ChatIdentity chat,
    WelcomeConfig config,
    Actor initiator,
    CancellationToken ct = default)
{
    await _repository.SaveWelcomeAsync(chat, config, ct);
    await _auditService.LogEventAsync(
        AuditEventType.ConfigurationChanged,
        initiator,
        target: null,
        value: $"Welcome ({chat.DisplayName})",
        ct);
    await _cache.RemoveAsync($"cfg_welcome_{chat.Id}", ct);
    if (chat.Id != 0)
        await _cache.RemoveAsync($"cfg_effective_welcome_{chat.Id}", ct);
    else
        await _cache.RemoveByTagAsync("effective_welcome", ct);
    _logger.LogInformation("Welcome config saved for {Chat} by {Actor}", chat.DisplayName, initiator);
}
```

### Get Method Template

```csharp
public async ValueTask<WelcomeConfig?> GetWelcomeAsync(long chatId, CancellationToken ct = default)
{
    return await _cache.GetOrCreateAsync(
        $"cfg_welcome_{chatId}",
        async _ => await _repository.GetWelcomeAsync(chatId, ct),
        CacheOptions,
        cancellationToken: ct);
}
```

### Audit Event Type

All config saves and deletes emit `AuditEventType.ConfigurationChanged` (#28).
The `value` field carries a human-readable identifier (config name + chat
display name). Future work could refine this to per-config event types from
the existing reserved settings range (20-29) — out of scope here.

### Actor Sourcing

- **UI callers** (Razor components): inject `ICurrentUserService` (or equivalent
  existing surface) and pass `Actor.FromCurrentUser()` or whatever the existing
  pattern is in `IAuditHandler` callers.
- **Background callers** (jobs, message handlers triggered by Telegram updates):
  pass a predefined system actor constant (`Actor.System`, `Actor.BackgroundJob`,
  or whatever the existing constants are).

The implementation plan will inventory each call site and identify the correct
Actor source. No call site is allowed to pass `null` or construct an
under-populated actor.

## Test Strategy

### Layer 1 — Unit Tests (`TelegramGroupsAdmin.UnitTests`)

For each of the 8 configs:

- `*ConfigMappingsTests` — round-trip test: `model → ToData() → JsonSerializer.Serialize → JsonSerializer.Deserialize → ToModel()` returns a model equal to the original. Add 7 new test files (`WelcomeConfigMappingsTests` already exists). One test per config: ~8 unit tests.
- `ConfigRepository.MergeXxxTests` — per-config merge tested in isolation:
  only-global, only-chat, both-with-overrides, both-with-default-fallthrough,
  partial-chat-with-explicit-nulls. ~5 cases per config × 8 configs ≈ 40 tests.
- `ConfigServiceTests` — typed-method tests with mocked `IConfigRepository` and
  `IAuditService`:
  - Save path: verify `repo.SaveXxxAsync(chat, config)` is called with the
    right args; verify `auditService.LogEventAsync(ConfigurationChanged, actor, ...)`
    fires; verify cache invalidation key + tag removal.
  - Get path: verify cache hit returns cached without hitting repo; verify
    cache miss calls repo + caches.
  - Delete path: verify both repo invocation and audit emission.

### Layer 2 — Integration Tests (`TelegramGroupsAdmin.IntegrationTests`)

Real PostgreSQL via the existing TestContainers fixture:

- `ConfigRepositoryIntegrationTests`:
  - `SaveAndGet_RoundTrip_Preserves<Config>` — for each of the 8 configs.
  - `GetEffective_<Config>_<Scenario>` — only-global, only-chat-without-global,
    both-merge-applied, partial-chat-with-fallthrough.
  - `SaveBotToken_RoundTrip_Encrypted` — verify column stores ciphertext (assert
    not equal to plain token); verify get returns decrypted plain text.
- `ConfigServiceIntegrationTests`:
  - `Save<Config>_EmitsAuditEvent` — full path: real `ConfigService` → real
    `ConfigRepository` → real `AuditService` → assert `audit_logs` table has
    expected row. One per config.

### Layer 3 — Component Tests (`TelegramGroupsAdmin.ComponentTests`)

~6-8 existing component tests (`WelcomeSystemConfigTests`,
`BotGeneralSettingsTests`, `BanCelebrationSettingsTests`,
`ServiceMessageDeletionSettingsTests`, etc.) need their `IConfigService` mocks
updated from `cfg.GetAsync<WelcomeConfig>(...)` shape to `cfg.GetWelcomeAsync(...)`
shape. Mechanical sweep, no behavior change to assert on.

### Layer 4 — E2E Tests (`TelegramGroupsAdmin.E2ETests`)

No new tests. Existing tests (`ExamFlowE2ETests`, `AuditLogTests`, etc.) go
through the UI which uses `IConfigService` indirectly; they continue to work
as long as DI resolution still produces a working `IConfigService`. Smoke-pass
run before merge.

### Test Inventory

| Layer | Files added | Files modified | Approx tests added |
|---|---|---|---|
| Unit | 9 (mappings + merge + service) | 1 (existing `WelcomeConfigMappingsTests`) | ~80 |
| Integration | 2 (`ConfigRepositoryIntegrationTests`, `ConfigServiceIntegrationTests`) | 0 | ~30 |
| Component | 0 | ~6-8 | 0 (mock updates only) |
| E2E | 0 | 0 | 0 |

### What This Catches

1. **The bug that motivated #453** — "mapping exists but is never invoked" —
   caught by integration round-trip tests that go through the actual
   `ConfigRepository.SaveWelcomeAsync` → `repo.GetWelcomeAsync` and would fail
   if the mapper were skipped.
2. **Audit-skipped-on-save regressions** — caught by integration test asserting
   `audit_logs` grows.
3. **Cache invalidation regressions** — caught by `ConfigService` unit tests
   verifying invalidation calls.
4. **Bot token encryption regressions** — caught by integration test asserting
   ciphertext at rest and decrypted plain text on read.

## Commit Sequence

Single PR, 7 commits. Bottom-up layered. Intermediate commits may not build
(per the user's "intermediate-broken-OK" rule); final state is green.

| # | Commit | Build state |
|---|---|---|
| 1 | `chore(ai): scaffold TelegramGroupsAdmin.AI project` — new csproj, sln entry, brief CLAUDE.md, NuGet refs (Microsoft.SemanticKernel + others), project refs (Configuration, Core, Data). Empty otherwise. | green |
| 2 | `refactor(ai): relocate AI services from Core to AI project` — move 7 service files + 6 value-type companions from `Core/Services/AI/` → `AI/Services/`. Namespace flip. AI's `ServiceCollectionExtensions` added; AI registrations removed from Core's. `Microsoft.SemanticKernel` package moves from Core's csproj to AI's. | broken (consumers still import old namespace) |
| 3 | `feat(config): add missing DTOs and mappings + unit tests` — 3 new `*ConfigData` types (WarningSystem, InviteCommand, BanCelebration) + `ModerationConfigData` wrapper; 7 new `*ConfigMappings.cs`; 8 round-trip mapping unit test classes; per-config merge unit tests. | green (additions only) |
| 4 | `refactor(config): expand IConfigRepository with typed methods + integration tests` — typed methods on the repo (Get/GetEffective/Save/Delete per config + bot token methods); per-config merge moved from `ConfigService` into the repo; integration tests for save/get round-trip per config + bot token encryption verification. Old anemic methods stay temporarily for ConfigService's continued use. | green |
| 5 | `refactor(config): flip Core ↔ Configuration project edges and relocate ConfigService` — Add `Configuration → Core` ref; remove `Core → Configuration` ref; add `Microsoft.Extensions.Caching.Hybrid` package to Configuration; move `IConfigService.cs` + `ConfigService.cs` from `Core/Services/` → `Configuration/Services/`. Namespace flip. ConfigService body NOT yet rewritten — still uses old generic API. | broken (consumers still import old namespace + old generic surface) |
| 6 | `refactor(config): rewire ConfigService to typed surface with audit, update consumers` — biggest commit. ConfigService body rewrite (typed methods delegating to typed repo + audit emission + cache invalidation per config). `IConfigService` typed methods replace generic. ConfigService's `IDataProtectionProvider` constructor dependency dropped. DI registrations move (`IConfigService` to Configuration's, AI services to AI's). Sweep ~30+ consumers: `using` updates + call-site migrations + Actor sourcing. ConfigService unit + integration tests added. Component test mocks updated (~6-8 files). | green (PR buildable from here) |
| 7 | `chore(config): retire dead config types and column-routing scaffolding` — delete `ConfigType` enum, `ConfigRecord.cs`, old `IConfigRepository` anemic methods, any `*ConfigData`/`*ConfigMappings` files that ended up unused. Smoke-pass E2E suite. | green |

### Risk Notes

- **Commit 2 risk:** AI services migration — currently functional, just relocating.
  Worth a `dotnet run --migrate-only` smoke-pass after commit 6 to confirm DI
  resolution still works for bot startup.
- **Commit 6 risk:** bot token encryption migration — cipher format and
  `DataProtectionPurposes.TelegramBotToken` constant don't change, so existing
  encrypted tokens in production keep working. No data migration needed.
- **Commit 6 risk:** 30+ consumer call-site updates have a real chance of one
  being missed or miscompiled. Build + integration test suite is the safety net.

## Acceptance Criteria

- [ ] `Core/TelegramGroupsAdmin.Core.csproj` no longer references
      `TelegramGroupsAdmin.Configuration`.
- [ ] `Configuration/TelegramGroupsAdmin.Configuration.csproj` references
      `TelegramGroupsAdmin.Core`.
- [ ] New `TelegramGroupsAdmin.AI` project exists at the peer level with
      ContentDetection / BackgroundJobs.
- [ ] `IConfigService` and `ConfigService` live at
      `TelegramGroupsAdmin.Configuration/Services/`.
- [ ] All 7 AI service files live at `TelegramGroupsAdmin.AI/Services/`.
- [ ] `IConfigRepository` exposes typed methods per config (Get, GetEffective,
      Save, Delete) plus `GetBotTokenAsync`/`SaveBotTokenAsync`. No generic
      `GetAsync(chatId)`/`UpsertAsync(record)` methods remain.
- [ ] `ConfigService` constructor no longer injects `IDataProtectionProvider`.
- [ ] `ConfigService` constructor injects `IAuditService`.
- [ ] `ConfigType` enum is deleted.
- [ ] All 8 configs flow through their `*ConfigMappings.ToData()` /
      `ToModel()` at runtime (verified by integration tests).
- [ ] Every save/delete on `IConfigService` emits an `AuditEventType.ConfigurationChanged`
      audit event with the threaded `Actor`.
- [ ] All 8 configs have round-trip unit tests for their mapping pairs.
- [ ] All 8 configs have integration tests covering save/get round-trip against
      real PostgreSQL.
- [ ] Bot token integration test verifies encryption at rest (ciphertext != plain)
      and round-trip decryption.
- [ ] Pre-existing E2E test suite passes without modification.
- [ ] Issues #341, #342, #453 close on PR merge.
- [ ] PR final state builds green; intermediate commits may be broken.

## Folds and Spawns

- Closes #341 (`refactor: TelegramBotConfigData is unused — wire up proper ToModel/ToDto mapping`).
- Closes #342 (`refactor: ConfigService bypasses repository layer for bot token and encrypted config access`).
- Closes #453 (`refactor: wire ConfigService through the Data-DTO/Mapping layer for Welcome + siblings`).
- Spawns / references #458 (`perf: move ConfigRepository per-field effective-config merge from app code into PostgreSQL`).

Future work (separate spec, not this PR):
- In-JSONB encrypted property mechanism for the five existing encrypted columns
  (`ApiKeys`, `PassphraseEncrypted`, `TelegramBotTokenEncrypted`,
  `VapidPrivateKeyEncrypted`, `UserApiHashEncrypted`) — design new `JsonConverter`
  or attribute marker, recursive scan in `TableExportService`, restore
  re-encryption logic, per-field migrations.
- AI services full restructuring beyond relocation (if/when warranted).
- Splitting `Configs.Moderation` into separate columns for `WarningSystemConfig`
  and `InviteCommandConfig`.
- Per-config audit event types from the reserved range 20-29 instead of the
  generic `ConfigurationChanged`.

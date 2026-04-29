# Canonical Golden Snapshot + Template DB Cloning

**Date:** 2026-04-29
**Branch:** `refactor/golden-canonical-snapshot-and-templating`
**Issues:** Closes #462, Closes #463
**Scope:** Replace today's three competing integration-test seeding strategies
with a single canonical superset cloned per-test from a Postgres template
database. Establishes a strict three-method model on `MigrationTestHelper`
(canonical clone / empty clone / fresh+migrate), introduces a subtractive
fluent reducer for tests that need a constrained shape from canonical, and
restructures `TestData/SQL` from scenario-based files to per-table files. Both
issues ship together because they belong together: #462 establishes "canonical
is the only seed shape" and #463 makes "load it once, clone the rest" possible.
Without #463, the larger canonical would slow integration tests during the
migration phases before Phase 4 cleanup.
**PR target:** `develop` (per project workflow).

---

## Context

Today's `TelegramGroupsAdmin.IntegrationTests` project has accumulated three
competing seed strategies:

1. Tests using `GoldenDataset.Seed*Async` correctly and referencing its
   constants.
2. Tests calling `GoldenDataset.SeedAsync` and then adding extra inline rows
   via `context.Set<X>().Add(new X { ... })` or raw `INSERT INTO`.
3. Tests bypassing `GoldenDataset` entirely and hand-rolling users, chats, and
   messages with arbitrary IDs.

The result: inconsistent data shapes across the suite, brittle assertions
that pass against contrived state, and silent drift when a test author
copies a Golden ID as a literal instead of referencing the constant.

**Today's actual fixture model:** every test creates a fresh database via
`new MigrationTestHelper()` + `CreateDatabaseAndApplyMigrationsAsync()`
(CREATE DATABASE + `Database.MigrateAsync()`), then drops it on `Dispose()`.
There is no TRUNCATE-based shared-container model. `PostgresFixture` only
starts the container; `MigrationTestHelper` does the per-test work.

A pre-design audit (4 parallel agents on the four test projects, plus a
follow-up classifier on every IntegrationTests file) confirmed:

- The 18-file remediation table from #462's body is mostly accurate, but two
  additional files need migration: `Repositories/AnalyticsRepositoryTests.cs`
  (calls retired `SeedWithoutTrainingDataAsync` + `SeedAnalyticsDataAsync`)
  and `Configuration/ConfigServiceIntegrationTests.cs` (calls partial-seed
  `SeedWebUsersOnlyAsync`).
- Across all 53 IntegrationTests classes: **10 canonical-consumers** (call
  some `GoldenDataset.Seed*Async` today), **35 empty-consumers** (start from
  empty, assemble state via `Add`/`INSERT`/SUT writes), **8 migration-tests**
  (apply migrations one-at-a-time).
- UnitTests, ComponentTests, and E2ETests are mostly clean of canonical
  collision risks. Three low-priority constant-hygiene items surfaced are
  deferred to follow-up issues.

**The 35:10 split changes the framing.** There is no "default" data shape.
Each test class explicitly picks via the `MigrationTestHelper` method it
calls. Empty is not an escape hatch; it's the larger consumer group.

---

## Goals

- Single canonical superset of test data, structurally enforced (no fourth
  bucket besides canonical / empty / migration-test).
- Fluent reducer API for tests that need a constrained shape from canonical.
- Per-test database cloned from a session-built `golden_template` or
  `empty_template`, replacing today's `CREATE DATABASE` + `MigrateAsync`
  per-test cost. Per-test setup drops to ~50–150ms regardless of canonical
  size.
- SQL fixtures organized strictly by table, FK-ordered, in a `canonical/`
  subfolder. Migration-test-only fixtures isolated under `migration/`.
- Migration tests untouched in setup mechanism — they keep their existing
  `MigrationTestHelper` methods and adopt `GoldenDataset.*` constants for
  ID literals only.
- Each commit in the implementation phase keeps build and test suite green
  so bisect lands on a meaningful state.

## Non-goals

- Refactoring migration tests onto the template-clone fast path (deliberately
  deferred — see "Migration tests deliberately untouched" below).
- Cross-project constant-hygiene cleanup (deferred to follow-up issues).
- Adding speculative reducers (`KeepInvites`, `KeepWebNotifications`,
  `KeepUsernameHistory`, `PruneUserActions`) that no current test requires.
  YAGNI applies; add when first consumer arrives.
- Migrating UnitTests / ComponentTests / E2ETests off any existing patterns.

---

## Architecture

### Three explicit paths via `MigrationTestHelper`

Each test class declares its data-shape intent by which `Create*` method it
calls. There is no NUnit attribute selection, no implicit default.

**Path 1 — Canonical** (`CreateDatabaseFromGoldenTemplateAsync()`).
Per-test database cloned from `golden_template`. Test code does no
seed-related work in `[SetUp]`; canonical is already there.

```csharp
[TestFixture]
public class FooTests
{
    private MigrationTestHelper? _testHelper;

    [SetUp]
    public async Task Setup()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        // DB has full canonical, ready to use
    }

    [TearDown]
    public void TearDown() => _testHelper?.Dispose();
}
```

**Path 1b — Canonical with reductions** (same method + `Reduce(...)` for ML
threshold tests, etc.).

```csharp
[SetUp]
public async Task Setup()
{
    _testHelper = new MigrationTestHelper();
    await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

    using var ctx = _testHelper.GetDbContext();
    await GoldenDataset.Reduce(ctx)
        .KeepSpam(5)
        .KeepHam(5)
        .ApplyAsync();
}
```

**Path 2 — Empty** (`CreateDatabaseFromEmptyTemplateAsync()`). Per-test
database cloned from `empty_template` (migrations only, no data). Tests
freely assemble their own state via `Add` / `INSERT` / SUT writes — that's
the entire point of the empty path.

```csharp
[SetUp]
public async Task Setup()
{
    _testHelper = new MigrationTestHelper();
    await _testHelper.CreateDatabaseFromEmptyTemplateAsync();
    // Clean migrated DB. Test assembles state as needed.
}
```

**Path 3 — Migration tests.** Stay on existing `MigrationTestHelper` methods
(`CreateDatabaseAndApplyMigrationsAsync` for HEAD-schema, `CreateDatabaseAndMigrateToAsync(targetMigration)`
for intermediate-schema). Migration tests adopt `GoldenDataset.*` constants
for ID literals in their inline data.

### Selection rule for reductions

Count-only, deterministic by id-order. `KeepSpam(5)` keeps the 5 training
labels with `Class=Spam` having the lowest `message_id`; the rest are deleted.
Same model for every reducer slice — lowest-id wins.

No predicate selectors. No per-row content selection.

### Boundary rule (code-review enforced)

The boundary rule applies **only to canonical-consumer tests** (those calling
`CreateDatabaseFromGoldenTemplateAsync`). Such a test **never** calls
`Add(new SomeDto { ... })` followed by `SaveChangesAsync`, and **never**
runs raw `INSERT INTO` in setup. The cloned canonical state plus an optional
`Reduce` plan are the only modification paths for these tests. If they need
data not in canonical, the options are:

- Drive writes through the system under test (the repo or service the test
  exercises), or
- Migrate the data into `canonical/*.sql` so every canonical-consumer gets it.

**Empty-consumer tests** (those calling `CreateDatabaseFromEmptyTemplateAsync`)
have no boundary rule — they start empty and may freely `Add`/`INSERT`/SUT-write
to build the state they need. That's the difference between the two paths.

---

## SQL fixture layout

### Final structure

```
TelegramGroupsAdmin.IntegrationTests/TestData/SQL/
  canonical/                              ← loaded once into golden_template
    01_web_users.sql
    02_telegram_users.sql
    03_managed_chats.sql
    04_telegram_user_mappings.sql
    05_linked_channels.sql
    06_chat_admins.sql
    07_messages.sql
    08_message_edits.sql
    09_user_actions.sql
    10_detection_results.sql
    11_content_detection_configs.sql
    12_training_labels.sql
    13_username_history.sql
    14_invites.sql
    15_web_notifications.sql

  migration/                              ← never loaded by canonical path
    40_pre_migration_impersonation_alerts.sql
```

The `.csproj` `EmbeddedResource` glob is updated to recursive
(`<EmbeddedResource Include="TestData\SQL\**\*.sql" />`) so subfolder
contents are included. Loading order under `canonical/` follows lexicographic
filename order, FK-correct by construction.

### Encrypted columns excluded from canonical

`api_keys` and any other DataProtection-encrypted columns are **not** seeded
into `canonical/*.sql`. Every test today encrypts these inline using a
per-test `IDataProtectionProvider`; we preserve that pattern via
`PostgresFixture.SharedDataProtectionProvider` (see Type surface below).
Tests that need encrypted config rows assemble them post-clone, using the
shared provider through DI registration.

### What absorbs what

`canonical/02_telegram_users.sql` absorbs the rows currently spread across
`00_base_telegram_users.sql`, the dedup-author rows in `30_dedup_test_data.sql`,
the old-message-author rows in `60_old_messages.sql`, and the analytics-author
rows in `50_analytics_test_data.sql`.

`canonical/07_messages.sql` absorbs `04_base_messages.sql` (11 base messages),
`30_dedup_test_data.sql` (22 dedup messages at IDs `95001`–`95022`),
`60_old_messages.sql` (6 old messages at IDs `96001`–`96006`), and the
temporal messages from `50_analytics_test_data.sql`.

`canonical/09_user_actions.sql` is new and absorbs the analytics user_action
rows from `50_analytics_test_data.sql` plus baseline kick/welcome history.

**Canonical contract for `09_user_actions.sql`:** every seeded row has a
non-null `MessageId` (and corresponding `ChatId`). This guarantees that any
`user_action` row a test sees with a null `MessageId` was created by a prior
`KeepMessages` reduction's SetNull cascade — a clean signal for any future
`Prune*` cleanup (out of scope for v1).

`canonical/12_training_labels.sql` is 100 spam + 100 ham labeled messages,
replacing `MLTrainingData.sql`, `10_training_minimal.sql`, `11_training_full.sql`,
`20_unbalanced_100_20.sql`, and `21_unbalanced_20_100.sql`. The 100/100 count
is the superset for every ML threshold test's reduction needs.

`canonical/06_chat_admins.sql`, `08_message_edits.sql`, `13_username_history.sql`,
`14_invites.sql`, `15_web_notifications.sql` are new files. `08_message_edits.sql`
specifically extracts the inline `INSERT INTO message_edits` rows currently
embedded in `BackupServiceTests.cs`.

### Files deleted (Phase 4)

```
00_base_telegram_users.sql        → folded into canonical/02_telegram_users.sql
02_base_managed_chats.sql         → folded into canonical/03_managed_chats.sql
03_base_linked_channels.sql       → folded into canonical/05_linked_channels.sql
04_base_messages.sql              → folded into canonical/07_messages.sql
05_base_detection_results.sql     → folded into canonical/10_detection_results.sql
06_base_content_detection_configs → folded into canonical/11_*
07_base_telegram_user_mappings    → folded into canonical/04_*
10_training_minimal.sql           → superseded by canonical/12_training_labels.sql
11_training_full.sql              → superseded
20_unbalanced_100_20.sql          → superseded
21_unbalanced_20_100.sql          → superseded
30_dedup_test_data.sql            → folded into canonical/02 + canonical/07
50_analytics_test_data.sql        → folded into canonical/07 + canonical/09
60_old_messages.sql               → folded into canonical/07
MLTrainingData.sql                → folded into canonical/12
```

---

## Type surface

### `GoldenDataset` additions

```csharp
namespace TelegramGroupsAdmin.IntegrationTests.TestData;

public static partial class GoldenDataset
{
    // Public per-test API: returns a builder for tests that need a constrained
    // shape from canonical. Synchronous factory; no DB hit until ApplyAsync.
    public static GoldenReducePlan Reduce(AppDbContext context);

    // Internal-ish: loads canonical/*.sql into the target context.
    // Used by PostgresFixture in [OneTimeSetUp] to build golden_template.
    // Used by GoldenReducePlanTests to exercise Reduce against a manually-
    // loaded canonical state before the template infrastructure exists.
    // Not intended for production test setup.
    public static Task LoadCanonicalAsync(AppDbContext context, IDataProtectionProvider dataProtection, CancellationToken ct = default);
}

public sealed class GoldenReducePlan
{
    GoldenReducePlan KeepSpam(int count);
    GoldenReducePlan KeepHam(int count);
    GoldenReducePlan KeepDetectionResults(int count);
    GoldenReducePlan KeepMessages(int count);
    GoldenReducePlan KeepUserActions(int count);

    Task ApplyAsync(CancellationToken ct = default);
}
```

### `MigrationTestHelper` additions

```csharp
public class MigrationTestHelper : IDisposable
{
    // Existing API (unchanged):
    public Task CreateDatabaseAndApplyMigrationsAsync();
    public Task CreateDatabaseAndMigrateToAsync(string targetMigration);
    public AppDbContext GetDbContext();
    public Task ExecuteSqlAsync(string sql);
    public Task<object?> ExecuteScalarAsync(string sql);
    public Task<T?> ExecuteScalarAsync<T>(string sql);
    public Task ApplyNextMigrationAsync(string migrationName);

    // NEW (Phase 2):
    public Task CreateDatabaseFromGoldenTemplateAsync();
    public Task CreateDatabaseFromEmptyTemplateAsync();
}
```

Both new methods do the same `CREATE DATABASE … TEMPLATE` work, differing only
in which template they clone (`golden_template` vs `empty_template`). They
require the templates to have been built by `PostgresFixture.[OneTimeSetUp]`.

### `PostgresFixture` additions

```csharp
public class PostgresFixture
{
    // Existing:
    public static string BaseConnectionString { get; private set; }
    public static string GetUniqueDatabaseName();

    // NEW (Phase 1 — exposed for use by canonical-consumer tests):
    public static IDataProtectionProvider SharedDataProtectionProvider { get; }
        = new EphemeralDataProtectionProvider();

    // [OneTimeSetUp] expanded in Phase 2 to build templates (see fixture flow)
}
```

`SharedDataProtectionProvider` is constructed once per test session. Lives
in-memory; no temp files, no cleanup. The canonical fixture and any test that
encrypts/decrypts config data both use this single instance, so encrypted
values written by one path can be read by another. Tests that today create
their own `services.AddDataProtection().PersistKeysToFileSystem(... fresh GUID ...)`
swap to `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`
during Phase 3 migration.

### Constants kept and extended

`GoldenDataset.TelegramUsers.UserN_TelegramUserId` (1–7),
`GoldenDataset.Users.UserN_Id` (web user GUIDs),
`GoldenDataset.ManagedChats.MainChat_Id` and siblings — all preserved
unchanged. New constants added only if a new fixture introduces an identifier
tests need to assert against (e.g., `Invites.PendingInvite_Id` if `14_invites.sql`
introduces a row referenced by ID in test assertions).

### Methods retired (Phase 4)

From `GoldenDataset`:

- `SeedAsync` (existing, replaced semantics)
- `SeedWithoutTrainingDataAsync`
- `SeedWithMinimalTrainingDataAsync`
- `SeedBalancedTrainingDataAsync`
- `SeedHighSpamTrainingDataAsync`
- `SeedHighHamTrainingDataAsync`
- `SeedDeduplicationTestDataAsync`
- `SeedAnalyticsDataAsync`
- `SeedOldMessagesAsync`
- `SeedContentDetectionConfigAsync`
- `SeedWebUsersOnlyAsync` *(discovered by pre-design audit)*

`MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` and
`CreateDatabaseAndMigrateToAsync` are NOT retired. They remain for migration
tests' use.

---

## Template hierarchy

```
[bare]                                 ← never templated; only MigrationTestHelper
   │  CREATE DATABASE bare_dbname (no migrations applied)         consumes via direct CREATE
   │
   ▼
empty_template                         ← cloned by CreateDatabaseFromEmptyTemplateAsync
   │  CREATE DATABASE empty_template
   │  (apply migrations to HEAD)
   │  UPDATE pg_database SET datistemplate=true
   │
   ▼
golden_template                        ← cloned by CreateDatabaseFromGoldenTemplateAsync
   │  CREATE DATABASE golden_template TEMPLATE empty_template
   │  (run LoadCanonicalAsync(ctx, SharedDataProtectionProvider) once)
   │  UPDATE pg_database SET datistemplate=true
   │
   ▼
test_<guid>                            ← per-test, via CREATE DATABASE ... TEMPLATE
```

`empty_template` has migrations applied (schema at HEAD). It is NOT what
migration tests need — they apply migrations themselves and need to control
the schema version. Migration tests use the existing `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync()`
and `CreateDatabaseAndMigrateToAsync(...)` methods, which create fresh DBs
without templates.

A `bare_template` (no migrations, just `CREATE DATABASE`) is deliberately
not introduced. The math:

- `CREATE DATABASE` is ~50ms today
- Migration tests' real cost is applying migrations, which they do regardless
- A `bare_template` saves ~50ms per migration test in exchange for a third
  template path

Not worth the complexity for a marginal gain.

### Npgsql connection pooling for templates

Postgres requires no other connections to a template database before
`CREATE DATABASE … TEMPLATE` and before flagging via
`UPDATE pg_database SET datistemplate=true`. Npgsql pools by default —
calling `Dispose()` returns the connection to the pool, doesn't terminate
the backend session. Phase 2 implementation must use `Pooling=false` in
the connection string for any connection used to build or modify templates,
so disposal actually closes the backend.

The existing `MigrationTestHelper.DropDatabaseAsync` already shows the
related pattern (it uses `pg_terminate_backend` to close lingering
connections before `DROP DATABASE`).

---

## Per-test fixture flow

### `PostgresFixture.[OneTimeSetUp]` (Phase 2 onwards)

```
1. start container (existing behavior)
2. construct EphemeralDataProtectionProvider, expose as SharedDataProtectionProvider
3. open admin connection (Pooling=false)
4. CREATE DATABASE empty_template
5. apply migrations to empty_template (DbContext + Migrate())
6. UPDATE pg_database SET datistemplate=true WHERE datname='empty_template'

7. CREATE DATABASE golden_template TEMPLATE empty_template
8. open connection to golden_template (Pooling=false)
9. await GoldenDataset.LoadCanonicalAsync(context, SharedDataProtectionProvider)
   ← only canonical SQL load all session; encrypted columns use shared provider
10. dispose connection (Pooling=false ensures actual close)
11. UPDATE pg_database SET datistemplate=true WHERE datname='golden_template'
```

Templates exist for the entire session. Per-test work consumes them via
`MigrationTestHelper`.

### `PostgresFixture.[OneTimeTearDown]`

Disposes the container. TestContainers handles teardown of all databases
inside (templates and any leftover per-test DBs). No explicit
`DROP DATABASE` for templates — let container teardown reclaim them.

### `MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync` / `CreateDatabaseFromEmptyTemplateAsync`

Each method:

```
1. open admin connection to "postgres" DB (Pooling=false)
2. CREATE DATABASE "test_<guid>" TEMPLATE [golden_template | empty_template]
3. dispose admin connection
```

Per-test connection (returned by `GetDbContext()`) uses default Npgsql
pooling — this is standard test connection behavior, not template-related.

### `MigrationTestHelper.Dispose`

Unchanged — drops the per-test DB via existing `DropDatabaseAsync` path.

---

## Reducer surface (v1)

Five reducers. Each driven by a real consumer.

```csharp
GoldenReducePlan KeepSpam(int count);              // training_labels WHERE class=Spam
GoldenReducePlan KeepHam(int count);               // training_labels WHERE class=Ham
GoldenReducePlan KeepDetectionResults(int count);  // detection_results
GoldenReducePlan KeepMessages(int count);          // messages — see FK behavior below
GoldenReducePlan KeepUserActions(int count);       // user_actions
```

### `KeepMessages` FK cascade behavior

When `KeepMessages` deletes message rows, the database's FK constraints
determine what happens to children. The actual configuration (per
`AppDbContext.cs`):

- `message_edits` → `Cascade` (deleted with the message)
- `training_labels` → `Cascade` (deleted with the message)
- `detection_results` → `Cascade` (deleted with the message)
- `message_translations` → `Cascade` (deleted with the message)
- **`user_actions` → `SetNull`** (rows survive but `MessageId`/`ChatId` go null)

So `KeepMessages(0)` does NOT remove `user_actions` rows. If a test wants
both empty, chain `KeepMessages(0).KeepUserActions(0)`.

If a future test needs to clean up SetNull orphans created by `KeepMessages`
(rows with null `MessageId`) without removing legitimately-canonical
non-orphan rows, add a `PruneUserActions` method targeting that case. The
verb distinction (`Keep*` for state, `Prune*` for orphan cleanup) is
intentional. Out of scope for v1.

### Validation rules

- `count >= 0`. Negative throws `ArgumentOutOfRangeException` synchronously
  at `Keep*` invocation time (no DB knowledge required).
- **No upper-bound validation against canonical row counts.** `Keep*` uses
  natural `LIMIT` semantics: `KeepSpam(200)` against a canonical containing
  100 spam rows keeps all 100. If a developer passes a count larger than
  canonical actually contains, the test's own assertions surface the
  mistake at the assertion site — clearer error than a `[SetUp]` throw
  with a stale constant.
- Calling the same `Keep*` twice is last-wins, no error.
- Slices not mentioned retain full canonical content (the "default =
  canonical" rule).

### Execution semantics

- `Reduce(ctx)` is synchronous, returns a `GoldenReducePlan` with no DB work
  performed.
- `Keep*` methods mutate-and-return-this; chaining accumulates operations
  on the same plan.
- `ApplyAsync(ct)` opens a transaction on the context, runs each registered
  `Keep*` operation as a single
  `DELETE … WHERE id NOT IN (SELECT id … ORDER BY id ASC LIMIT n)`
  against the corresponding table, commits.
- `Keep*` calls touch independent slices; order between them does not change
  final state.
- Plans can be constructed once and reused across multiple `ApplyAsync`
  calls (e.g., to apply the same shape to two different contexts).

---

## Migration plan

| Phase | Commit subject | Outcome |
|-------|----------------|---------|
| 1 | `feat(test): add canonical SQL fixtures + GoldenReducePlan builder + SharedDataProtectionProvider` | Green; new infra exists, no consumer change |
| 2 | `feat(test): add template DB infrastructure to MigrationTestHelper (#463)` | Green; templates built, new methods exist, no consumer change |
| 3A | `refactor(test): migrate 10 canonical-consumer tests to template clone + Reduce` | Green; 10 test classes faster |
| 3B | `refactor(test): migrate 35 empty-consumer tests to empty-template clone` | Green; 35 test classes faster |
| 3C | `refactor(test): migration tests adopt GoldenDataset constants` | Green; constants threaded |
| 4 | `chore(test): retire legacy seed methods + SQL files` | Green; final state |

Six commits total. Each green and bisectable.

### Phase 1 — Build the new seed surface (no consumer change)

- Update `.csproj` `EmbeddedResource` glob from `TestData\SQL\*.sql` to
  `TestData\SQL\**\*.sql` (recursive).
- Create `TestData/SQL/canonical/` subfolder with all 15 per-table SQL
  files (FK-ordered).
- Create `TestData/SQL/migration/` subfolder. Move
  `40_pre_migration_impersonation_alerts.sql` into it. Update
  `CriticalMigrationTests.cs` (the only consumer) to reference the new path.
- Add `GoldenReducePlan.cs` with the five `Keep*` methods and `ApplyAsync`.
- Add `GoldenDataset.Reduce(AppDbContext)` factory.
- Add `GoldenDataset.LoadCanonicalAsync(AppDbContext, IDataProtectionProvider, CancellationToken)`.
- Add `PostgresFixture.SharedDataProtectionProvider` (an
  `EphemeralDataProtectionProvider` instance).
- Add NUnit-style framework tests for `GoldenReducePlan` in
  `TestData/Tests/GoldenReducePlanTests.cs`. Each test uses
  `MigrationTestHelper + LoadCanonicalAsync` to set up canonical against a
  freshly-migrated DB, then exercises `KeepSpam`, `KeepHam`, `KeepMessages`
  (cascade behavior, including verifying `user_actions` SetNull behavior
  is documented), `KeepDetectionResults`, `KeepUserActions`, the `count==0`
  case, the `count > actual_canonical_rows` LIMIT-semantics case,
  negative-count validation, and last-wins semantics.
- Old `Seed*Async` methods, old `00_base_*.sql` etc.: all unchanged.
- All existing tests still on legacy `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` +
  optional `Seed*Async` path.
- Build green; existing test suite still green.

### Phase 2 — Template DB infrastructure (#463)

- Extend `PostgresFixture.[OneTimeSetUp]` per the per-test fixture flow
  above. Use `Pooling=false` for build-time admin connections to ensure
  templates can be flagged.
- Add `MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync()` and
  `CreateDatabaseFromEmptyTemplateAsync()` methods. Both use `Pooling=false`
  for the admin connection that runs `CREATE DATABASE … TEMPLATE`.
- No test classes call the new methods yet. Existing tests still on legacy
  path.
- Build green; existing test suite still green.

### Phase 3A — Migrate 10 canonical-consumer tests

The audit identified 10 test classes that today call `GoldenDataset.Seed*Async`.
Each is migrated to:

1. Replace `await _testHelper.CreateDatabaseAndApplyMigrationsAsync()` with
   `await _testHelper.CreateDatabaseFromGoldenTemplateAsync()`.
2. Drop the `await GoldenDataset.SeedXyzAsync(...)` call entirely (canonical
   is already in the cloned DB).
3. For ML threshold tests, replace `TRUNCATE + INSERT` setup with
   `await GoldenDataset.Reduce(ctx).KeepSpam(N).KeepHam(M).ApplyAsync()`.
4. If the test today builds its own `IDataProtectionProvider` for encrypted
   config, swap to `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`.

The 10 files:

- `Configuration/ConfigServiceIntegrationTests.cs`
- `Deduplication/SimHashComparisonTests.cs`
- `ML/BayesClassifierServiceTests.cs`
- `ML/MLTextClassifierServiceTests.cs`
- `Repositories/AnalyticsRepositoryTests.cs`
- `Repositories/DetectionResultsRepositoryTests.cs`
- `Repositories/TelegramUserRepositoryTests.cs`
- `Repositories/TelegramUserUpsertTests.cs`
- `Repositories/TrainingLabelsRepositoryTests.cs`
- `Telegram/Repositories/LinkedChannelsRepositoryTests.cs`

Four additional files were flagged by the audit as ambiguous and require
manual verification during implementation:

- `Repositories/ContentDetection/ProfileScanAlertMappingTests.cs` — agent saw
  manual `Add` patterns; likely empty-consumer in practice
- `Repositories/ContentDetection/ReportsRepositoryTests.cs` — agent flagged
  reclassify to empty-consumer (never seeds)
- `Telegram/Services/WelcomeFlowBypassIntegrationTests.cs` — agent uncertain
  whether seeding is implicit
- `Telegram/Services/BanCelebrationServiceTests.cs` — agent uncertain whether
  seeding is implicit

Implementer reads each file's setup, classifies definitively, and migrates
to the appropriate phase (3A or 3B).

### Phase 3B — Migrate 35 empty-consumer tests (mechanical sweep)

The audit identified 35 test classes that today start from
`CreateDatabaseAndApplyMigrationsAsync` and never call any `GoldenDataset.Seed*Async`.
For each: replace `await _testHelper.CreateDatabaseAndApplyMigrationsAsync()`
with `await _testHelper.CreateDatabaseFromEmptyTemplateAsync()`. The test's
existing data-assembly logic (Add / INSERT / SUT writes) is preserved
unchanged — empty-consumers explicitly may do that.

If a test today uses `services.AddDataProtection().PersistKeysToFileSystem(...)`,
swap to the shared provider as in Phase 3A.

This is a near-mechanical sweep. The 35 files (per audit):

- All under `Configuration/` (5 files): AIProviderConfigIntegrationTests,
  ConfigRepositoryIntegrationTests, ContentDetectionConfigRepositoryTests,
  SystemConfigRepositoryWebPushTests
- All under `ContentDetection/Repositories/` (1 file): ReportsRepositoryTests
- Under `Jobs/`: WelcomeTimeoutJobTests
- Under `Repositories/`: BanCelebrationCaptionRepositoryTests,
  BanCelebrationGifRepositoryTests, DbContextFactoryMigrationTests,
  InviteRepositoryTests, MessageHistoryRepositoryTests,
  NotificationRepositoriesTests, ReportCallbackContextRepositoryTests,
  TelegramUserRepositoryKickCountTests, UserActionsRepositoryConstraintTests,
  UsernameHistoryRepositoryTests, UserRepositoryTests
- Under `Services/`: BackgroundJobConfigPersistenceTests, NotificationConfigTests
- Under `Services/Backup/`: BackupServiceTests
- Under `Telegram/`: AuditHandlerTests, CasCheckServiceTests,
  MessageProcessingServiceTests, SystemAccountBypassTests
- Under `Telegram/Repositories/`: ExamSessionRepositoryTests
- Under `Telegram/Services/`: ExamFlowServiceTests
- Under `Telegram/Services/Bot/`: BotChatServiceTests, BotDmServiceTests,
  BotMessageServiceTests

(Some entries above are pure-unit-test classes that don't touch the DB —
they're listed in the audit but require no migration. The implementer skips
those during the sweep.)

### Phase 3C — Migration tests adopt constants

Migration tests stay on existing `MigrationTestHelper` methods
(`CreateDatabaseAndApplyMigrationsAsync` for HEAD-schema scenarios,
`CreateDatabaseAndMigrateToAsync` for intermediate-schema scenarios). They do
NOT migrate to `CreateDatabaseFromEmptyTemplateAsync` — that's a deliberate
non-goal of this PR (out of scope for the optimization).

What changes: replace hardcoded ID literals with `GoldenDataset.*` constants.

The 8 migration test files:

- `Migrations/CascadeBehaviorTests.cs`
- `Migrations/CriticalMigrationTests.cs` (also fix line 210's
  `-1001234567890` and line 265's `-1009876543210` literals)
- `Migrations/DataIntegrityTests.cs`
- `Migrations/InfrastructureTests.cs`
- `Migrations/MigrationCompactionTests.cs`
- `Migrations/MigrationWorkflowTests.cs`
- `Migrations/SequenceIntegrityTests.cs`
- `PgBouncer/PgBouncerMigrationTests.cs`

Some of these files have minimal or no constant references and require zero
changes. The implementer audits and updates only what's needed.

### Phase 4 — Cleanup

- Run `find_symbol_usages` on every retired method to confirm zero callers.
  If any consumer slipped past Phases 3A–C, route it through the new system
  in an extra commit before proceeding.
- Delete the 11 retired methods from `GoldenDataset`.
- Delete the 15 obsolete SQL files (listed in "Files deleted" above).
- Capture final integration suite wall-clock for the PR description.

`MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` and
`CreateDatabaseAndMigrateToAsync` are NOT deleted — they remain for migration
tests' use.

---

## Error handling

### Plan validation errors (synchronous, before `ApplyAsync`)

- Negative count → `ArgumentOutOfRangeException` on the `Keep*` call.
- No upper-bound validation. `Keep*` uses natural `LIMIT` semantics; passing
  a count larger than canonical actually contains is a no-op for the excess
  (see Reducer surface > Validation rules for rationale).

### Plan execution errors (during `ApplyAsync`)

- `ApplyAsync` opens a transaction, runs each step, commits.
- On exception in any step: transaction rolls back. The original exception
  is wrapped in `GoldenReducePlanException` with `StepName` (e.g.,
  `"KeepSpam"`, `"KeepMessages"`) and the inner exception preserved.

### Template build errors during `[OneTimeSetUp]`

- If `CREATE DATABASE`, migration apply, or `LoadCanonicalAsync` fails
  during template construction, `[OneTimeSetUp]` propagates the failure.
  NUnit reports the `[OneTimeSetUp]` failure and skips all tests in the
  assembly. Failure mode is loud and immediate.

### Per-test database creation errors

- `CREATE DATABASE … TEMPLATE` requires no other connections to the template.
  The fixture's connections to template DBs use `Pooling=false`, so disposal
  actually closes the backend before the template is flagged. If a clone
  fails for any other reason (transient connection issue, etc.), the test
  fails normally; not retried.

---

## Testing strategy

### Phase 1 — Framework correctness tests

`GoldenReducePlan` is exercised by NUnit tests in
`TestData/Tests/GoldenReducePlanTests.cs` (new file). Each test uses
`MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync()` to build a fresh
DB at HEAD schema, calls `LoadCanonicalAsync` to populate canonical, then
exercises one or more `Keep*` operations and asserts final row counts and
id ordering. Coverage:

- `KeepSpam(N)` keeps the lowest-id N spam labels
- `KeepHam(N)` keeps the lowest-id N ham labels
- `KeepSpam(0)` removes all spam (and `KeepHam(0)` symmetrically)
- `KeepDetectionResults(N)` works
- `KeepUserActions(N)` works
- `KeepMessages(0)` cascades through `message_edits`, `training_labels`,
  `detection_results`, `message_translations` (Cascade FKs) and SetNulls
  `user_actions.MessageId`/`ChatId` (verifies the documented FK behavior)
- Negative count throws `ArgumentOutOfRangeException`
- `count > actual_canonical_rows` is a no-op for the excess
- Last-wins for repeated `Keep*` on same slice
- `Keep*` calls in different orders produce identical final state
- A single `ApplyAsync` exercising multiple reducers runs in one transaction
  (assert via querying intermediate state from a separate connection — should
  see canonical, not partial-reduced state, until commit)

These tests run in Phase 1 against the manually-loaded canonical via
`LoadCanonicalAsync`. Once Phase 2 lands, they could optionally be rewritten
to use `CreateDatabaseFromGoldenTemplateAsync` for setup speed, but Phase 1
must validate correctness without depending on the templates that Phase 2
introduces.

### Phases 2–4 — Existing test suite as regression test

The pre-existing integration tests are the regression detector for the
migration. Each Phase 3 commit must show the suite green before proceeding.
A test failure during a Phase 3 commit is a real bug — either:

- The test had a hidden assumption about minimal seed shape that canonical
  now violates → fix by reducing the appropriate slice
- The test was relying on data that legacy `Seed*Async` provided and
  canonical missed → fix by adding to the appropriate `canonical/*.sql`
- The test was using an additive pattern that's been removed → for canonical-
  consumers, fix by rerouting through SUT writes; for empty-consumers, this
  shouldn't happen (additive is allowed there)

### Phase 4 — Symbol-usage verification

Before deleting any retired method, run `mcp__csharp-er-mcp__find_symbol_usages`
on each. Expected result: zero non-test references. Any unexpected reference
gets routed through the new system in an extra commit before proceeding to
deletion.

---

## Performance baseline

### Pre-Phase-1 baseline (Phase 1, first commit)

Capture the integration suite's full wall-clock time before any changes:

```
dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal"
```

Record in PR description as `T0`. Today's per-test cost is dominated by
`CREATE DATABASE` + `MigrateAsync()` (250–550ms) plus optional `Seed*Async`
(another 0–1500ms depending on which Seed method).

### Mid-migration timing (informational only)

Phases 1 and 2 don't change consumer behavior — suite timing should match T0.

Phase 3 commits move test classes from the legacy path to template clones.
Each migrated class drops its per-test cost from ~250–2000ms to ~50–150ms.
Suite timing should improve commit-by-commit; capture intermediate timings
for the PR description if useful but not required.

### Post-Phase-4 final timing

After Phase 4 lands, capture again as `T1`. Document `T0` and `T1` in PR
description for transparency.

---

## Migration tests deliberately untouched

Migration tests (`CascadeBehaviorTests`, `DataIntegrityTests`,
`InfrastructureTests`, `MigrationWorkflowTests`, `SequenceIntegrityTests`,
`MigrationCompactionTests`, `CriticalMigrationTests`, `PgBouncerMigrationTests`)
test migrations themselves. Some need intermediate-schema state via
`CreateDatabaseAndMigrateToAsync(targetMigration)`; others apply all
migrations end-to-end via `CreateDatabaseAndApplyMigrationsAsync()`. Either
way, they need the migration system itself to be exercised — they cannot
clone a pre-migrated template.

`CreateDatabaseAndApplyMigrationsAsync` and `CreateDatabaseAndMigrateToAsync`
remain on `MigrationTestHelper` indefinitely for these consumers.

A `bare_template` (no migrations, just `CREATE DATABASE`) is deliberately
not introduced. The math:

- `CREATE DATABASE` is ~50ms today
- Migration tests' real cost is applying migrations, which they do regardless
- A `bare_template` saves ~50ms per migration test in exchange for a third
  template path

Not worth the complexity for a marginal gain. Migration tests live outside
the template optimization on purpose.

---

## Out of scope / future work

- **Selective canonical loading as public API.** `LoadCanonicalAsync` exists
  but is internal-ish (used by fixture and framework tests, not production
  test code). If a future test genuinely needs "empty + selective base data"
  beyond what empty-template + SUT writes can provide, expose
  `LoadCanonicalAsync` publicly at that point.
- **Additional reducers.** `KeepInvites`, `KeepWebNotifications`,
  `KeepUsernameHistory`, `PruneUserActions` are symmetric to the existing
  surface but have no current consumer. Add when first consumer arrives.
- **`bare_template` for migration test optimization.** Discussed above, not
  worth it at current scale.
- **Migration tests on template path.** Migration tests that today use
  `CreateDatabaseAndApplyMigrationsAsync` for HEAD-schema scenarios could in
  principle migrate to `CreateDatabaseFromEmptyTemplateAsync` for the same
  speedup the empty-consumers get. Deferred to keep scope clean — migration
  tests are a separate concern.
- **Cross-project constant-hygiene cleanup.** Three low-priority items
  surfaced by the pre-design audit, deferred to follow-up issues:
  - `UnitTests/PeerCacheIdConversionTests.cs:25, 40` — replace literal
    `-1001322973935` with `GoldenDataset.ManagedChats.MainChat_Id`. Requires
    UnitTests to reference IntegrationTests' constants (architectural
    decision deferred).
  - `E2ETests/Tests/Reports/ReportsTests.cs:358, 365, 371, 378` — replace
    canonical-range literals with `Random.Shared`-generated builder values.
  - `IntegrationTests/Deduplication/SimHashComparisonTests.cs:136` — verify
    `Has.Count.EqualTo(22)` assertion still holds under canonical (likely
    fine since dedup data is in canonical at the same IDs).

---

## Acceptance checklist

### Phase 1
- [ ] `.csproj` `EmbeddedResource` glob updated to `TestData\SQL\**\*.sql`
- [ ] `TestData/SQL/canonical/` exists with 15 per-table SQL files
- [ ] `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql`
      moved; `CriticalMigrationTests.cs` references the new path
- [ ] `GoldenReducePlan` exists with 5 `Keep*` methods + `ApplyAsync`
- [ ] `GoldenDataset.Reduce(AppDbContext)` exists
- [ ] `GoldenDataset.LoadCanonicalAsync(AppDbContext, IDataProtectionProvider, CancellationToken)` exists
- [ ] `PostgresFixture.SharedDataProtectionProvider` exists (an
      `EphemeralDataProtectionProvider`)
- [ ] `GoldenReducePlanTests.cs` exists with framework correctness coverage
- [ ] All existing tests still pass on legacy path
- [ ] `T0` baseline captured

### Phase 2
- [ ] `PostgresFixture.[OneTimeSetUp]` builds `empty_template` and
      `golden_template`, marks both as templates, using `Pooling=false` for
      build-time admin connections
- [ ] `MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync()` exists
- [ ] `MigrationTestHelper.CreateDatabaseFromEmptyTemplateAsync()` exists
- [ ] Both new methods use `Pooling=false` for the admin connection that
      runs `CREATE DATABASE … TEMPLATE`
- [ ] `[OneTimeTearDown]` disposes the container; no explicit template drops
- [ ] All existing tests still pass

### Phases 3A–C
- [ ] 10 canonical-consumer files migrated; 4 ambiguous files classified and
      migrated to appropriate phase; suite green
- [ ] 35 empty-consumer files migrated (mechanical method-name swap + DI
      provider swap where applicable); suite green
- [ ] Migration tests adopt `GoldenDataset.*` constants where applicable;
      suite green

### Phase 4
- [ ] `find_symbol_usages` shows zero callers of every retired method
- [ ] 11 retired methods deleted from `GoldenDataset`
- [ ] 15 obsolete SQL files deleted
- [ ] `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` retained
      (still used by migration tests)
- [ ] All tests pass
- [ ] `T1` final timing captured; documented in PR description with `T0`
      for comparison

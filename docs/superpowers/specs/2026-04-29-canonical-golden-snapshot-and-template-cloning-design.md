# Canonical Golden Snapshot + Template DB Cloning

**Date:** 2026-04-29
**Branch:** `refactor/golden-canonical-snapshot-and-templating`
**Issues:** Closes #462, Closes #463
**Scope:** Replace today's three competing integration-test seeding strategies
with a single canonical superset cloned per-test from a Postgres template
database. Establishes a strict two-path model (canonical or empty), introduces
a subtractive fluent reducer for tests that need a constrained shape, and
restructures `TestData/SQL` from scenario-based files to per-table files. Both
issues ship together because they belong together: #462 establishes "canonical
is the only seed shape" and #463 makes "load it once, clone the rest" possible.
Without #463, the larger canonical would slow the integration suite during
Phases 1–3 before Phase 4 recovers and surpasses the pre-change baseline.
**PR target:** `develop` (per project workflow).

---

## Context

Today's `TelegramGroupsAdmin.IntegrationTests` project (623 tests, ~5–8 min
wall-clock) has accumulated three competing seed strategies:

1. Tests using `GoldenDataset.Seed*Async` correctly and referencing its
   constants.
2. Tests calling `GoldenDataset.SeedAsync` and then adding extra inline rows
   via `context.Set<X>().Add(new X { ... })` or raw `INSERT INTO`.
3. Tests bypassing `GoldenDataset` entirely and hand-rolling users, chats, and
   messages with arbitrary IDs.

The result: inconsistent data shapes across the suite, brittle assertions that
pass against contrived state, and silent drift when a test author copies a
Golden ID as a literal instead of referencing the constant. The fixture today
runs TRUNCATE-based per-test cleanup against a single shared Postgres container,
so the `Seed*Async` cost is paid on every test that needs data.

A pre-design audit (4 parallel agents, one per test project) confirmed the
existing 18-file remediation table in #462's body and surfaced two additional
files that need migration:

- `Repositories/AnalyticsRepositoryTests.cs` — calls retired methods
  (`SeedWithoutTrainingDataAsync` + `SeedAnalyticsDataAsync`)
- `Configuration/ConfigServiceIntegrationTests.cs` — calls a partial-seed
  method (`SeedWebUsersOnlyAsync`) the issue body did not enumerate

The audit also confirmed UnitTests, ComponentTests, and E2ETests are mostly
clean of canonical-collision risks. Three low-priority constant-hygiene items
(in UnitTests, ComponentTests, and E2ETests) are explicitly deferred to
quick-win follow-ups since they're unrelated to the canonical-snapshot
architecture.

---

## Goals

- Single canonical superset of test data, structurally enforced (no fourth
  bucket besides canonical / empty / migration-test).
- Fluent reducer API for tests that need a constrained shape from canonical.
- Per-test database cloned from a session-built `golden_template`, replacing
  TRUNCATE-based per-test cleanup. Per-test setup drops from "TRUNCATE +
  re-seed" to "CREATE DATABASE ... TEMPLATE" (~50–150ms).
- SQL fixtures organized strictly by table, FK-ordered, in a `canonical/`
  subfolder. Migration-test-only fixtures isolated under `migration/`.
- Migration tests untouched — they keep `MigrationTestHelper` and adopt
  `GoldenDataset.*` constants for ID literals.
- Each commit in the implementation phase keeps build and test suite green so
  bisect lands on a meaningful state.

## Non-goals

- Refactoring migration tests onto the template-clone fast path (deliberately
  deferred — see "Migration tests deliberately untouched" below).
- Cross-project constant-hygiene cleanup (deferred to follow-up issues).
- Adding speculative reducers (`KeepInvites`, `KeepWebNotifications`,
  `KeepUsernameHistory`) that no current test requires. YAGNI applies; add
  when first consumer arrives.
- Migrating UnitTests / ComponentTests / E2ETests off any existing patterns.

---

## Architecture

### Three seeding paths, no fourth bucket

**Path 1 — Canonical (default).** The fixture clones a per-test database from
`golden_template`. The test's `[SetUp]` does nothing seed-related; canonical
is already there.

```csharp
[TestFixture]
public class FooTests : IntegrationTestBase
{
    [Test]
    public async Task Bar()
    {
        // DB has full canonical, ready to use
    }
}
```

**Path 1b — Canonical with reductions.** For tests that need a constrained
shape (ML threshold, single-class corpora). The fixture clones canonical, then
the test's `[SetUp]` runs a fluent `Reduce` plan to subtract.

```csharp
[TestFixture]
public class MLThresholdTests : IntegrationTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await GoldenDataset.Reduce(_context)
            .KeepSpam(5)
            .KeepHam(5)
            .ApplyAsync();
    }
}
```

**Path 2 — Empty DB.** The fixture clones from `empty_template` (migrations
only, no data). For constraint tests and "no data" assertions. Test class
opts in via attribute.

```csharp
[TestFixture, UseEmptyDatabase]
public class ConstraintTests : IntegrationTestBase
{
    [Test]
    public async Task ViolatesConstraint()
    {
        // DB has migrations only, ready to assemble specific state
    }
}
```

**Path 3 — Migration tests.** Stay on `MigrationTestHelper` with their existing
fresh-DB-per-test pattern. They reference `GoldenDataset.*` constants for ID
literals but otherwise live outside the template-clone optimization. See
"Migration tests deliberately untouched" below.

### Selection rule for reductions

Count-only, deterministic by id-order. `KeepSpam(5)` keeps the 5 training
labels with `Class=Spam` having the lowest `message_id`; the rest are deleted.
Same model for every reducer slice — lowest-id wins.

No predicate selectors. No per-row content selection. The locked rationale: the
issue is *fixing* the "every test does its own thing" problem; predicates
would re-introduce it.

### Boundary rule (code-review enforced)

A test that has been migrated to the new infrastructure (i.e., has
`[UseTemplateFixture]` or no attribute under the post-Phase-4 default) **never**
calls `Add(new SomeDto { ... })` followed by `SaveChangesAsync`, and **never**
runs raw `INSERT INTO` in setup. The cloned template plus an optional `Reduce`
plan are the only modification paths. Tests that need additive data must
either:

- Drive the writes through the system under test (the repo or service the
  test exercises), or
- Migrate the data into `canonical/*.sql` so every test gets it, or
- Use `[UseEmptyDatabase]` and assemble state through SUT writes.

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

All files under `canonical/` and `migration/` are marked `EmbeddedResource` in
the `.csproj`. Loading order under `canonical/` follows lexicographic filename
order, which is FK-correct by construction (the numbering encodes dependency
order: `web_users` and `telegram_users` first, then `managed_chats`, then
their dependents, etc.).

### What absorbs what

`canonical/02_telegram_users.sql` absorbs the rows currently spread across
`00_base_telegram_users.sql`, the dedup-author rows in `30_dedup_test_data.sql`,
the old-message-author rows in `60_old_messages.sql`, and the analytics-author
rows in `50_analytics_test_data.sql`.

`canonical/07_messages.sql` absorbs the rows currently in
`04_base_messages.sql` (11 base messages), `30_dedup_test_data.sql` (22 dedup
messages at IDs `95001`–`95022`), `60_old_messages.sql` (6 old messages at IDs
`96001`–`96006`), and the temporal messages from `50_analytics_test_data.sql`.

`canonical/09_user_actions.sql` is new and absorbs the analytics user_action
rows from `50_analytics_test_data.sql` plus baseline kick/welcome history.

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

```csharp
namespace TelegramGroupsAdmin.IntegrationTests.TestData;

public static partial class GoldenDataset
{
    // Public per-test API: returns a builder for tests that need a constrained
    // shape from canonical. Synchronous factory; no DB hit until ApplyAsync.
    public static GoldenReducePlan Reduce(AppDbContext context);

    // Internal-ish: loads canonical/*.sql into the target context.
    // Used by PostgresFixture in [OneTimeSetUp] to build golden_template.
    // Used by the framework's own builder tests to exercise Reduce against a
    // manually-loaded canonical state before the template infrastructure exists.
    // Not intended for production test setup.
    public static Task LoadCanonicalAsync(AppDbContext context, CancellationToken ct = default);
}

public sealed class GoldenReducePlan
{
    GoldenReducePlan KeepSpam(int count);
    GoldenReducePlan KeepHam(int count);
    GoldenReducePlan KeepDetectionResults(int count);
    GoldenReducePlan KeepMessages(int count);     // cascades through edits, translations, training_labels, detection_results
    GoldenReducePlan KeepUserActions(int count);

    Task ApplyAsync(CancellationToken ct = default);
}
```

### Constants kept and extended

`GoldenDataset.TelegramUsers.UserN_TelegramUserId` (1–7),
`GoldenDataset.Users.UserN_Id` (web user GUIDs),
`GoldenDataset.ManagedChats.MainChat_Id` and siblings — all preserved
unchanged. New constants added only if a new fixture introduces an identifier
tests need to assert against (e.g., `Invites.PendingInvite_Id` if `14_invites.sql`
introduces a row that's referenced by ID in test assertions).

### Attributes (Phase 2)

```csharp
namespace TelegramGroupsAdmin.IntegrationTests.Attributes;

// During Phases 2-3: opt-in to template-clone fixture path.
// Phase 4: removed because canonical becomes the default.
[AttributeUsage(AttributeTargets.Class)]
public sealed class UseTemplateFixtureAttribute : Attribute { }

// Permanent: opts into empty_template clone. Expected to have few or zero
// consumers in practice — kept as escape hatch.
[AttributeUsage(AttributeTargets.Class)]
public sealed class UseEmptyDatabaseAttribute : Attribute { }
```

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

---

## Template hierarchy

```
[bare]                                 ← never templated; only MigrationTestHelper consumes
   │  CREATE DATABASE bare_dbname (no migrations applied)
   │
   ▼
empty_template                         ← cloned by [UseEmptyDatabase] consumers
   │  CREATE DATABASE empty_template
   │  (apply migrations to HEAD)
   │  UPDATE pg_database SET datistemplate=true
   │
   ▼
golden_template                        ← cloned by default (Path 1) and [UseTemplateFixture]
   │  CREATE DATABASE golden_template TEMPLATE empty_template
   │  (run LoadCanonicalAsync to populate)
   │  UPDATE pg_database SET datistemplate=true
   │
   ▼
test_<guid>                            ← per-test, via CREATE DATABASE ... TEMPLATE
```

`empty_template` has migrations applied (schema at HEAD). It is NOT what
migration tests need — they apply migrations themselves and need to control
the schema version. Migration tests use `MigrationTestHelper.CreateAsync()`
which creates a fresh DB without templates.

A `bare_template` is deliberately not introduced. The `CREATE DATABASE`
operation itself is ~50ms — already cheap. Migration tests' real cost is
applying migrations, which they have to do regardless of starting state.
A `bare_template` would save ~50ms per migration test in exchange for a third
template path to maintain. Not worth the complexity.

---

## Per-test fixture flow

### `PostgresFixture.[OneTimeSetUp]` (Phase 2 onwards)

```
1. start container (existing behavior)
2. open admin connection
3. CREATE DATABASE empty_template
4. apply migrations to empty_template (DbContext + Migrate())
5. UPDATE pg_database SET datistemplate=true WHERE datname='empty_template'

6. CREATE DATABASE golden_template TEMPLATE empty_template
7. open connection to golden_template
8. await GoldenDataset.LoadCanonicalAsync(context)   ← only canonical SQL load all session
9. close connection (required before template flag)
10. UPDATE pg_database SET datistemplate=true WHERE datname='golden_template'
```

Templates exist for the entire session. Per-test work consumes them.

### Per-test `[SetUp]` (Phase 2 onwards, transitional)

```
read [UseEmptyDatabase] / [UseTemplateFixture] from TestContext.CurrentContext
read test name → generate per-test database name "test_<guid>"

if attribute is [UseEmptyDatabase]:
    CREATE DATABASE "test_<guid>" TEMPLATE empty_template
    expose connection string for AppDbContext

else if attribute is [UseTemplateFixture]:
    CREATE DATABASE "test_<guid>" TEMPLATE golden_template
    expose connection string for AppDbContext

else:
    (legacy path during transition only)
    TRUNCATE all tables on shared container
    expose existing shared connection
    [test then calls old Seed*Async methods]
```

In Phase 4, the `else` branch is removed. `[UseTemplateFixture]` is removed
(since canonical-clone is the only default path). `[UseEmptyDatabase]` stays.

### Per-test `[TearDown]`

For attributed tests: `DROP DATABASE "test_<guid>"`. For the legacy fallback:
no-op (next test's TRUNCATE handles cleanup).

---

## Reducer surface (v1)

Five reducers. Each driven by a real consumer.

```csharp
GoldenReducePlan KeepSpam(int count);              // training_labels WHERE class=Spam
GoldenReducePlan KeepHam(int count);               // training_labels WHERE class=Ham
GoldenReducePlan KeepDetectionResults(int count);  // detection_results
GoldenReducePlan KeepMessages(int count);          // messages, cascades through edits/translations/training_labels/detection_results
GoldenReducePlan KeepUserActions(int count);       // user_actions
```

### Validation rules

- `count >= 0`. Negative throws `ArgumentOutOfRangeException` synchronously
  at `Keep*` invocation time (no DB knowledge required).
- **No upper-bound validation against canonical row counts.** `Keep*` uses
  natural `LIMIT` semantics: `KeepSpam(200)` against a canonical containing
  100 spam rows keeps all 100 (the inner `SELECT … LIMIT 200` returns 100,
  the outer `DELETE … NOT IN (...)` deletes zero rows). If a developer
  passes a count larger than canonical actually contains, the test's own
  assertions surface the mistake with a clearer error than a validation
  exception would (`Expected: 200 / Actual: 100` at the assertion site is
  more localized than an `[SetUp]` throw). Rationale: maintaining a static
  canonical-count contract creates drift risk between SQL fixtures and
  validation constants, and the footgun it would catch is rare and
  self-diagnosing.
- Calling the same `Keep*` twice is last-wins, no error.
- Slices not mentioned retain full canonical content (the "default =
  canonical" rule).

### Execution semantics

- `Reduce(ctx)` is synchronous, returns a `GoldenReducePlan` with no DB work
  performed.
- `Keep*` methods mutate-and-return-this; chaining accumulates operations on
  the same plan.
- `ApplyAsync(ct)` opens a transaction on the context, runs each registered
  `Keep*` operation as a single `DELETE … WHERE id NOT IN (SELECT id … ORDER BY id ASC LIMIT n)`
  against the corresponding table, commits.
- `Keep*` calls touch independent slices; order between them does not change
  final state.
- `KeepMessages` cascades via existing FK constraints (configured in
  `AppDbContext`) — deleting messages cascades to `message_edits`,
  `message_translations`, `training_labels`, `detection_results`.
- Plans can be constructed once and reused across multiple `ApplyAsync` calls
  (e.g., to apply the same shape to two different contexts).

---

## Migration plan

| Phase | Commit subject | Outcome |
|-------|----------------|---------|
| 1 | `feat(test): add canonical SQL fixtures + GoldenReducePlan builder` | Green; new infra exists, no consumer change |
| 2 | `feat(test): build empty_template + golden_template in PostgresFixture (#463)` | Green; templates exist, no consumer change |
| 3A | `refactor(test): migrate Group A tests onto template fixture` | Green; 3 test classes faster |
| 3B | `refactor(test): migrate Group B tests onto template fixture` | Green; ~17 test classes faster |
| 3C | `refactor(test): migrate ML threshold tests onto template fixture + Reduce` | Green; ML tests faster |
| 3D | `refactor(test): migration tests adopt GoldenDataset constants` | Green; constants threaded |
| 4 | `chore(test): retire legacy seed methods + SQL files; finalize fixture defaults` | Green; final state |

Seven commits total. Each green and bisectable.

### Phase 1 — Build the new seed surface (no consumer change)

- Create `TestData/SQL/canonical/` subfolder with all 15 per-table SQL files
  (FK-ordered).
- Create `TestData/SQL/migration/` subfolder. Move `40_pre_migration_impersonation_alerts.sql`
  into it. Update `CriticalMigrationTests.cs:184-186` (the only consumer)
  to reference the new path.
- Add `GoldenReducePlan.cs` with the five `Keep*` methods and `ApplyAsync`.
- Add `GoldenDataset.Reduce(AppDbContext)` factory.
- Add `GoldenDataset.LoadCanonicalAsync(AppDbContext, CancellationToken)`.
- Add NUnit-style framework tests for `GoldenReducePlan` that load canonical
  via `MigrationTestHelper + LoadCanonicalAsync`, then exercise `KeepSpam`,
  `KeepHam`, `KeepMessages` (cascade), `KeepDetectionResults`, `KeepUserActions`,
  the `count==0` case, the `count > actual_canonical_rows` LIMIT-semantics
  case (passes count > N, ends with N rows surviving), negative-count
  validation, and last-wins semantics.
- Old `Seed*Async` methods, old `00_base_*.sql` etc., old TRUNCATE+seed
  fixture path: all unchanged.
- Build green; existing test suite still green.

### Phase 2 — Template DB infrastructure (#463)

- Extend `PostgresFixture.OneTimeSetUp` per the per-test fixture flow above.
- Add `UseTemplateFixtureAttribute` and `UseEmptyDatabaseAttribute`.
- Per-test `SetUp` reads the attribute on the current test class (NUnit API
  detail; final form decided during implementation) and dispatches to the
  right path. Legacy TRUNCATE-fallback branch retained for unmigrated tests.
- Add `[OneTimeTearDown]` cleanup that drops both templates after the
  container stops (technically redundant with TestContainers, but explicit).
- No test classes use either attribute yet. Existing tests still on legacy path.
- Build green; existing test suite still green.

### Phase 3A — Migrate Group A (low-risk constant subs)

Three classes, simple constant substitutions and attribute additions:

- `Telegram/Repositories/LinkedChannelsRepositoryTests.cs`
- `Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`
- `Telegram/Repositories/ExamSessionRepositoryTests.cs`

Each: add `[UseTemplateFixture]`, drop legacy `Seed*Async` calls, replace ID
literals with Golden constants where present.

### Phase 3B — Migrate Group B (mid-complexity Add/INSERT swaps)

Seventeen classes. Each: add `[UseTemplateFixture]` (or remove the test's
inline data assembly entirely if canonical now provides it):

- `Repositories/UserActionsRepositoryConstraintTests.cs` (uses
  `KeepUserActions(0)` because it asserts on user_actions count)
- `Repositories/InviteRepositoryTests.cs`
- `Repositories/UsernameHistoryRepositoryTests.cs`
- `Repositories/TelegramUserRepositoryKickCountTests.cs`
- `Repositories/TrainingLabelsRepositoryTests.cs`
- `Repositories/DetectionResultsRepositoryTests.cs`
- `Repositories/NotificationRepositoriesTests.cs`
- `ContentDetection/Repositories/ProfileScanAlertMappingTests.cs`
- `Jobs/WelcomeTimeoutJobTests.cs`
- `Telegram/Services/ExamFlowServiceTests.cs`
- `Telegram/Services/BanCelebrationServiceTests.cs`
- `Telegram/Services/Bot/BotChatServiceTests.cs`
- `Telegram/Services/Bot/BotDmServiceTests.cs`
- `Telegram/Services/Bot/BotMessageServiceTests.cs`
- `Services/Backup/BackupServiceTests.cs`
- `Repositories/AnalyticsRepositoryTests.cs` *(added by audit)*
- `Configuration/ConfigServiceIntegrationTests.cs` *(added by audit)*

For tests that today extend canonical via additive seed methods
(`SeedAnalyticsDataAsync`, `SeedDeduplicationTestDataAsync`, `SeedOldMessagesAsync`),
the migration is "drop the additive call entirely" — the data is now in
canonical.

### Phase 3C — Migrate ML threshold tests

Two classes. Both replace `TRUNCATE + INSERT` setup loops with
`Reduce(ctx).KeepSpam(N).KeepHam(M).ApplyAsync()`:

- `ML/MLTextClassifierServiceTests.cs`
- `ML/BayesClassifierServiceTests.cs`

### Phase 3D — Migration tests adopt constants

Five classes. They stay on `MigrationTestHelper` and inline data, but replace
hardcoded ID literals with `GoldenDataset.*` constants:

- `Migrations/CriticalMigrationTests.cs` (also fix line 210's
  `-1001234567890` and line 265's `-1009876543210` literals)
- `Migrations/CascadeBehaviorTests.cs`
- `Migrations/DataIntegrityTests.cs`
- `Migrations/InfrastructureTests.cs`
- `Migrations/SequenceIntegrityTests.cs`

### Phase 4 — Cleanup

- Run `find_symbol_usages` on every retired method to confirm zero callers.
  If any consumer slipped past Phases 3A–D, route it through the new system
  in an extra commit before proceeding.
- Delete the 11 retired methods from `GoldenDataset`.
- Delete the 15 obsolete SQL files (listed in "Files deleted" above).
- Delete the legacy TRUNCATE-fallback branch from `PostgresFixture.[SetUp]`.
- Make `golden_template` clone the default for unattributed test classes.
- Remove `UseTemplateFixtureAttribute` (definition + all usages) since
  canonical is now the default.
- Keep `UseEmptyDatabaseAttribute` as a permanent escape hatch.
- Capture final integration suite wall-clock for the PR description.

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

- If `CREATE DATABASE`, migration apply, or `LoadCanonicalAsync` fails during
  template construction, the fixture's `[OneTimeSetUp]` propagates the
  failure. NUnit reports the `[OneTimeSetUp]` failure and skips all tests in
  the assembly. Failure mode is loud and immediate.

### Per-test database creation errors

- `CREATE DATABASE … TEMPLATE` requires no other connections to the template.
  The fixture's session connection to `golden_template` is closed in step 9
  of `[OneTimeSetUp]` before the template flag is set. If a clone fails for
  any other reason, the test fails normally (not retried).

---

## Testing strategy

### Phase 1 — Framework correctness tests

`GoldenReducePlan` is exercised by NUnit tests in
`TestData/Tests/GoldenReducePlanTests.cs` (new file). Each test uses
`MigrationTestHelper` to build a fresh DB at HEAD schema, calls
`LoadCanonicalAsync`, then exercises one or more `Keep*` operations and asserts
final row counts and id ordering. Coverage:

- `KeepSpam(N)` keeps the lowest-id N spam labels
- `KeepHam(N)` keeps the lowest-id N ham labels
- `KeepSpam(0)` removes all spam (and `KeepHam(0)` symmetrically)
- `KeepDetectionResults(N)` works
- `KeepUserActions(N)` works
- `KeepMessages(0)` cascades through `message_edits`, `training_labels`,
  `detection_results`
- Negative count throws `ArgumentOutOfRangeException`
- `count > actual_canonical_rows` is a no-op for the excess (e.g.,
  `KeepSpam(200)` against a fixture with 100 spam rows leaves 100 surviving)
- Last-wins for repeated `Keep*` on same slice
- `Keep*` calls in different orders produce identical final state
- A single `ApplyAsync` exercising multiple reducers runs in one transaction
  (assert via querying intermediate state from a separate connection — should
  see canonical, not partial-reduced state, until commit)

### Phases 2–4 — Existing test suite as regression test

The pre-existing 623 integration tests are the regression detector for the
migration. Each Phase 3 commit must show the suite green before proceeding.
A test failure during a Phase 3 commit is a real bug — either:

- The test had a hidden assumption about minimal seed shape that canonical
  now violates → fix by reducing the appropriate slice
- The test was relying on data that legacy `Seed*Async` provided and canonical
  missed → fix by adding to the appropriate `canonical/*.sql`
- The test was using an additive pattern that's been removed → fix by
  rerouting through SUT writes

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

Record in PR description as `T0`. Per `tga_reference_integration_test_runtime`,
expected `T0` is ~5–8 minutes.

### Mid-migration timing (informational only)

The integration suite is expected to run **slower** between Phases 1 and 4B
because canonical contains more rows than today's minimal seeds and TRUNCATE+seed
is paying that bigger cost per test. This is the transient slowdown #463 is
designed to recover. Not gated on; just observed.

### Post-Phase-4 final timing

After Phase 4 lands, capture again as `T1`. Expected: `T1 < T0` by a meaningful
margin (the 5-minute claim in #463's body is a target, but the actual savings
depends on the specific TRUNCATE-vs-clone delta on this hardware). Document
both numbers in PR description for transparency.

---

## Migration tests deliberately untouched

Migration tests (`CascadeBehaviorTests`, `DataIntegrityTests`,
`InfrastructureTests`, `SequenceIntegrityTests`, `CriticalMigrationTests`)
test migrations themselves. They apply migrations one-at-a-time and assert at
intermediate schema versions. They cannot start from `empty_template` (which
has migrations applied to HEAD) or `golden_template` (also at HEAD plus
canonical data). They need a database with no migrations applied.

The existing `MigrationTestHelper.CreateAsync()` creates a fresh DB and lets
the test drive migrations forward. This stays unchanged.

A `bare_template` (no migrations, just `CREATE DATABASE`) is deliberately not
introduced. The math:

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
  beyond what `[UseEmptyDatabase]` + SUT writes can provide, expose
  `LoadCanonicalAsync` publicly at that point.
- **Additional reducers.** `KeepInvites`, `KeepWebNotifications`,
  `KeepUsernameHistory` are symmetric to the existing surface but have no
  current consumer. Add when the first consumer arrives.
- **`bare_template` for migration test optimization.** Discussed above, not
  worth it at current scale.
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
- [ ] `TestData/SQL/canonical/` exists with 15 per-table SQL files marked
      `EmbeddedResource`
- [ ] `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql`
      moved; `CriticalMigrationTests.cs` references the new path
- [ ] `GoldenReducePlan` exists with 5 `Keep*` methods + `ApplyAsync`
- [ ] `GoldenDataset.Reduce(AppDbContext)` exists
- [ ] `GoldenDataset.LoadCanonicalAsync(AppDbContext, CancellationToken)` exists
- [ ] `GoldenReducePlanTests.cs` exists with framework correctness coverage
- [ ] All existing tests still pass on legacy `Seed*Async` path
- [ ] `T0` baseline captured

### Phase 2
- [ ] `PostgresFixture.[OneTimeSetUp]` builds `empty_template` and
      `golden_template`, marks both as templates
- [ ] `UseTemplateFixtureAttribute` and `UseEmptyDatabaseAttribute` defined
- [ ] Per-test `[SetUp]` branches on attribute; legacy TRUNCATE+seed retained
      for unattributed tests
- [ ] All existing tests still pass

### Phases 3A–D
- [ ] Group A (3 files) migrated; suite green
- [ ] Group B (17 files) migrated; suite green
- [ ] Group C (2 files) migrated; suite green
- [ ] Migration tests adopt `GoldenDataset.*` constants; suite green

### Phase 4
- [ ] `find_symbol_usages` shows zero callers of every retired method
- [ ] 11 retired methods deleted from `GoldenDataset`
- [ ] 15 obsolete SQL files deleted
- [ ] Legacy TRUNCATE+seed branch removed from `PostgresFixture.[SetUp]`
- [ ] `UseTemplateFixtureAttribute` removed (definition + all usages)
- [ ] `UseEmptyDatabaseAttribute` retained
- [ ] All tests pass
- [ ] `T1` final timing captured; documented in PR description with `T0` for
      comparison

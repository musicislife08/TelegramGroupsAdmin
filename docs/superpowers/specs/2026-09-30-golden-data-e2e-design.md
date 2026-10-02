# Golden Data for E2E Tests

**Date:** 2026-09-30
**Branch:** `test/e2e-web-first-assertions`
**Scope:** Let new Playwright E2E tests run against the canonical golden dataset
(plus the reduce/mutate API) instead of seeding with `Test*Builder` classes. Extract
the golden infrastructure from `TelegramGroupsAdmin.IntegrationTests` into a shared,
NUnit-free test library that both test projects reference, add a per-test
golden/empty clone path to the E2E harness, then use it to close the E2E coverage
gaps found during the web-first-assertion sweep.
**PR target:** `develop`.

---

## Context

The same branch converted the E2E suite to retrying `Expect(...)` assertions and
banned one-shot Playwright reads (RS0030). A follow-up classification of every
unreferenced page-object member found three things:

1. Members targeting UI that no longer exists or duplicating used members (removed).
2. Members duplicated inline by tests (wired up).
3. Members pointing at **behaviour with no E2E test at all** — e.g. the Web Admin
   Accounts per-user actions, invite creation, the Reports Warn action, Audit
   filters by user id / issued-by, adding a training sample or a ban-celebration
   caption, trusting a user from the Users page. Several of these helpers could
   never have worked (selectors relying on a MudBlazor `data-testid` that 9.9 never
   renders, a renamed "Create Invite" → "Generate Invite" button, a "Ham" radio
   now labelled "CLEAN").

New tests for (3) must not add to the existing E2E seeding pattern.
`.claude/rules/integration-test-data.md` already applies the canonical-data rule to
E2E, but the E2E project has no access to the golden dataset:

- The golden infrastructure (`GoldenDataset`, reduce/mutate builders,
  `GoldenDatasetConstants`, canonical SQL) lives in `IntegrationTests/TestData`,
  `internal`, under a hardcoded embedded-resource prefix.
- E2E creates databases with plain `CREATE DATABASE` + startup migration
  (`TestWebApplicationFactory.EnsureDatabaseCreated`), and its shared server
  truncates every table per test so builders can re-seed.

Existing E2E tests keep their builders and base classes for now (explicit decision);
this design only adds a path for new tests.

## Goals

- One source of truth for canonical data, shared by integration and E2E tests.
- New E2E tests start each test from a fresh clone of `golden_template` (or
  `empty_template`), shaped by reduce/mutate before the app starts.
- Maximum determinism: a fresh app instance per test, no state reused across tests.
- Close the coverage gaps from the classification with real tests, written against
  golden data.

## Non-goals

- Migrating existing E2E tests off `Test*Builder` / `SharedE2ETestBase` truncation.
- Parallel E2E execution. E2E stays sequential: one browser, a fresh context per test.
- Changing integration-test behaviour (the extraction is a pure move).

## Architecture

### Components

**1. `TelegramGroupsAdmin.Testing.Golden`** (new class library, no NUnit)

Moved from `IntegrationTests/TestData` (public API, namespace
`TelegramGroupsAdmin.Testing.Golden`):

- `GoldenDataset` (load / `Reduce(ctx)` / `Mutate(ctx)`), `GoldenReducePlanBuilder`,
  `ChildReducePlan` (+ `GoldenReducePlanState`), `GoldenMutatePlanBuilder`,
  `TimestampShift`, `GoldenReducePlanException`, `GoldenDatasetConstants`.
- `SQL/canonical/*.sql` as embedded resources; the resource prefix is derived from
  the library assembly instead of the hardcoded `TelegramGroupsAdmin.IntegrationTests.TestData.`.

New:

- `GoldenTemplates.BuildAsync(string baseConnectionString, IDataProtectionProvider protector)`
  — builds `empty_template` (create + migrate) and `golden_template`
  (`TEMPLATE empty_template` + `LoadCanonicalAsync`), marks both `datistemplate`.
  Lifted from `PostgresFixture.BuildEmptyTemplateAsync` / `BuildGoldenTemplateAsync`
  (`Pooling=false` admin connections; Postgres refuses `TEMPLATE` while a session is
  connected).
- `GoldenTemplates.CloneAsync(baseConnectionString, GoldenTemplate template, string databaseName)`
  and `DropAsync(baseConnectionString, databaseName)` (terminate backends, then drop).
  Lifted from `MigrationTestHelper`.

Stays in IntegrationTests: `CanonicalSlices` (test oracle) and `TestData/Tests/*`
(the dataset self-tests: `LoadCanonicalAsyncTests`, `GoldenReducePlanTests`,
`GoldenMutatePlanTests`).

**2. IntegrationTests** references the library. `PostgresFixture` calls
`GoldenTemplates.BuildAsync` (still with its ephemeral data-protection provider);
`MigrationTestHelper` calls `CloneAsync` / `DropAsync`. Namespaces updated.
`IntegrationTests/CLAUDE.md` paths updated, and its known errors fixed: the
`rerun@` / `reshoot@` active/deleted labels are swapped, the password hash is
PBKDF2 (not bcrypt), and there is no active GlobalAdmin anchor in constants.

**3. E2E `E2EFixture`** builds both templates once per run, after starting its
Postgres container, using the shared E2E key ring (below).

**4. `TestWebApplicationFactory`** gains an optional template source: when given,
`EnsureDatabaseCreated` clones from that template instead of `CREATE DATABASE`
(startup migration is then a no-op). It also accepts the shared key-ring directory.
Existing constructor calls keep today's behaviour.

**5. `GoldenE2ETestBase`** (new; new tests only):

- `protected virtual GoldenTemplate Template => GoldenTemplate.Golden;`
  (override to `Empty` for empty-state tests).
- `protected virtual Task ArrangeDataAsync(AppDbContext ctx) => Task.CompletedTask;`
  — reduce/mutate and precondition read-backs, run **before** the app starts.
- `[SetUp]`: clone → `ArrangeDataAsync` → new `TestWebApplicationFactory`
  (clone name, shared keys) → `StartServer()` → new browser context + page.
- `[TearDown]`: context → factory dispose → `DropAsync`.
- Login helpers: cookie injection for canonical users via the app's
  `IAuthCookieService` and the user's stored security stamp — no SUT writes.
  `LoginAsOwnerAsync` (`owner@example.com`), `LoginAsAdminAsync`
  (`admin@example.com`), `LoginAsGlobalAdminAsync` (`ahead@canonical.test`, newly
  pinned in `GoldenDatasetConstants.WebUsers`). UI login for the no-TOTP canonical
  users (`machine@canonical.test` GlobalAdmin, `reshoot@canonical.test` Admin) using
  the shared canonical password constant.

**6. Shared key ring.** Canonical `configs.api_keys` is encrypted at template-build
time. E2E builds the template with
`DataProtectionProvider.Create(keysDir, b => b.SetApplicationName("TgSpamPreFilter"))`
over a per-run directory, and every golden-test app instance persists keys to the
same directory, so the app can decrypt canonical secrets. IntegrationTests keep their
ephemeral provider.

### Per-test flow

```
E2EFixture (once per run)
  └─ Postgres container → GoldenTemplates.BuildAsync(sharedKeyRing)
       empty_template  (migrated)
       golden_template (empty + canonical, api_keys encrypted with sharedKeyRing)

GoldenE2ETestBase (per test, sequential)
  SetUp
    1. CloneAsync(golden_template | empty_template → e2e_<guid>)
    2. ArrangeDataAsync(ctx)            ← optional override
         GoldenDataset.Reduce(ctx)...   e.g. KeepMessages(0) for an empty state
         GoldenDataset.Mutate(ctx)...   e.g. re-time rows to "today"
         read-back guard of any canonical-edit precondition
    3. new TestWebApplicationFactory(db: e2e_<guid>, keys: sharedKeyRing).StartServer()
    4. new browser context + page
  Test
    LoginAs(canonical user) → act via page objects → assert with Expect
  TearDown
    context → factory dispose → DropAsync(e2e_<guid>)
```

Arranging before the app starts is deliberate: the app caches config in HybridCache
(15 min) and performs startup-only writes (VAPID keys, default job configs), so data
changed after startup would be tested against stale state.

### Data rules for new E2E tests (preference order)

1. Canonical as-is; read expectations at runtime (e.g. chat names from `ManagedChats`).
2. Reduce/mutate in `ArrangeDataAsync`. **If the needed reduction or mutation does
   not exist, add it to `GoldenReducePlanBuilder` / `GoldenMutatePlanBuilder`**, with
   its own self-test in `GoldenReducePlanTests` / `GoldenMutatePlanTests`. No raw SQL
   or SUT writes in tests to fill the gap.
3. Flag-edit an unreferenced canonical row in the SQL, pin it in
   `GoldenDatasetConstants`, add a recipe to `IntegrationTests/CLAUDE.md` (Part 2,
   marked "canonical edit <date>"), and guard it with a read-back. Check what else
   asserts on that set first (`LoadCanonicalAsyncTests` exact counts).
4. An app write only when that write **is** the behaviour under test (e.g. clicking
   Disable on a user and asserting the result).

Canonical timestamps are frozen at the 2026-04-30 snapshot, so date-windowed pages
(dashboard, analytics) need an explicit `Mutate` re-time in the test's
`ArrangeDataAsync`; the base class does not do it implicitly.

## New coverage (after the infrastructure lands)

One self-contained commit per area, written by a Fable agent, high value first.
Each fixes the page-object members it uses (selectors noted below).

| Area | Behaviour | Notes |
|---|---|---|
| Web Admin Accounts | Per-user actions: edit permission, disable/enable, delete/restore, unlock, enable/disable/reset TOTP; confirm and cancel paths | `ActionMenuButton` selector is broken (relies on `data-testid`); retarget via the row's Actions cell |
| Web Admin Accounts | Create invite → link shown (`register?invite=`) + snackbar; register with the invite | Button is now "Generate Invite"; link is a read-only `Invite Link` text field; resolves the `RegistrationTests` TODO |
| Reports | Warn action on a moderation report | Dismiss / Delete as Spam / Ban already covered |
| Audit Log | Filter by Telegram user id; filter by issued-by | Helpers use `FillAsync` on a non-`Immediate` field — must commit (Enter/blur) |
| Content Detection | Add training sample (spam and clean); Source filter; training-mode toggle persists | 'Ham' radio is now "CLEAN" |
| Ban Celebrations | Add caption; duplicate-GIF warning (Keep Both / Cancel Upload); add GIF from URL | `NameInput` selector likely wrong (use label) |
| Background Jobs | Cancel in the job config dialog discards edits | |
| Users | Trust a user from the Users page; trusted/admin badges; view details; tab badges; warnings cell | Needs `aria-label`s in the app (below) |
| Analytics | Content Detection tab renders its content | Tighter locator than `ActiveTabContent` |
| Chats / Reports / Login (low) | Exam score and MC passed chips, chat id caption, bot status chip, refresh health, inactive chip, login page links | Component tests already cover some; E2E adds the integrated path |

Members still unreferenced after this step are deleted.

### App-side changes (separate commits)

- **Users page accessibility:** add `aria-label`s to the trust/untrust icon button and
  the Trusted / Admin badges in `UserInfoCell` (they have no accessible name today), so
  tests can target them by role.
- **`LoginVerify.razor`:** remove the `_successMessage` field and its `alert-success`
  block; it is only ever assigned `null` (since it was introduced) and can never render.

## Testing and verification

1. **Extraction is a pure move:** the full integration suite (~890 tests) passes before
   and after with identical counts, including the dataset self-tests.
2. **E2E template path:** smoke tests on `GoldenE2ETestBase` — canonical Owner logs in
   via cookie, the dashboard shows a canonical value read at runtime; an `Empty`
   template test shows the empty state.
3. **Isolation:** a test performs an app write; a following test asserts it is absent.
4. **Shared key ring:** the app reads the canonical API key (AI settings show it
   configured) with no decrypt error logged.
5. **New reduce/mutate operations:** each gets a failing self-test first.
6. **Existing E2E untouched:** the full E2E suite stays green; existing base classes
   have no diff.
7. **New coverage tests:** every new assertion is mutation-checked (deliberately broken
   expectation → test fails), results reported. No RS0030 suppressions unless a value
   genuinely has to be read, with a reason.

## Risks

- **Startup cost:** a fresh app per test costs ~1.5–2 s; acceptable for determinism and
  limited to new tests.
- **Fixed canonical emails vs. login rate limit:** UI logins are per-app-instance, and
  each test has its own instance, so the in-memory limiter never accumulates across tests.
- **Canonical TOTP:** `owner@` / `admin@` have TOTP enabled with no secret, so UI login
  for them lands on `/login/setup-2fa`; tests use cookie injection for them and the
  no-TOTP users for UI-login flows.
- **Canonical edits ripple:** flag-edits can break integration assertions on exact
  counts; the data-rule checklist requires checking `LoadCanonicalAsyncTests` and grepping
  the affected set before editing.

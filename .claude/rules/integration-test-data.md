---
paths:
  - "TelegramGroupsAdmin.IntegrationTests/**/*.cs"
  - "TelegramGroupsAdmin.IntegrationTests/**/*.sql"
  - "TelegramGroupsAdmin.E2ETests/**/*.cs"
  - "docs/superpowers/plans/**/*.md"
  - "docs/superpowers/specs/**/*.md"
---

# Integration-test data rules (MANDATORY)

The canonical dataset under `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/` is
**scrubbed real prod data**, cloned per test as a fresh PostgreSQL template. A test's job is to
assert its logic against that real data. These rules apply to every integration and E2E test,
and to every spec or plan that specifies one.

## The hard rule

- **Never seed a precondition.** No SUT write method as setup (`GetOrCreateAsync`, `UpsertAsync`,
  `SetBanStatusAsync`, `TrustUserAsync`, `InsertAsync`, `CreateAsync`, …), no `ctx.<Table>.Add(...)`,
  no raw `INSERT`. A SUT write appears in a test **only when that write is the assertion subject**.
  Do not launder an out-of-place write by asserting on it afterwards.
- **Never add rows to canonical to get a shape.** There are plenty of users: find an existing
  canonical row that no test or doc references (`grep -rn <id> TelegramGroupsAdmin.*Tests docs`
  → only the SQL itself) and **flag-edit it in place** so the dataset stays realistic. Keep the
  story plausible (e.g. a timed-out joiner later trusted; a 12h temp-ban whose flag was never cleared).
- **Pin every anchor in `TestData/GoldenDatasetConstants.cs`** (nested test-domain class, XML doc on
  each constant) and add a Part 2 recipe to `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`, marked
  "(canonical edit <date>)" when a row was edited.
- **Guard edited preconditions.** A test that relies on an edited row reads it back first and asserts
  the flags, so a later canonical change fails loudly instead of passing vacuously.
- **Read expectations at runtime** (`ctx.Table.CountAsync(...)`, chat names from `ManagedChats`)
  instead of hard-coding counts, so tests survive canonical edits.
- **Before flipping flags, check what else asserts on that set**: `TestData/Tests/LoadCanonicalAsyncTests`
  pins exact counts for some tables; grep for exact-count assertions on the affected tab/list.

## Preference order for a precondition

1. A canonical row that already has the shape (default; mine the SQL first).
2. Flag-edit an unreferenced canonical row in place (see above).
3. Nothing else. Synthetic rows and in-test seeding are not options for business-logic tests.
   Empty-template tests are reserved for migration / infrastructure / schema fixtures.

## When you are writing a plan or spec

The Testing section must name the canonical anchor (id + username) for every integration test and
say which rows, if any, get edited and how. Test code in a plan that calls a SUT write method as
setup is a plan defect — fix it before dispatching an implementer.

## Escalate, don't work around

If canonical lacks a shape and no unreferenced row can carry it, or a template build / FK / sequence
problem appears, STOP and report (DONE_WITH_CONCERNS / BLOCKED). Never change a test's assertion or
switch code paths to make it pass.

Dataset orientation, table counts, and anchor recipes: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`.

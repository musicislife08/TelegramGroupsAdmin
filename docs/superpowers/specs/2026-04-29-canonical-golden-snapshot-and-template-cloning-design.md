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
- A first-pass audit classified the 53 IntegrationTests classes as roughly
  14 canonical-consumers / 30 empty-consumers / 1 mixed / 4 ambiguous /
  8 migration-tests, where "empty-consumer" was defined loosely as "doesn't
  call `GoldenDataset.Seed*Async` today." That classification is
  **informational only** — the strict universal boundary rule (see
  Architecture > Boundary rule below) makes most of those 30 hidden
  canonical-consumers, because they assemble setup data via direct
  `Add`/`INSERT` patterns the new rule disallows. A second-pass audit
  under the strict rule is a Pre-Phase-1 prerequisite — its output also
  feeds the canonical bootstrap (see Migration plan).
- UnitTests, ComponentTests, and E2ETests are mostly clean of canonical
  collision risks. Three low-priority constant-hygiene items surfaced are
  deferred to follow-up issues.

**There is no "default" data shape.** Each test class — sometimes each
*test method* — explicitly picks via the `MigrationTestHelper` method it
calls. Empty is not an escape hatch; it's a comparable consumer group.

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
database cloned from `empty_template` (migrations only, no data). Used in
two specific cases:

- Tests asserting on empty-state behavior ("X returns null when no rows"),
  where the empty state IS the assertion.
- Tests where the SUT under test is a write method, and the test exercises
  that write path from a clean slate. The SUT call IS the setup.

The empty path is **not** a license to assemble ad-hoc test data via
`Add(new XxxDto)` or raw `INSERT`. That pattern is forbidden under the
universal boundary rule. If a test needs preexisting data that isn't
exercising a write path, it should be on the canonical path with `Reduce`
if needed — never on the empty path with hand-rolled setup.

```csharp
[SetUp]
public async Task Setup()
{
    _testHelper = new MigrationTestHelper();
    await _testHelper.CreateDatabaseFromEmptyTemplateAsync();
    // Clean migrated DB. Test either asserts on emptiness or exercises a
    // write SUT — no Add/INSERT setup data.
}
```

**Path 3 — Migration tests.** Stay on existing `MigrationTestHelper` methods
(`CreateDatabaseAndApplyMigrationsAsync` for HEAD-schema, `CreateDatabaseAndMigrateToAsync(targetMigration)`
for intermediate-schema). Migration tests adopt `GoldenDataset.*` constants
for ID literals in their inline data.

### Selection rule for reductions

Count-only, deterministic by stable ordering. Each `Keep*` method picks
the rows it keeps using a fixed `ORDER BY` on its target table:

- `KeepDetectionResults`, `KeepUserActions` — `ORDER BY id ASC` (real
  surrogate bigserial PKs).
- `KeepSpam`, `KeepHam` — `training_labels` has no surrogate PK; the
  composite key is `(chat_id, message_id)`. Reducer uses
  `ORDER BY chat_id ASC, message_id ASC` to keep ordering deterministic
  across all chats. Caveat: because `message_id` is per-chat, "lowest
  ordering" prefers the earliest message in the lowest-id chat first,
  then walks forward — not a single global sequence. Tests that need
  specific labels should pin them via `GoldenDataset.*_Id` constants
  rather than relying on which N labels are kept.
- `KeepMessages` — `messages` likewise has no surrogate PK; same
  `ORDER BY chat_id ASC, message_id ASC`.

The same "ordering by composite key" approach is what the database would
use anyway for these tables. No `ORDER BY` randomness, no content-aware
selection.

No predicate selectors. No per-row content selection.

### Boundary rule (universal, code-review enforced)

A test's setup data comes from one of three patterns, regardless of which
`Create*` method the test calls:

1. **Canonical clone + optional `Reduce`** — for tests needing pre-existing
   data. The cloned `golden_template` provides canonical state; `Reduce`
   constrains it to a specific shape if needed.
2. **Empty clone, no setup** — for tests asserting on empty-state behavior
   (e.g., "X returns null when DB has no rows"). The cloned `empty_template`
   provides migrations only; the test's assertion IS the empty-state check.
3. **SUT-driven writes when the SUT is a write method** — `repo.AddXxxAsync(...)`
   etc., used to test that the write itself works. The setup IS the test.
   Allowed on either canonical or empty paths.

A test **never** directly calls `_dbContext.Add(new SomeDto { ... })`
followed by `SaveChangesAsync` for setup, and **never** runs raw
`INSERT INTO` for setup. This rule applies to both canonical-consumers
*and* empty-consumers.

If a row needs to exist for a test that isn't exercising the write path,
that row belongs in canonical — every test gets it, IDs are referenced by
constants, drift is impossible. The differentiator is whether the SUT's
write path is part of what the test is exercising:

- "Verify `AddUser` saves correctly" → call `repo.AddUserAsync(...)` (the
  write IS the test), then assert.
- "Verify `GetUserById` returns the right user" → user should be in
  canonical; test calls `repo.GetUserByIdAsync(GoldenDataset.TelegramUsers.User1_TelegramUserId)`
  and asserts on canonical's User1 properties.

For *read* tests, arranging via canonical keeps the setup independent of
the read path's correctness. For *write* tests, you have no choice — the
SUT IS the arrange step.

---

## Canonical bootstrap from local database

The 17 `canonical/*.sql` files are NOT authored by hand. They are
produced **once, before Phase 1 begins**, by a collaborative export-and-
sanitize task run interactively against the running local development
database. The maintainer and Claude work this through together: write
sampling queries against the live DB, apply the sanitization rules
below, rewrite IDs into the canonical ranges, `pg_dump` per table, spot-
check the output, then commit the resulting SQL files as part of the
Phase 1 commit.

**No bootstrap script is committed.** This is intentional: it is a one-
shot task, not a maintained tool. The procedure is documented here for
context — and for any future re-bootstrap if a schema change ever
invalidates the existing canonical (rare) — but the only artifact that
lands in the repo is the 17 canonical SQL files themselves. After Phase
1 they are maintained like any other code: small edits for new features
as they ship.

### Sampling targets

Local DB holds ~20,000 messages and proportionally large volumes of
related data. Canonical samples a small but representative slice:

- **Messages: 400 total**
  - 100 explicit ham (has `training_labels` row, class=Ham)
  - 100 implicit ham (no training label, content benign)
  - 100 explicit spam (has `training_labels` row, class=Spam)
  - 100 implicit spam (no training label, content spammy)

  The implicit/explicit split preserves test coverage for both
  labeled-training and unlabeled-detection paths. 100 of each type gives
  reducers enough headroom to satisfy the existing high-spam ML training
  test (which expects ~90–100 spam samples post-dedup) while staying
  small enough to keep `pg_restore` of the template fast.
- **Training labels: 200 total** — derived from the 100 explicit ham +
  100 explicit spam messages above (one row per explicit message). This
  is the corpus the `KeepSpam` / `KeepHam` reducers operate on.
- **Telegram users: ~30–50 total** — every sender of a sampled message,
  plus admins of any chat referenced, plus users referenced by
  `chat_admins`, `username_history`, and `user_actions` rows.
- **Admin users (`users` table): ~5** — the actual admin set on the
  `users` table (.NET Identity-style, string GUID PK). Small group, no
  sampling needed; bootstrap maps the first ~4 onto `User1_Id`–`User4_Id`
  constants directly. (Earlier drafts called this `web_users`; the table
  is named `users`.)
- **Managed chats: 2–4** — at minimum the chat the sampled messages came
  from, plus any chats referenced by `linked_channels` or admin scope.
- **Configs: 1 row** — a representative `chat_id=0` global config, with
  all five DataProtection-encrypted columns (`api_keys`,
  `passphrase_encrypted`, `telegram_bot_token_encrypted`,
  `vapid_private_key_encrypted`, `user_api_hash_encrypted`) stripped to
  NULL. JSONB columns are kept but with secret/identifying values
  neutralized (see sanitization rules). `LoadCanonicalAsync` post-step
  fills in any encrypted column the test path needs populated.
- **Content detection configs: ~3** — global plus a couple chat overrides.
- **Detection results, user_actions, message_edits, invites,
  web_notifications, username_history:** sampled as the FK-supporting set
  for the above. No fixed target — bootstrap takes whatever rows are
  needed to keep canonical FK-coherent.

Final canonical row counts will be in the low hundreds — much smaller
than full prod, much larger than today's hand-crafted ~50-row fixtures.

### Sanitization rules

Applied as SQL transformations on a temporary copy of the sampled data
before `pg_dump`:

| Table / column | Rule |
|---|---|
| Real surrogate `id` / FK columns (DB-generated bigserial PKs — `detection_results.id`, `user_actions.id`, `message_edits.id`, `invites.id`, `web_notifications.id`, `username_history.id`, `linked_channels.id`, `configs.id`, `content_detection_configs.id`, `chat_admins.id`, etc.) and their FK references | Rewrite to deterministic canonical sequences starting at 1 within each table. These IDs are not exposed by `GoldenDataset.*` constants today, so renumbering is safe. |
| `users.id` (string `nvarchar(450)` GUID-style PK on the `users` admin table — .NET Identity-style ID, NOT a bigserial) and `users.invited_by` self-FK | **Pin to existing constants where they exist.** `GoldenDataset.Users.User1_Id = "b388ee38-0ed3-4c09-9def-5715f9f07f56"`, `User2_Id = "921637d5-0f65-4c66-b143-6f057dd06a1c"`, etc. — bootstrap maps real local-DB admin rows onto these specific constant GUIDs (existing constants stay unchanged). Any `users` rows not pinned by a constant get fresh deterministic GUIDs (`00000000-0000-0000-0000-0000000000NN`). |
| Telegram-semantic IDs (`managed_chats.id` real Telegram chat ID — supergroups/channels are negative; `telegram_users.id` real Telegram user ID — positive; `messages.message_id` int per-chat sequence Telegram assigns; `(chat_id, message_id)` is the composite uniqueness key for both `messages` AND `training_labels` — neither table has a surrogate PK) | **Pin to existing constants where they exist.** `GoldenDataset.TelegramUsers.User1_TelegramUserId = 100001`, `User2_TelegramUserId = 100002`, etc., and `GoldenDataset.ManagedChats.MainChat_Id = -1001322973935` are preserved unchanged — bootstrap maps real local-DB rows onto these constant values. Any unpinned chats/users get clearly-fake values that can never collide with a real backup: chat IDs in `-1009000000000..-1009000000099`, telegram user IDs in `9000000000..9000000099`. **Telegram `message_id` is per-chat, not globally unique** — the same value (1, 2, 3, …) appears independently in every chat. Bootstrap preserves this: each canonical chat gets its own `message_id` sequence starting near 1; `message_id` values WILL repeat across chats. Tests filtering `WHERE message_id = N` without scoping `chat_id` see multiple rows by design. Per-chat ordering stays monotonic in original send order. |
| `telegram_users.username` / `first_name` / `last_name` for **non-spammer** users | Replace with synthetic (`user_<seq>`, `Test`, `User`) |
| `telegram_users.username` / `first_name` / `last_name` for **spammer** users | **Keep verbatim.** Spam personas are intentional ML training signal; unpinned rewritten IDs put them in the fake `9000000000+` range so they don't map back to the real account. |
| `messages.message_text` AND `messages.urls` for **ham** messages (explicit + implicit) | Replace `message_text` with `repeat(' lorem', length(message_text) / 6)` — preserves message length and tokenization shape, kills content. Set `messages.urls` to NULL on the same rows (lorem ipsum carries no URLs). |
| `messages.message_text` AND `messages.urls` for **spam** messages (explicit + implicit) | **Preserve verbatim except for live URL hostnames.** Both columns get the same hostname rewrite: any URL's hostname becomes a deterministic `.invalid` domain (`https://bad.example/path?a=1` → `https://spam-host-01.invalid/path?a=1`). Scheme, path, query string, and surrounding text are preserved in `message_text`; `messages.urls` carries the rewritten URL list. Rationale: spam realism drives ML/detection fidelity; the only mandatory mutation is preventing committed fixtures, test output, or PR review from carrying clickable links to active malware/phishing infrastructure. Other identifiers in spam (handles, wallet addresses, phone numbers, invite codes, etc.) are deliberately kept intact for realism — these are public spammer artifacts, not protected PII. |
| `messages.media_local_path` / `messages.photo_local_path` / `messages.photo_thumbnail_path` / `messages.media_file_name` for **all** messages | Strip to NULL. These are local-filesystem paths and original filenames from the dev box; not test-relevant and they leak local layout. |
| `message_edits.old_text` / `message_edits.new_text` for **all** messages | Sanitize using the same rules that apply to the parent `messages.message_text`: ham edits (where the edit belongs to a ham message) get `repeat(' lorem', length(text) / 6)`; spam edits get verbatim text with URL hostnames rewritten. The associated `*_content_hash` columns are recomputed from the sanitized values (see "derived hashes" row below). |
| `message_translations.translated_text` for **all** translations | Sanitize using the same ham/spam rule the parent message gets. Recomputed `similarity_hash` lands in the "derived hashes" row. |
| Derived hash columns: `messages.content_hash`, `messages.similarity_hash`, `message_translations.similarity_hash`, `message_edits.old_content_hash`, `message_edits.new_content_hash` | **Recompute deterministically from the sanitized inputs and embed the new values in the SQL fixture, matching the runtime call shape exactly.** `HashUtilities.ComputeContentHash` calls `.ToLowerInvariant()` on both args and throws on null; production coalesces with `?? ""` before calling — bootstrap MUST do the same. `messages.content_hash = ComputeContentHash(message_text ?? "", urls ?? "")`. `message_edits` has NO url columns; production extracts URLs from edit text, JSON-serializes them, and hashes — the bootstrap mirrors this: `ComputeContentHash(old_text ?? "", oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "")` (same for `new_text`). See bootstrap workflow step 5 for the full code shape. Stale hashes (from pre-sanitization text) would break dedup and similarity tests; NULLed hashes would force tests to reseed them. Recomputed-and-embedded gives canonical the same self-contained, no-runtime-work property the rest of the SQL has. |
| `users.email` / `users.normalized_email` / `users.password_hash` / `users.security_stamp` (the admin `users` table) | Replace with synthetic; password hash → known-good test value or NULL; security stamp → fresh deterministic GUID. |
| `managed_chats.title` / `username` | Replace with synthetic |
| `configs` encrypted columns (all five: `api_keys`, `passphrase_encrypted`, `telegram_bot_token_encrypted`, `vapid_private_key_encrypted`, `user_api_hash_encrypted`) | **Strip all to NULL.** None of the local DB's ciphertext can be decrypted under the test `EphemeralDataProtectionProvider`'s keyring; carrying it forward would produce undecryptable canonical rows. `LoadCanonicalAsync` post-step inserts canonical encrypted values via `SharedDataProtectionProvider` for any column the test path needs populated. |
| `configs` JSONB columns containing tokens, URLs, contact emails, account IDs, or other credentials (e.g., values nested inside `sendgrid_config`, `web_push_config`, `ai_provider_config`, `telegram_bot_config`, `user_api_config`, `bot_protection_config`, `file_scanning_config`) | Strip secret/identifying values; preserve structural shape (keys present, values neutralized to synthetic placeholders). Spot-check during bootstrap to confirm no live tokens slipped through. |
| Any user-supplied content not covered above | Sanitize to synthetic or strip |

### Bootstrap workflow

1. Connect to local DB (read-only).
2. Run sampling queries to pick target rows per the table above.
3. Apply sanitization SQL on a temporary copy.
4. Rewrite IDs to canonical sequences; update FKs accordingly.
5. **Recompute derived hashes against the sanitized inputs.** Run the
   app's actual hash routines (whatever ships in the app code at the
   time) and update the temporary copy with the new hash values so
   `pg_dump` captures them as literals. Match the runtime call shape
   exactly — `HashUtilities.ComputeContentHash` calls `.ToLowerInvariant()`
   on both inputs, so it throws on null; production sites coalesce nulls
   to `""` before calling. The bootstrap MUST use the same idiom.

   - `messages.content_hash` — call
     `HashUtilities.ComputeContentHash(message_text ?? "", urls ?? "")`
     on the sanitized columns. Both `message_text` and `urls` are taken
     directly from the `messages` row (spam: verbatim text + hostname-
     rewritten urls; ham: lorem-ipsum text + NULL urls — coalesced to
     `""` for the hash).
   - `messages.similarity_hash` — SimHash over the sanitized `message_text`
     (or empty string if NULL — match the runtime SimHash call site's
     null-handling).
   - `message_translations.similarity_hash` — SimHash over the sanitized
     `translated_text`.
   - `message_edits.old_content_hash` / `message_edits.new_content_hash`
     — `message_edits` has NO url columns; production extracts URLs
     from `old_text` / `new_text` via the URL extractor, JSON-serializes
     the extracted list, and hashes text + serialized list. Bootstrap
     must do the same to produce runtime-bit-identical hashes:
     ```
     var oldUrls = UrlUtilities.ExtractUrls(sanitized_old_text);  // List<string>? — null when no URLs found
     var oldUrlsJson = oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "";
     var oldContentHash = HashUtilities.ComputeContentHash(sanitized_old_text ?? "", oldUrlsJson);
     // same shape for new_*
     ```
     Note the URL extraction runs against the **already-sanitized** text:
     for spam this means extraction picks up the `.invalid` hostnames
     from the sanitization step (still a valid URL shape, will extract
     normally); for ham `ExtractUrls` returns null (lorem ipsum has no
     URLs), which the `?? ""` coalesce above turns into `""` for the
     hash — bit-identical to what production produces for any other
     URL-less message. The null-vs-empty distinction matters: an empty
     list would JSON-serialize to `"[]"` and produce different hashes.

   This is the only step in the bootstrap that calls into application
   code — everything else is pure SQL.
6. `pg_dump --data-only --column-inserts --table=<each table>` to produce
   per-table SQL fixtures.
7. Manual spot-check of the output, focused on (a) URL/hostname coverage
   on spam messages, (b) accidental non-spammer leakage in any column not
   covered by the rules table, (c) encrypted ciphertext that may have
   slipped through, (d) JSONB credential/token leakage. Realistic spam
   personas and intact spam wording are expected and not flagged.
8. Commit the 17 `canonical/*.sql` files as part of the Phase 1 commit.
   Do not commit the sampling/sanitization SQL or any tooling used along
   the way; the SQL files are the only artifact.

---

## SQL fixture layout

### Origin

The 17 `canonical/*.sql` files are committed as ordinary SQL files
produced by the one-time bootstrap above. After Phase 1, they are edited
directly for incremental additions (new tables, new columns, new
test-relevant rows). Treat them as code, not as derived artifacts.

### Final structure

```
TelegramGroupsAdmin.IntegrationTests/TestData/SQL/
  canonical/                              ← loaded once into golden_template
    01_users.sql                          ← admin users (.NET Identity-style); was misspelled `web_users` in earlier drafts
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
    16_configs.sql                        ← all 5 encrypted cols NULL; filled by LoadCanonicalAsync post-step
    17_message_translations.sql           ← child of messages AND message_edits; loaded last among message-children

  migration/                              ← never loaded by canonical path
    40_pre_migration_impersonation_alerts.sql
```

The `.csproj` `EmbeddedResource` glob is updated to recursive
(`<EmbeddedResource Include="TestData\SQL\**\*.sql" />`) so subfolder
contents are included. Loading order under `canonical/` follows lexicographic
filename order, FK-correct by construction.

### Encrypted columns and the shared keyring

The `configs` table has **five** DataProtection-encrypted columns:
`api_keys`, `passphrase_encrypted`, `telegram_bot_token_encrypted`,
`vapid_private_key_encrypted`, and `user_api_hash_encrypted`. It also
has several plain JSONB columns (`backup_encryption_config`, etc.).
Because encryption output is non-deterministic (includes nonce/
timestamp), the same plaintext produces different ciphertext on every
run. A static literal ciphertext in a SQL file cannot work — yesterday's
ciphertext is undecryptable by today's keyring.

The mechanism:

- `canonical/16_configs.sql` seeds the row with all plain columns set
  (`id`, `chat_id`, `backup_encryption_config`, `created_at`, etc.) and
  every encrypted column NULL. Most of the canonical config data lives
  in the SQL fixture as plain JSONB, where it belongs.
- After SQL fixtures load, `LoadCanonicalAsync(context, dataProtection, ct)`
  runs a hardcoded C# post-step that encrypts the canonical plaintext
  values for whichever subset of the five encrypted columns the test
  path needs populated, using `dataProtection`, and issues parameterized
  `UPDATE configs SET <col> = $1 WHERE id = $2` statements to fill them
  in. Columns not exercised by any canonical-consumer test (e.g.,
  `vapid_private_key_encrypted` if no test path touches web push) are
  left NULL.
- The `dataProtection` argument is `PostgresFixture.SharedDataProtectionProvider`
  (an `EphemeralDataProtectionProvider`) — built once per test session.
- Every test that decrypts canonical-seeded values must use the same
  `SharedDataProtectionProvider` instance. Tests today that build their own
  ephemeral provider with a fresh GUID-named key directory swap that
  registration to
  `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`
  during Phase 3 migration.

If a future canonical row needs other encrypted columns (e.g., a new
encrypted column in some other table), the same pattern extends: SQL
fixture seeds the row with the encrypted column NULL, then
`LoadCanonicalAsync` `UPDATE`s the column with a freshly-encrypted value.

The narrow exception: any test that intentionally validates independent
keyring isolation (encrypt under one key, fail to decrypt under another)
keeps its per-test ephemeral provider. The audit found no such tests today,
but the pattern is preserved as an opt-out.

### Relationship to the old `00_base_*.sql` / scenario fixtures

Today's `TestData/SQL/` contains 15 obsolete files (the `00_base_*.sql`
through `21_*.sql` and `30_*` / `50_*` / `60_*` scenario files). Under
the new model, those files are deleted in Phase 4 and replaced by the
canonical layout above. There is no per-row migration mapping between
old and new — canonical comes from a fresh bootstrap export, not from
splicing together today's hand-crafted scenario data.

The old data was small, stable, and deterministic; the new canonical is
larger, realistic-shape, and bootstrapped. Tests that referenced
specific old IDs (e.g., dedup messages at `95001`–`95022`) get updated
during Phase 3 to reference whatever IDs the bootstrap assigned to the
equivalent canonical rows. The `GoldenDataset.*_Id` constants are the
mapping layer: tests use the constants, the bootstrap fills them in.

### Canonical contract for `09_user_actions.sql`

Every seeded `user_actions` row has a non-null `MessageId` (and
corresponding `ChatId`). This guarantees that any `user_action` row a
test sees with a null `MessageId` was created by a prior `KeepMessages`
reduction's SetNull cascade — a clean signal for any future `Prune*`
cleanup (out of scope for v1).

The bootstrap sampling enforces this by only sampling `user_action` rows
that have a non-null message FK. Rows in production with null `MessageId`
(if any) are dropped during sampling.

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
    // Public per-test API: returns the stage-1 builder for tests that need a
    // constrained shape from canonical. Synchronous factory; no DB hit until
    // ApplyAsync. Once any child reducer is called, the type transitions to
    // ChildReducePlan and KeepMessages is no longer accessible.
    public static GoldenReducePlanBuilder Reduce(AppDbContext context);

    // Internal-ish: loads canonical/*.sql into the target context.
    // Used by PostgresFixture in [OneTimeSetUp] to build golden_template.
    // Used by GoldenReducePlanTests to exercise Reduce against a manually-
    // loaded canonical state before the template infrastructure exists.
    // Not intended for production test setup.
    public static Task LoadCanonicalAsync(AppDbContext context, IDataProtectionProvider dataProtection, CancellationToken ct = default);
}

// Two-stage type-state builder. The type system rules out the wrong-order
// FLUENT CHAIN: once any child reducer is called, KeepMessages is no longer
// reachable via the returned type, so `Reduce(ctx).KeepHam(5).KeepMessages(5)`
// won't compile. The type system does NOT prevent registering parent ops
// after children through intermediate variables (since the underlying plan
// is a shared mutable object — see "Caveat: shared mutable plan" below).
// Topological execution at ApplyAsync time is the runtime backstop that
// makes those edge cases produce the same result as the canonical chain.

// Stage 1 — returned by Reduce(ctx). All five reducers reachable.
public sealed class GoldenReducePlanBuilder
{
    // Parent reducer — staying in stage 1 (KeepMessages still reachable for last-wins).
    public GoldenReducePlanBuilder KeepMessages(int count);

    // Child reducers — transition to ChildReducePlan (stage 2).
    public ChildReducePlan KeepSpam(int count);
    public ChildReducePlan KeepHam(int count);
    public ChildReducePlan KeepDetectionResults(int count);
    public ChildReducePlan KeepUserActions(int count);

    public Task ApplyAsync(CancellationToken ct = default);
}

// Stage 2 — returned by any child Keep* call. KeepMessages is gone.
public sealed class ChildReducePlan
{
    public ChildReducePlan KeepSpam(int count);
    public ChildReducePlan KeepHam(int count);
    public ChildReducePlan KeepDetectionResults(int count);
    public ChildReducePlan KeepUserActions(int count);

    public Task ApplyAsync(CancellationToken ct = default);
}

// Result for fluent chains:
//   ✅ Reduce(ctx).KeepMessages(5).KeepDetectionResults(2).ApplyAsync()  // valid
//   ✅ Reduce(ctx).KeepSpam(5).KeepHam(5).ApplyAsync()                   // valid (no parent)
//   ❌ Reduce(ctx).KeepHam(5).KeepMessages(5).ApplyAsync()               // compile error

// Caveat: shared mutable plan. KeepX methods mutate-and-return a reference
// to the same underlying plan object, so this still compiles and works:
//   var p = Reduce(ctx);             // p : GoldenReducePlanBuilder
//   var c = p.KeepDetectionResults(2); // c : ChildReducePlan
//   p.KeepMessages(5);                // legal: p still has the parent type
//   await c.ApplyAsync();             // applies BOTH operations on the shared plan
// The compile-time guarantee covers the FLUENT chain, not every possible
// reference path. Topological execution makes this produce the right result
// regardless. We don't try to design out this case — it's uncommon in
// practice and benign under topological sort.
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
`GoldenDataset.Users.UserN_Id` (admin user GUIDs on the `users` table),
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

Five reducers across two stages.

```csharp
// Stage 1 — GoldenReducePlanBuilder (parent stage):
GoldenReducePlanBuilder KeepMessages(int count);  // messages — parent of cascading children
ChildReducePlan         KeepSpam(int count);      // training_labels WHERE class=Spam
ChildReducePlan         KeepHam(int count);       // training_labels WHERE class=Ham
ChildReducePlan         KeepDetectionResults(int count);
ChildReducePlan         KeepUserActions(int count);

// Stage 2 — ChildReducePlan (post-parent):
ChildReducePlan KeepSpam(int count);
ChildReducePlan KeepHam(int count);
ChildReducePlan KeepDetectionResults(int count);
ChildReducePlan KeepUserActions(int count);
// (no KeepMessages — calling a child reducer transitions out of the parent stage)
```

### Mental model: cascade-narrowing, not additive

Child reducers do NOT independently specify their slice. They **further
restrict** whatever survived `KeepMessages`'s cascade. The cascade is
where the wide cuts happen; child reducers are for tightening.

`KeepMessages(5)` deletes 395 messages. Postgres FK cascades automatically:

- `message_edits` for those 395 → deleted (Cascade)
- `training_labels` for those 395 → deleted (Cascade)
- `detection_results` for those 395 → deleted (Cascade)
- `message_translations` for those 395 → deleted (Cascade)
- `user_actions` for those 395 → `MessageId`/`ChatId` SetNull (rows survive)

So after `Reduce(ctx).KeepMessages(5).ApplyAsync()`:

- 5 messages remain.
- Children of those 5 messages remain at their natural counts (whatever
  the canonical bootstrap sampled).
- `user_actions` retains all original rows; the 395 messages' actions
  carry null `MessageId`/`ChatId`.

If you want to **further restrict** children beyond what cascade left,
chain a child reducer:

| Test wants | Plan |
|---|---|
| 5 messages + their natural detection results | `KeepMessages(5)` |
| 5 messages, but only 2 detection results among them | `KeepMessages(5).KeepDetectionResults(2)` |
| 5 messages, no training labels at all | `KeepMessages(5).KeepHam(0).KeepSpam(0)` |
| 0 messages AND 0 user_actions (cascade only SetNulls) | `KeepMessages(0).KeepUserActions(0)` |
| All messages, only 5 detection results | `KeepDetectionResults(5)` |

If a future test needs to clean up SetNull orphans created by
`KeepMessages` (rows with null `MessageId`) without removing
legitimately-canonical non-orphan `user_actions`, add a
`PruneUserActions` method targeting that case. The verb distinction
(`Keep*` for state, `Prune*` for orphan cleanup) is intentional. Out of
scope for v1.

### `KeepMessages` FK cascade behavior (full reference)

Per `AppDbContext.cs`:

- `message_edits` → `Cascade`
- `training_labels` → `Cascade`
- `detection_results` → `Cascade`
- `message_translations` → `Cascade`
- **`user_actions` → `SetNull`** (rows survive; FKs go null)

### Validation rules

- `count >= 0`. Negative throws `ArgumentOutOfRangeException` synchronously
  at `Keep*` invocation time (no DB knowledge required).
- **No upper-bound validation against canonical or post-cascade row
  counts.** `Keep*` uses natural `LIMIT` semantics in both directions:
  - Canonical-bound: `KeepSpam(500)` against a canonical containing 100
    spam `training_labels` rows silently keeps all 100.
  - Cascade-bound: `KeepMessages(5).KeepDetectionResults(50)` when the
    cascade left only 12 detection rows surviving silently keeps all 12.

  Same rule, same reasoning: the developer can't easily predict either
  bound (canonical drifts; cascade-survivor counts depend on which
  specific N messages survive). The test's own assertions surface the
  mistake at the assertion site — `Has.Count.EqualTo(50)` failing with
  actual 12 is clearer than a `[SetUp]` throw saying the same.
- Calling the same `Keep*` twice is last-wins, no error.
- Slices not mentioned retain whatever the cascade (or canonical, if no
  parent reduction) leaves them at.

### Execution semantics

- `Reduce(ctx)` is synchronous, returns a `GoldenReducePlanBuilder` with
  no DB work performed.
- `Keep*` methods mutate-and-return-this on the underlying plan; the
  return *type* may transition (parent → child stage) but the underlying
  plan instance is the same.
- `ApplyAsync(ct)` opens a transaction on the context and runs operations
  in **fixed topological order, independent of registration order**:
  1. `KeepMessages` (if registered) — runs first; FK cascade fires.
  2. `KeepSpam` / `KeepHam` (training_labels reductions on whatever
     survived the cascade).
  3. `KeepDetectionResults` (on whatever survived the cascade).
  4. `KeepUserActions` (on whatever survived the cascade — including
     SetNull orphans from step 1).

  Then commits. The compile-time type-state ordering means the user's
  chain order already matches this order in practice; the topological
  sort is the runtime guarantee.

  The DELETE shape varies by table. **The slice predicate appears in
  both the inner SELECT and the outer DELETE** so unrelated rows are
  never affected:

  - Tables with a surrogate `id` PK and no slice predicate
    (`detection_results`, `user_actions`):
    ```sql
    DELETE FROM <table>
    WHERE id NOT IN (
      SELECT id FROM <table>
      ORDER BY id ASC LIMIT @n
    );
    ```
  - Tables with composite `(chat_id, message_id)` PK and no slice
    predicate (`messages`):
    ```sql
    DELETE FROM messages
    WHERE (chat_id, message_id) NOT IN (
      SELECT chat_id, message_id FROM messages
      ORDER BY chat_id ASC, message_id ASC LIMIT @n
    );
    ```
  - Tables with composite PK *and* a slice predicate (`training_labels`
    for `KeepSpam` / `KeepHam`) — slice predicate appears on BOTH sides:
    ```sql
    DELETE FROM training_labels
    WHERE label = @sliceLabel                    -- outer slice predicate
      AND (chat_id, message_id) NOT IN (
        SELECT chat_id, message_id FROM training_labels
        WHERE label = @sliceLabel                -- inner slice predicate
        ORDER BY chat_id ASC, message_id ASC LIMIT @n
      );
    ```

  Without the outer slice predicate, `KeepSpam(5)` would delete every
  ham row too (since none are in the inner SELECT).

- **Plans are single-shot.** `Reduce(AppDbContext)` binds the target
  context at construction; the plan records operations against that
  specific context and `ApplyAsync` runs them once. Calling `ApplyAsync`
  a second time on the same plan is undefined — most operations would be
  no-ops (slices already at limit), but no guarantee is made. Build a
  fresh plan per test setup. If a future need arises to apply the same
  shape across contexts, change `Reduce()` to take no context and add a
  `context` parameter to `ApplyAsync` — but that's not v1.

---

## Migration plan

Two prerequisites run before Phase 1's commit (audit + canonical
bootstrap, detailed below). They produce no commits — only inputs into
Phase 1.

| Phase | Commit subject | Outcome |
|-------|----------------|---------|
| 1 | `feat(test): add canonical SQL fixtures + GoldenReducePlan builder + SharedDataProtectionProvider` | Green; new infra exists, no consumer change |
| 2 | `feat(test): add template DB infrastructure to MigrationTestHelper (#463)` | Green; templates built, new methods exist, no consumer change |
| 3A | `refactor(test): migrate canonical-consumer tests to template clone + Reduce` | Green; canonical consumers (count from second-pass audit) faster |
| 3B | `refactor(test): migrate true-empty consumer tests to empty-template clone` | Green; true empty consumers faster |
| 3C | `refactor(test): migration tests adopt GoldenDataset constants` | Green; constants threaded |
| 4 | `chore(test): retire legacy seed methods + SQL files` | Green; final state |

Six commits total. Each green and bisectable.

### Pre-Phase-1 prerequisites (no commits)

Two prerequisite tasks must complete before Phase 1's commit lands.
Neither produces a commit on its own; their outputs feed Phase 1's commit
contents. **Order matters: audit → bootstrap → Phase 1 commit.**

**1. Second-pass audit under strict boundary rule.** The first-pass audit
classified files based on "do you call `GoldenDataset.Seed*Async`?" That
signal is too loose under the strict universal boundary rule (no direct
`Add`/`INSERT` for setup, both paths). Many tests today don't call
`Seed*Async` but DO assemble data ad hoc — the strict rule makes them
canonical-consumers, not empty-consumers.

Dispatch the audit agent on `TelegramGroupsAdmin.IntegrationTests` with
the strict-rule classifier:

- **canonical-consumer:** test needs preexisting data (any data) — even
  if today it assembles via `Add` / raw `INSERT`. Will migrate to
  `CreateDatabaseFromGoldenTemplateAsync` + `Reduce` in Phase 3A.
- **true empty-consumer:** test asserts on empty-state behavior, OR the
  SUT under test is a write method exercised from clean slate. Will
  migrate to `CreateDatabaseFromEmptyTemplateAsync` in Phase 3B.
- **needs canonical extension:** test needs a row shape canonical does
  not currently hold. The audit's union of these requests becomes
  required input to the bootstrap below — bootstrap MUST produce SQL
  fixtures that satisfy every "needs canonical extension" hit, otherwise
  we ship canonical that's missing rows and patch it repeatedly during
  Phase 3A.
- **migration-test:** unchanged path.

The audit must run **before** the canonical bootstrap so the bootstrap
knows the full set of rows/tables/shapes canonical needs to carry.

**2. Canonical bootstrap from local database** (the export-and-sanitize
task described in the "Canonical bootstrap from local database" section
above). Executed collaboratively against the running local DB once the
audit has produced its definitive "needs canonical extension" list.
Output: 17 `canonical/*.sql` files reviewed and ready to commit. Map
existing `GoldenDataset.*_Id` constants to specific canonical rows;
update the constants file if any IDs need to shift.

### Phase 1 — Build the new seed surface (no consumer change)

Pre-Phase-1 prerequisites (audit and bootstrap) are complete; canonical
SQL files exist on the maintainer's working copy ready to commit
alongside the new infrastructure below.

- Commit the 17 `canonical/*.sql` files (output of the pre-Phase-1
  bootstrap, including any rows the audit identified as "needs canonical
  extension"). The `GoldenDataset.*_Id` constants file is updated in the
  same commit if IDs shifted.
- Update `.csproj` `EmbeddedResource` glob from `TestData\SQL\*.sql` to
  `TestData\SQL\**\*.sql` (recursive).
- Create `TestData/SQL/migration/` subfolder. Move
  `40_pre_migration_impersonation_alerts.sql` into it. Update
  `CriticalMigrationTests.cs` (the only consumer) to reference the new path.
- Add `GoldenReducePlanBuilder.cs` and `ChildReducePlan.cs` (two-stage
  type-state builder; calling any child reducer transitions out of the
  parent stage at the type level). `ApplyAsync` on either stage runs
  registered operations in fixed parent-first topological order. See
  Reducer surface for the slice-predicate-on-both-sides DELETE shape.
- Add `GoldenDataset.Reduce(AppDbContext)` factory.
- Add `GoldenDataset.LoadCanonicalAsync(AppDbContext, IDataProtectionProvider, CancellationToken)`.
- Add `PostgresFixture.SharedDataProtectionProvider` (an
  `EphemeralDataProtectionProvider` instance).
- Add NUnit-style framework tests for `GoldenReducePlanBuilder` /
  `ChildReducePlan` in `TestData/Tests/GoldenReducePlanTests.cs`. Each
  test uses `MigrationTestHelper + LoadCanonicalAsync` to set up
  canonical against a freshly-migrated DB, then exercises `KeepSpam`,
  `KeepHam`, `KeepMessages` (cascade behavior, including verifying
  `user_actions` SetNull behavior), `KeepDetectionResults`,
  `KeepUserActions`, slice-predicate isolation (KeepSpam doesn't touch
  ham, etc.), cascade-narrowing (KeepMessages alone vs KeepMessages
  followed by a child reducer), topological execution (registration
  order via intermediate variables still produces parent-first
  behavior), `count==0`, `count > actual_canonical_rows` LIMIT semantics,
  `count > post_cascade_rows` LIMIT semantics, negative-count validation,
  last-wins semantics, and single-shot apply (no plan-reuse contract).
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

### Phase 3A — Migrate canonical-consumer tests

Exact file list comes from the Pre-Phase-1 second-pass audit. The first-pass
audit's count (14 explicit Golden-callers + the 5 reclassified files +
`UserRepositoryTests` mixed class) is a lower bound; the strict-rule
audit will move many additional files from "empty-consumer" to
"canonical-consumer."

Each canonical-consumer class is migrated to:

1. Replace `await _testHelper.CreateDatabaseAndApplyMigrationsAsync()` with
   `await _testHelper.CreateDatabaseFromGoldenTemplateAsync()`.
2. Drop the `await GoldenDataset.SeedXyzAsync(...)` call entirely (canonical
   is already in the cloned DB).
3. For ML threshold tests, replace `TRUNCATE + INSERT` setup with
   `await GoldenDataset.Reduce(ctx).KeepSpam(N).KeepHam(M).ApplyAsync()`.
4. If the test today builds its own `IDataProtectionProvider`, swap to
   `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`.
   This is required for canonical-consumers because the cloned `golden_template`
   carries `api_keys` ciphertext encrypted under the shared provider's
   keyring; only the shared provider can decrypt it.

**Confirmed canonical-consumer files** (from first-pass audit + spec review;
the second-pass audit will add more):

- `Configuration/ConfigServiceIntegrationTests.cs`
- `Deduplication/SimHashComparisonTests.cs`
- `ML/BayesClassifierServiceTests.cs`
- `ML/MLTextClassifierServiceTests.cs`
- `Repositories/AnalyticsRepositoryTests.cs`
- `Services/Backup/BackupServiceTests.cs` *(reclassified from audit)*
- `Repositories/DetectionResultsRepositoryTests.cs`
- `Repositories/DbContextFactoryMigrationTests.cs` *(reclassified)*
- `Repositories/MessageHistoryRepositoryTests.cs` *(reclassified)*
- `Repositories/NotificationRepositoriesTests.cs` *(reclassified)*
- `Repositories/TelegramUserRepositoryTests.cs`
- `Repositories/TelegramUserUpsertTests.cs`
- `Repositories/TrainingLabelsRepositoryTests.cs`
- `Telegram/Repositories/LinkedChannelsRepositoryTests.cs`

**1 mixed class — `Repositories/UserRepositoryTests.cs`:** test method
`AnyUsersExistAsync_EmptyDatabase_ReturnsFalse` (line 33) is a true-empty
test (asserts on empty state); `AnyUsersExistAsync_WithExistingUser_ReturnsTrue`
(line 56) needs canonical. Migration: each test method picks its own
`Create*` call. The per-test `MigrationTestHelper` instance is already
created inside each `[Test]` method, so the per-method choice is mechanical.

**Four files flagged by first-pass as ambiguous** — second-pass audit will
classify definitively:

- `ContentDetection/Repositories/ProfileScanAlertMappingTests.cs`
- `ContentDetection/Repositories/ReportsRepositoryTests.cs`
- `Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`
- `Telegram/Services/BanCelebrationServiceTests.cs`

**Hidden canonical-consumers from the 30-file empty list:** the second-pass
audit will classify each file under the strict rule. Examples likely to
reclassify (do `Add(new XxxDto)` for setup today, no SUT-write being
tested): `BotChatServiceTests`, `BotDmServiceTests`, `BotMessageServiceTests`,
`WelcomeTimeoutJobTests`, `ExamFlowServiceTests`, `InviteRepositoryTests`,
`UsernameHistoryRepositoryTests`, etc. Each becomes
`CreateDatabaseFromGoldenTemplateAsync` + canonical references for IDs
(possibly with `Reduce`).

### Phase 3B — Migrate true-empty consumer tests

True-empty consumers are a small set: tests that genuinely need empty-state
verification, OR tests that exercise a write SUT from clean slate. The
second-pass audit produces this list. Examples:

- `UserRepositoryTests.AnyUsersExistAsync_EmptyDatabase_ReturnsFalse` (the
  test method, not the whole class) — asserts that the empty-DB result is
  `false`. Pure empty-state verification.
- Constraint tests under `Repositories/` whose entire purpose is "this
  constraint blocks invalid inserts" — they need empty + a single bad
  insert via raw SQL or SUT to verify the constraint fires.

For each true-empty consumer: replace
`await _testHelper.CreateDatabaseAndApplyMigrationsAsync()` with
`await _testHelper.CreateDatabaseFromEmptyTemplateAsync()`.

**No data-assembly migration in this phase.** True-empty tests by
definition either don't have setup data or exercise a write SUT to produce
it. Per the universal boundary rule, no `Add`/`INSERT` for setup.

**DI registration swap (per-file judgment):**

Tests that today use `services.AddDataProtection().PersistKeysToFileSystem(... fresh GUID ...)`
fall into two categories:

- Tests that interact with `configs.api_keys` or other DataProtection-encrypted
  data and need to share a keyring with future canonical-seeded data → swap
  to `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`.
- Tests that intentionally validate keyring isolation (encrypt under one
  key, fail to decrypt under another) → keep their per-test ephemeral
  provider unchanged.

The audit found no tests in the second category today; default to the
shared provider unless a "this test asserts keyring isolation" rationale
exists.

**The exact file list comes from the second-pass audit.** First-pass guesses
suggest the count is small (likely under 10), but only the strict-rule
classifier produces a definitive list.

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
- Delete the 15 obsolete SQL files (listed in "Files deleted" above; this
  count is intentionally less than the 17 new `canonical/*.sql` files,
  because today's configs row came from inline `SeedConfigsAsync` rather
  than a SQL fixture, and `message_translations` had no fixture under the
  old layout).
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

`GoldenReducePlanBuilder` and `ChildReducePlan` are exercised by NUnit
tests in `TestData/Tests/GoldenReducePlanTests.cs` (new file). Each test
uses `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync()` to
build a fresh DB at HEAD schema, calls `LoadCanonicalAsync` to populate
canonical, then exercises one or more `Keep*` operations (entering the
parent stage via `Reduce(ctx)` and transitioning into `ChildReducePlan`
as soon as any child reducer is called) and asserts final row counts
and id ordering. Coverage:

- `KeepSpam(N)` keeps the first N spam `training_labels` rows under
  `ORDER BY chat_id ASC, message_id ASC`. Verifies `KeepSpam` does NOT
  delete ham rows (regression test for the slice-predicate bug — the
  outer DELETE must filter to `WHERE label = Spam`).
- `KeepHam(N)` keeps the first N ham labels under the same composite
  ordering, and does NOT delete spam.
- `KeepSpam(0)` removes all spam, leaves all ham (and `KeepHam(0)` symmetrically).
- `KeepDetectionResults(N)` keeps lowest N by surrogate `id` ASC.
- `KeepUserActions(N)` keeps lowest N by surrogate `id` ASC.
- `KeepMessages(0)` cascades through `message_edits`, `training_labels`,
  `detection_results`, `message_translations` (all Cascade FKs) and
  SetNulls `user_actions.MessageId`/`ChatId`. Test asserts that
  `message_translations` rows are actually present in canonical before
  the cascade (otherwise this assertion is vacuous) and that none survive
  after. Verifies the documented FK behavior.
- **Cascade-narrowing model:**
  - `KeepMessages(5)` alone leaves 5 messages plus their natural-cascade
    set of children — assert child counts equal what bootstrap produced
    for those 5 messages.
  - `KeepMessages(5).KeepDetectionResults(2)` further restricts: assert
    exactly 2 detection_results survive, both tied to surviving messages.
  - `KeepMessages(0).KeepUserActions(0)` removes all messages AND all
    user_actions (cascade only SetNulls user_actions; the explicit
    `KeepUserActions(0)` is required to delete them).
- **Topological execution order:** registering `KeepDetectionResults(2)`
  before `KeepMessages(5)` produces the same final state as registering
  them in reverse — `ApplyAsync` reorders to parent-first internally.
  (The compile-time type-state usually prevents the wrong-order chain,
  but plans built with intermediate variables can still register in any
  order; topological execution is the runtime guarantee.)
- **Compile-time order enforcement (verified by API review, not a
  runtime test):** during Phase 1 PR review, the reviewer confirms
  `ChildReducePlan` exposes only the four child reducers + `ApplyAsync`
  and has no `KeepMessages` member. That structural property is what
  makes `Reduce(ctx).KeepHam(5).KeepMessages(5)` a compile error. We do
  NOT add a Roslyn-compilation runtime test that asserts the snippet
  fails to compile — the API surface itself is the proof, and adding
  Roslyn machinery just to verify a structural property is over-engineered.
- Negative count throws `ArgumentOutOfRangeException`.
- `count > actual_canonical_rows` is a no-op for the excess (canonical-bound).
- `count > post_cascade_rows` is a no-op for the excess (cascade-bound).
- Last-wins for repeated `Keep*` on same slice within the same stage.
- A single `ApplyAsync` exercising multiple reducers runs in one transaction
  (assert via querying intermediate state from a separate connection — should
  see canonical, not partial-reduced state, until commit).

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
- The test was using an additive pattern that's forbidden under the
  universal boundary rule (`Add(new XxxDto)` / raw `INSERT` for setup) →
  resolve in one of two ways depending on what the test is actually
  doing: (a) if the test exercises a write SUT, route the additive
  pattern through that SUT (the write IS the test); (b) if the test
  reads from preexisting data and was using `Add` to fabricate that
  data, migrate it to the canonical path with `Reduce` and reference
  rows via `GoldenDataset.*_Id` constants. The empty path is NOT a
  license to keep the additive pattern — the universal boundary rule
  applies to both paths.

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

### Pre-Phase-1 prerequisites
- [ ] Second-pass audit complete; per-file classification produced
      (canonical-consumer / true-empty / needs-canonical-extension /
      migration-test); union of "needs canonical extension" rows captured
      as bootstrap input
- [ ] Canonical bootstrap complete: 17 `canonical/*.sql` files produced
      from local DB sampling (400 messages — 100 of each ham/spam type,
      200 training labels, plus FK-supporting rows), sanitization rules
      applied per the rules table, manual spot-check passed (URL
      hostnames neutralized in spam, no non-spammer leakage, no
      encrypted ciphertext, no JSONB credentials)
- [ ] `GoldenDataset.*_Id` constants mapped to specific canonical rows
      (existing constants stable, new constants added only as new
      fixtures introduce identifiers tests will reference); constants
      file ready to commit alongside SQL fixtures
- [ ] No bootstrap script committed to the repo (canonical SQL files
      are the only artifact)

### Phase 1
- [ ] `.csproj` `EmbeddedResource` glob updated to `TestData\SQL\**\*.sql`
- [ ] `TestData/SQL/canonical/` exists with 17 per-table SQL files
      committed (output of the pre-Phase-1 bootstrap)
- [ ] `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql`
      moved; `CriticalMigrationTests.cs` references the new path
- [ ] `GoldenReducePlanBuilder` (parent stage) and `ChildReducePlan`
      (child stage) exist with the 5 `Keep*` methods split per the type
      surface; `ApplyAsync` on both stages
- [ ] PR reviewer verifies `ChildReducePlan` has no `KeepMessages` member —
      this is the structural property that makes `KeepHam(N).KeepMessages(N)`
      a compile error in fluent chains (no runtime compile-fail test required)
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
- [ ] All canonical-consumer files migrated to `CreateDatabaseFromGoldenTemplateAsync`,
      ad-hoc `Add`/`INSERT` setup replaced with canonical references and
      optional `Reduce`; suite green
- [ ] `UserRepositoryTests` mixed class migrated per-test-method; suite green
- [ ] All true-empty consumer files migrated to `CreateDatabaseFromEmptyTemplateAsync`;
      no `Add`/`INSERT` setup remains; suite green
- [ ] DI provider swap reviewed per-file with documented intent (default
      to `SharedDataProtectionProvider`; preserve ephemeral only with
      keyring-isolation rationale); suite green
- [ ] Migration tests adopt `GoldenDataset.*` constants where applicable;
      suite green

### Phase 4
- [ ] `find_symbol_usages` shows zero callers of every retired method
- [ ] 11 retired methods deleted from `GoldenDataset`
- [ ] 15 obsolete SQL files deleted (the deletion list above; distinct from
      the 17 new `canonical/*.sql` files added in Phase 1 — asymmetry is
      intentional: today's `configs` row came from inline `SeedConfigsAsync`
      C# code, not a SQL file; `message_translations` had no fixture under
      the old layout)
- [ ] `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` retained
      (still used by migration tests)
- [ ] All tests pass
- [ ] `T1` final timing captured; documented in PR description with `T0`
      for comparison

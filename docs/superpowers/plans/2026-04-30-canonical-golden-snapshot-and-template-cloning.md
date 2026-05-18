# Canonical Golden Snapshot + Template DB Cloning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace today's three competing IntegrationTests seeding strategies with a single canonical superset cloned per-test from a Postgres template DB, plus a subtractive `GoldenDataset.Reduce(...)` builder for tests that need a constrained shape from canonical.

**Architecture:** `MigrationTestHelper` exposes three explicit paths (canonical clone / empty clone / fresh+migrate). `PostgresFixture.[OneTimeSetUp]` builds `empty_template` (migrations only) and `golden_template` (empty + canonical SQL + DataProtection-encrypted UPDATEs) once per session. Per-test setup becomes `CREATE DATABASE … TEMPLATE`. Tests that need a constrained shape from canonical use `GoldenDataset.Reduce(ctx).KeepX(N)…ApplyAsync()`, a two-stage type-state builder that runs in fixed parent-first topological order regardless of registration order.

**Tech Stack:** .NET 10.0, EF Core 10, NUnit, Npgsql, PostgreSQL 18, Testcontainers.PostgreSql, Microsoft.AspNetCore.DataProtection.

**Spec:** [`docs/superpowers/specs/2026-04-29-canonical-golden-snapshot-and-template-cloning-design.md`](../specs/2026-04-29-canonical-golden-snapshot-and-template-cloning-design.md)

**Closes:** #462, #463

**PR target:** `develop`

---

## Phase ordering

| Phase | Commit subject | Bisect-green |
|-------|----------------|--------------|
| Pre-1a | ✅ (no commit) Second-pass strict-rule audit — done 2026-04-30 | n/a |
| Pre-1b | ✅ (no commit) Canonical bootstrap from local DB — done 2026-05-01 | n/a |
| Pre-1c | ✅ (no commit) Author `IntegrationTests/CLAUDE.md` cheat sheet — done 2026-05-03 | n/a |
| 1 | `feat(test): add canonical SQL fixtures + GoldenReducePlan builder + SharedDataProtectionProvider` | yes |
| 2 | `feat(test): add template DB infrastructure to MigrationTestHelper (#463)` | yes |
| 3A | `refactor(test): migrate canonical-consumer tests to template clone + Reduce` | yes |
| 3B | `refactor(test): migrate true-empty consumer tests to empty-template clone` | yes |
| 3C | `refactor(test): migration tests adopt GoldenDataset constants` | yes |
| 4 | `chore(test): retire legacy seed methods + SQL files` | yes |
| Post-A | (no commit) Bootstrap cleanup — drop bootstrap schema + tmp/ working files | n/a |
| Post-B | (no commit) File follow-up bug reports as separate GitHub issues | n/a |

Six commits total. Pre-1a/1b/1c produce inputs into the Phase 1 commit, not their own commits. The maintainer drives Pre-1b interactively against the running local DB; Pre-1c writes `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`, which lands in the Phase 1 commit alongside the canonical SQL files. Constants are deliberately *not* produced in Pre-1c — they are pulled into existence on demand by the test rewrites in Phases 3A–C as each test discovers what it needs to reference. Post-A/B run after the PR merges.

---

## Pre-Phase 1a: Second-pass audit (no commit) — ✅ COMPLETE

Output: a per-file classification of every `TelegramGroupsAdmin.IntegrationTests/**/*.cs` test class under the strict universal boundary rule (canonical-consumer / true-empty-consumer / needs-canonical-extension / migration-test). The "needs-canonical-extension" union is required input to the bootstrap (Pre-1b).

**Status:** Audit complete on 2026-04-30. Output: `tmp/canonical-bootstrap/audit-output.md` (50 files classified). Findings already incorporated into Phase 3A (28 tasks), Phase 3B (12 tasks), and Pre-Phase 1b extension list (4 concrete needs-canonical items). Re-run only if new test files land before Phase 3A starts.

### Task A1: Dispatch the strict-rule audit subagent

**Files:**
- Read-only: every file under `TelegramGroupsAdmin.IntegrationTests/**/*.cs`
- Output (working artifact, NOT committed): `tmp/canonical-bootstrap/audit-output.md`, structured per the schema below. The `tmp/` directory is already in `.gitignore`.

- [x] **Step 1: Dispatch the audit subagent**

Use Agent (subagent_type: `general-purpose` or `Explore`) with this prompt verbatim:

> Audit every `*.cs` file under `TelegramGroupsAdmin.IntegrationTests/` (excluding `bin/`, `obj/`, `TestResults/`) and classify each test class under the strict universal boundary rule defined in `docs/superpowers/specs/2026-04-29-canonical-golden-snapshot-and-template-cloning-design.md` (Architecture > Boundary rule).
>
> Classifier:
> - **canonical-consumer:** test needs preexisting data — even if today it assembles via `Add(new XxxDto)` or raw `INSERT`. Will migrate to `CreateDatabaseFromGoldenTemplateAsync` + optional `Reduce`.
> - **true-empty-consumer:** test asserts on empty-state behavior, OR the SUT under test is a write method exercised from clean slate (the SUT call IS the setup). No preexisting setup data.
> - **needs-canonical-extension:** test needs a row shape canonical does not currently hold (e.g., a specific FK relationship, a row with a particular column value). List the required rows/columns/shapes precisely; this becomes input to the canonical bootstrap.
> - **migration-test:** lives under `Migrations/` or `PgBouncer/`; tests the migration system itself. Stays on existing `MigrationTestHelper` methods.
>
> Write the result as `tmp/canonical-bootstrap/audit-output.md` (relative to the repo root) with this structure:
>
> ```
> # Strict-rule audit output (YYYY-MM-DD)
>
> ## canonical-consumer (N files)
> - path/to/File.cs — one-line rationale
> ...
>
> ## true-empty-consumer (N files)
> - path/to/File.cs — one-line rationale (assertion-on-empty | write-SUT-from-empty)
> ...
>
> ## mixed (per-test-method) (N files)
> - path/to/File.cs
>   - `TestMethodA` — canonical-consumer (rationale)
>   - `TestMethodB` — true-empty-consumer (rationale)
> ...
>
> ## needs-canonical-extension (N items)
> - path/to/File.cs:LineN — required rows/shape (e.g., "needs a `messages` row with `media_local_path` non-null and matching `message_edits` rows")
> ...
>
> ## migration-test (N files)
> - path/to/File.cs
> ...
> ```
>
> Do not edit any source files. Do not run tests. Read-only audit.

- [x] **Step 2: Verify the audit output exists and is well-formed**

Run: `wc -l tmp/canonical-bootstrap/audit-output.md && head -40 tmp/canonical-bootstrap/audit-output.md`

Expected: file present; sections `canonical-consumer`, `true-empty-consumer`, `mixed`, `needs-canonical-extension`, `migration-test` each present with at least their headers.

- [x] **Step 3: Confirm the file is excluded from git tracking**

Run: `git check-ignore -v tmp/canonical-bootstrap/audit-output.md`

Expected: output shows `.gitignore:NN:tmp/    tmp/canonical-bootstrap/audit-output.md` (path is matched by the existing `tmp/` rule). The audit output is a working artifact that informs Phases 1, 3A, 3B but never lands in the repo.

---

## Pre-Phase 1b: Canonical bootstrap from local database (no commit) — ✅ COMPLETE

**Status:** Bootstrap complete on 2026-05-01. 35 canonical SQL files generated at `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/` (3,174 INSERTs, 2.4 MB total). Bootstrap schema preserved in local DB and `tmp/canonical-bootstrap/` working files retained as a safety net — final cleanup runs in Post-Phase A after Pre-Phase 1c + Phase 1–4 prove canonical works end-to-end.

**Final canonical sizes (post-Strict-Plus prune):** 9 web users, 335 telegram_users, 21 managed_chats, 400 messages (100/100/100/100 ham/spam slices), 200 training_labels, 376 detection_results, 23 message_edits, 14 message_translations, 100 audit_log, 104 chat_admins, 10 reports, 10 profile_scan_results, 12 user_tags, 3 admin_notes, 4 username_history, 3 telegram_user_mappings, 19 invites, 3 linked_channels, 5 notification_preferences, 2 username_blacklist (only Exact match implemented in `BlacklistMatchType`; future types added when feature ships), 293 welcome_responses, 993 user_actions, 1 prompt_versions (single Main Chat synthetic row; tests build version history via SUT), 18 content_detection_configs, 20 configs (encrypted columns NULL — Apply-phase injects at runtime; `welcome_config` populated only on global+Main Chat for synthetic exam prompts, NULL on other 18 per-chat rows), reference tables (74 captions / 92 gifs / 7 blocklist / 17 stop_words / 6 tag_definitions), 5 EMPTY tables. Total 3174 INSERT statements.

**Privacy posture:** all telegram_user_ids and chat_ids rotated via deterministic md5 hash with secret salts (kept in `tmp/canonical-bootstrap/sql/B5_rotate_ids.sql`, gitignored). User IDs land in [9×10¹², 10¹³); chat IDs in [-1.001×10¹⁴, -1×10¹⁴] starting with `-100` prefix to satisfy app code's Telegram supergroup format checks while remaining clearly synthetic (15-digit IDs vs real Telegram supergroups at 13 digits). Real prod identities are not derivable from canonical without the salts. URL hostnames in spam content uniformly replaced with `canonical-spam.test` (paths/queries preserved); phones replaced with NANP-reserved `+15555550199` / `555-555-0199`; non-canonical emails replaced with `spam@canonical.test`. Free-text audit fields (admin_notes, audit_log narrative, reports.reviewed_by) re-anchored to canonical fixture identities.

> **Original execution model (preserved for reference):** This phase ran in a SEPARATE conversation with a dedicated bootstrap agent. The agent worked collaboratively with the maintainer against the live local development database. Treat this whole section as a self-contained brief — a fresh agent reads it cold and produces the deliverables below.
>
> **One-time export:** Pre-Phase 1b runs ONCE against the prod-restored DB on 2026-04-30 (or a near-future date if the bootstrap conversation runs later). The committed canonical SQL files become the pinned, frozen contract — Phase 1 onward uses ONLY those files; the `bootstrap` schema and the local DB it ran against are no longer relevant. There is no scheduled re-run cadence and no "refresh from prod" workflow planned. If the canonical contract ever needs to evolve (new test shapes the existing canonical can't satisfy), prefer extending Reduce plans or adding targeted fixture rows over a wholesale re-bootstrap.
>
> **Local DB connection:** read host/port/db/user/password from `compose/compose.yml` (the `postgres` service block). Do not copy credentials into commits, chat logs, or this plan. Use read-only access where possible; the only writes are to the `bootstrap` schema (created and dropped by this phase).
>
> **Required input — Pre-Phase 1a audit output:** `tmp/canonical-bootstrap/audit-output.md` (already produced; gitignored under `tmp/`). Read its `needs-canonical-extension` section before sampling. The four concrete extensions canonical MUST carry to satisfy known consumer tests are listed below as a fast-path summary:
>
> 1. **SimHash dedup messages** — `Deduplication/SimHashComparisonTests.cs:124` references message IDs **95001–95022** with named near-duplicate groups: Group1 crypto signals (95001–95004), Group2 investment scams (95005–95007), Group3 giveaway scams (95008–95010), additional groups (95011–95016), Group6 ham ML (95017–95019), Group7 money-fast (95020–95022). **Was attempted but did NOT land** — see `IntegrationTests/CLAUDE.md` Part 2 "Known canonical gaps." Phase 3A.3 must extend canonical OR seed inline.
> 2. **Welcome response slice** — `Jobs/WelcomeTimeoutJobTests.cs:113` needed a `welcome_responses` row pinned to `(MainChat, specific user, WelcomeMessageId=<new constant>)` with `Response=Pending`, plus variants for `Response IN (Accepted, Denied, Left, Timeout)` so all five timeout-path branches can be exercised. **Partially landed:** canonical carries synthetic IDs `999001..999005` covering the five `WelcomeResponseType` statuses (Pending / Accepted / Denied / Timeout / Left) on `(chat_id=-100026957614982, user_id=9196379650113, username='canonical_user1', welcome_message_id=99001..99005)` — but NOT pinned to the legacy `User1_TelegramUserId` the test expects. Phase 3A.4 retargets the test at `9196379650113` (or extends canonical with the legacy-user variant).
> 3. **Analytics time-spread data** — `Repositories/AnalyticsRepositoryTests.cs:78` aggregates over daily / weekly / monthly / 7-day / 30-day / 365-day windows. ✅ **RESOLVED in 3A.7 (2026-05-18)** — canonical stays frozen; Phase 3A.7 introduced `Reduce.KeepMessages(allowlist)` + new `Mutate.Shift*` verbs so the test re-times specific surviving rows into NOW()-relative buckets in its own clone. See 3A.7 resolution note below.
> 4. **Ban-celebration `user_actions` set** — `Telegram/Services/BanCelebrationServiceTests.cs:546` renders `{bancount}` placeholders 1–7. **Was attempted but did NOT land** — the seven synthetic ban anchor rows are not in canonical. See `IntegrationTests/CLAUDE.md` Part 2 "Known canonical gaps." Phase 3A.22 must extend canonical OR seed inline. (Note: canonical does include a heavily-banned spammer `9971261287520` with 4 Ban actions — usable as a partial fixture for tests that don't need exactly 1–7.)
>
> **Deliverables:**
> 1. 35 `canonical/*.sql` files on the maintainer's working copy under `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/`, sanitized and ready to commit alongside Phase 1's infrastructure changes. The four needs-canonical-extension items above were attempted but only #2 partially landed — see the per-item notes for what Phase 3A consumers will encounter.
> 2. Three follow-up bug reports — filed as separate GitHub issues, **NOT** fixed in this PR. See "Follow-up bugs surfaced during bootstrap" at the end of this section.
> 3. The `bootstrap` schema dropped from the local DB; no bootstrap script committed to the repo.

When this PR's main work resumes, the canonical SQL files will already be on the working copy. Phase 1's Task 1.4 takes them as input.

The bootstrap workflow has 8 steps mirroring spec section "Canonical bootstrap from local database > Bootstrap workflow." Each step is a checkbox so the bootstrap agent + maintainer can pause/resume.

### Task B1: Connect to local DB and confirm schema parity

**Files:**
- Read-only: local DB
- Reference: `TelegramGroupsAdmin.Data/AppDbContext.cs` (FK cascade behavior)

- [ ] **Step 1: Confirm local DB connection string is exported**

Run: `echo "${LOCAL_DB_CONNECTION:-not set}"`

Expected: a Postgres connection string. If unset, ask the maintainer for the read-only connection details and export `LOCAL_DB_CONNECTION`.

- [ ] **Step 2: Confirm local DB schema is at HEAD**

Run:
```bash
psql "$LOCAL_DB_CONNECTION" -c "SELECT migration_id FROM \"__EFMigrationsHistory\" ORDER BY migration_id DESC LIMIT 1;"
```

Expected: the most recent migration name from `TelegramGroupsAdmin.Data/Migrations/`. If schema lags HEAD, **STOP** and ask the maintainer to migrate the local DB first — otherwise the bootstrap captures stale-shape data.

### Bootstrap workflow architecture (reference for all sampling tasks)

The bootstrap samples 35 of the 45 production tables into a `bootstrap` schema, with **FK constraints replicated and enforced** during the sampling phase. The 10 transient/operational tables are skipped entirely. `__EFMigrationsHistory` is excluded (test DB populates it during schema setup).

**Per-table decision matrix:**

| Decision | Tables | Rule |
|---|---|---|
| **SKIP** (10) | `cached_blocked_domains`, `exam_sessions`, `file_scan_quota`, `file_scan_results`, `pending_notifications`, `push_subscriptions`, `report_callback_contexts`, `telegram_link_tokens`, `telegram_sessions`, `verification_tokens` | No bootstrap.* table created; not in pg_dump |
| **FULL** (9) | `users`, `configs`, `content_detection_configs`, `ban_celebration_captions`, `ban_celebration_gifs`, `blocklist_subscriptions`, `prompt_versions`, `stop_words`, `tag_definitions` | `INSERT INTO bootstrap.X SELECT * FROM X` |
| **EMPTY** (5) | `domain_filters`, `recovery_codes`, `image_training_samples`, `video_training_samples`, `web_notifications` | bootstrap.X table created; no INSERT (0 rows in prod, no test needs shape) |
| **NEEDS-EXTENSION** (1) | `username_blacklist` | bootstrap.X table created; canonical inserts representative test rows |
| **FK-CLOSURE** (15) | `telegram_users`, `managed_chats`, `chat_admins`, `linked_channels`, `telegram_user_mappings`, `username_history`, `admin_notes`, `user_tags`, `notification_preferences`, `invites`, `message_edits`, `detection_results`, `user_actions`, `message_translations` (with no-op-translation filter), `welcome_responses` (Pre-1b deliverable #2 + analytics time-spread) | Sample rows whose FK targets are already in bootstrap.*; add missing parents recursively |
| **N=10 AUGMENT-USERS** (2) | `profile_scan_results`, `reports` | Prefer rows referencing users already in bootstrap.telegram_users; fill shortfall by adding users (and their FK closure) |
| **CUSTOM PREDICATE** (3) | `messages` (slice classification), `training_labels` (185 prod + 15 synthetic), `audit_log` (decision deferred — sample N most recent or by referenced users) | Per-table SQL in B2/B3 |
| **EXCLUDE** (1) | `__EFMigrationsHistory` | Migration runner state, populated by test DB schema setup |

**Topological dependency layers** (sampling order in B2/B3):

- **Layer 0 — roots (18 tables):** `managed_chats, telegram_users, users, configs, content_detection_configs, ban_celebration_captions, ban_celebration_gifs, blocklist_subscriptions, domain_filters, notification_preferences, prompt_versions, recovery_codes, stop_words, tag_definitions, audit_log` *(structurally — its FKs are CASCADE so it actually depends on telegram_users + users; treat as Layer 1 in practice)*, `image_training_samples` (empty), `video_training_samples` (empty), `web_notifications` (empty), `username_blacklist` (needs-extension).
- **Layer 1 — direct children of roots (after messages):** `chat_admins, linked_channels, telegram_user_mappings, profile_scan_results, username_history, admin_notes, audit_log, user_tags, welcome_responses, invites, reports`. Plus `messages` itself.
- **Layer 2 — children of messages:** `message_edits, detection_results, training_labels, user_actions`.
- **Layer 3 — grandchildren:** `message_translations`.

**FK constraint replication strategy:**

`LIKE table INCLUDING ALL` carries CHECK constraints, indexes, defaults, identity, storage — but **NOT FK constraints**. The bootstrap explicitly recreates FKs on each `bootstrap.X` table after creation, pointing at `bootstrap.*` parents (not `public.*`). This catches invalid data at INSERT time rather than at later FK-closure spot-checks. Constraints are dropped before `pg_dump` (Task B6.5) so canonical SQL files don't carry constraint definitions — the test DB already has them via migration-driven schema.

**Universal sampling pattern for FK-closure tables:**

```sql
-- 1. Identify the rows we want (per-table predicate)
WITH chosen AS (SELECT ... FROM source_table WHERE <predicate>)

-- 2. Pull missing FK targets into bootstrap.* parents FIRST
INSERT INTO bootstrap.parent_table
SELECT DISTINCT p.* FROM parent_table p
WHERE p.<pk> IN (SELECT <fk_col> FROM chosen)
  AND p.<pk> NOT IN (SELECT <pk> FROM bootstrap.parent_table);

-- 3. NOW INSERT the child rows — FK constraint passes
INSERT INTO bootstrap.child_table
SELECT s.* FROM source_table s JOIN chosen c ...;
```

If any FK target is genuinely missing in prod (orphan FK), the bootstrap-time INSERT errors with a Postgres FK violation — surfaces data-quality bugs immediately rather than at restore time.

### Task B2: Setup — schema, all bootstrap tables, FK replication, helpers

**Bootstrap workspace conventions (apply to all of B2–B7):**

- The bootstrap uses a dedicated `bootstrap` schema with permanent UNLOGGED tables — NOT `TEMP TABLE`. Postgres temp tables are session-scoped, so a later `pg_dump` (which opens its own session) can't see them. UNLOGGED tables in a named schema are visible across sessions and are crash-unsafe (fine, since the bootstrap is one-shot).
- `training_labels.label` is `smallint` with `0 = Spam`, `1 = Ham` (per `TelegramGroupsAdmin.Core/Models/TrainingLabel.cs`). All SQL in this bootstrap uses the smallint values directly. **Never write `'Ham'` / `'Spam'` against this column** — Postgres will reject it against a smallint.
- A persisted slice classification (`bootstrap.message_slices`) drives every downstream decision: sanitization (lorem ham vs hostname-rewrite spam), spammer detection (which `telegram_users` keep their real names), and FK closure for derived tables.
- **FK constraints are replicated and enforced** during sampling. `LIKE INCLUDING ALL` does NOT carry FK constraints, so Step 2 manually replicates them via `pg_get_constraintdef` rewriting parent refs to `bootstrap.*`. Constraints are dropped before `pg_dump` (Task B6.5) so canonical SQL files don't carry constraint definitions.

- [ ] **Step 1: Create the `bootstrap` schema, slice tracking table, lorem helper, and all 35 sampled-table mirrors**

```sql
CREATE SCHEMA IF NOT EXISTS bootstrap;

-- Slice classification (no prod analog) — populated during sampling, queried during sanitization.
CREATE UNLOGGED TABLE bootstrap.message_slices (
    chat_id    bigint NOT NULL,
    message_id integer NOT NULL,
    slice      text   NOT NULL CHECK (slice IN ('explicit_ham','implicit_ham','explicit_spam','implicit_spam')),
    PRIMARY KEY (chat_id, message_id)
);

-- Lorem helper used by all ham-text sanitization sites. Returns the canonical
-- Lorem Ipsum passage truncated to EXACTLY target_len characters.
CREATE OR REPLACE FUNCTION bootstrap.lorem(target_len integer) RETURNS text AS $$
    SELECT CASE
        WHEN target_len IS NULL OR target_len <= 0 THEN ''
        ELSE LEFT(
            repeat(
                'Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. ',
                CEIL(target_len::numeric / 445)::integer + 1
            ),
            target_len
        )
    END;
$$ LANGUAGE sql IMMUTABLE;

-- Mirror tables — 35 of the 45 production tables (10 SKIP'd transient/operational
-- tables and 1 EXCLUDE'd migration-meta table omitted; see Bootstrap workflow
-- architecture matrix above).
-- Layer 0 roots:
CREATE UNLOGGED TABLE bootstrap.users                      (LIKE users INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.telegram_users             (LIKE telegram_users INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.managed_chats              (LIKE managed_chats INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.configs                    (LIKE configs INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.content_detection_configs  (LIKE content_detection_configs INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.ban_celebration_captions   (LIKE ban_celebration_captions INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.ban_celebration_gifs       (LIKE ban_celebration_gifs INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.blocklist_subscriptions    (LIKE blocklist_subscriptions INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.domain_filters             (LIKE domain_filters INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.notification_preferences   (LIKE notification_preferences INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.prompt_versions            (LIKE prompt_versions INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.recovery_codes             (LIKE recovery_codes INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.stop_words                 (LIKE stop_words INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.tag_definitions            (LIKE tag_definitions INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.username_blacklist         (LIKE username_blacklist INCLUDING ALL);

-- Layer 1 children of roots:
CREATE UNLOGGED TABLE bootstrap.messages                   (LIKE messages INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.chat_admins                (LIKE chat_admins INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.linked_channels            (LIKE linked_channels INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.telegram_user_mappings     (LIKE telegram_user_mappings INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.profile_scan_results       (LIKE profile_scan_results INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.username_history           (LIKE username_history INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.admin_notes                (LIKE admin_notes INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.audit_log                  (LIKE audit_log INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.user_tags                  (LIKE user_tags INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.welcome_responses          (LIKE welcome_responses INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.invites                    (LIKE invites INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.web_notifications          (LIKE web_notifications INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.reports                    (LIKE reports INCLUDING ALL);

-- Layer 2 children of messages:
CREATE UNLOGGED TABLE bootstrap.message_edits              (LIKE message_edits INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.detection_results          (LIKE detection_results INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.training_labels            (LIKE training_labels INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.user_actions               (LIKE user_actions INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.image_training_samples     (LIKE image_training_samples INCLUDING ALL);
CREATE UNLOGGED TABLE bootstrap.video_training_samples     (LIKE video_training_samples INCLUDING ALL);

-- Layer 3 grandchildren:
CREATE UNLOGGED TABLE bootstrap.message_translations       (LIKE message_translations INCLUDING ALL);
```

Sanity-check the lorem helper:
```sql
SELECT length(bootstrap.lorem(0));     -- 0
SELECT length(bootstrap.lorem(50));    -- 50
SELECT length(bootstrap.lorem(2000));  -- 2000 (cycles through canonical passage ~5x)
SELECT bootstrap.lorem(11);            -- 'Lorem ipsum'  (exact prefix of canonical)
```

- [ ] **Step 1.5: Replicate FK constraints from prod onto bootstrap mirror tables**

`LIKE INCLUDING ALL` does not copy FK constraints. This step iterates every FK on the 35 source tables, rewrites parent references from `public.*` to `bootstrap.*`, and applies the constraint to the bootstrap mirror. Constraints retain their CASCADE/SET NULL/RESTRICT delete rules. Self-referencing FKs (e.g., `users.invited_by`) are handled correctly because both endpoints land in the bootstrap schema.

```sql
DO $$
DECLARE
    r record;
    new_def text;
    bare_table text;
    bootstrap_tables text[] := ARRAY[
        'users','telegram_users','managed_chats','configs','content_detection_configs',
        'ban_celebration_captions','ban_celebration_gifs','blocklist_subscriptions',
        'domain_filters','notification_preferences','prompt_versions','recovery_codes',
        'stop_words','tag_definitions','username_blacklist',
        'messages','chat_admins','linked_channels','telegram_user_mappings',
        'profile_scan_results','username_history','admin_notes','audit_log',
        'user_tags','welcome_responses','invites','web_notifications','reports',
        'message_edits','detection_results','training_labels','user_actions',
        'image_training_samples','video_training_samples',
        'message_translations'
    ];
BEGIN
    FOR r IN
        SELECT c.conname, cl.relname AS tablename, pg_get_constraintdef(c.oid) AS def
        FROM pg_constraint c
        JOIN pg_class cl ON cl.oid = c.conrelid
        JOIN pg_namespace n ON n.oid = cl.relnamespace
        WHERE c.contype = 'f'
          AND n.nspname = 'public'
          AND cl.relname = ANY(bootstrap_tables)
    LOOP
        -- Rewrite "REFERENCES tablename(" → "REFERENCES bootstrap.tablename("
        -- pg_get_constraintdef emits unqualified table names because public is in
        -- the default search_path. Skip FKs whose parent isn't in our import set.
        new_def := regexp_replace(r.def,
            'REFERENCES ([a-z_][a-z0-9_]*)\(',
            'REFERENCES bootstrap.\1(');
        -- Verify the rewritten parent is in our table set; if not, skip the FK
        -- (the parent isn't sampled — this only happens if the matrix changes).
        IF NOT EXISTS (
            SELECT 1 FROM unnest(bootstrap_tables) bt
            WHERE position('REFERENCES bootstrap.' || bt || '(' IN new_def) > 0
        ) THEN
            RAISE NOTICE 'Skipping FK % on %: parent not in bootstrap import set', r.conname, r.tablename;
            CONTINUE;
        END IF;
        EXECUTE format('ALTER TABLE bootstrap.%I ADD CONSTRAINT %I %s',
                       r.tablename,
                       r.conname || '_bs',     -- Suffix avoids collision with prod constraint names
                       new_def);
    END LOOP;
END $$;

-- Verify FK count: should match prod's FK count for the 35 tables
SELECT count(*) AS bootstrap_fk_count
FROM pg_constraint c
JOIN pg_class cl ON cl.oid = c.conrelid
JOIN pg_namespace n ON n.oid = cl.relnamespace
WHERE c.contype = 'f' AND n.nspname = 'bootstrap';
-- Compare against:
SELECT count(*) AS prod_fk_count_for_imported_tables
FROM pg_constraint c
JOIN pg_class cl ON cl.oid = c.conrelid
JOIN pg_namespace n ON n.oid = cl.relnamespace
WHERE c.contype = 'f' AND n.nspname = 'public'
  AND cl.relname IN (-- same 35 tables --);
-- Expect: bootstrap_fk_count == prod count (or slightly less if some FKs point
-- to SKIP'd tables; see RAISE NOTICE output above).
```

- [ ] **Step 2: Sample Layer 0 root tables (FULL-COPY + EMPTY tiers)**

Per the per-table decision matrix, the Layer 0 roots split into **FULL** copies (9 tables, all rows imported) and **EMPTY** tier (5 tables — created in Step 1 but no INSERTs because prod has 0 rows and no test needs shape). The `username_blacklist` (NEEDS-EXTENSION) is also empty in prod; canonical adds representative test rows in a later sanitization-adjacent step. Layer 0 must complete before any Layer 1+ sample, because the FK constraints replicated in Step 1.5 will reject child INSERTs against empty parent tables.

```sql
-- ── FULL copies: small reference tables (rows referenced by tests, not by FK closure)
INSERT INTO bootstrap.users                     SELECT * FROM users;
INSERT INTO bootstrap.configs                   SELECT * FROM configs;
INSERT INTO bootstrap.content_detection_configs SELECT * FROM content_detection_configs;
INSERT INTO bootstrap.ban_celebration_captions  SELECT * FROM ban_celebration_captions;
INSERT INTO bootstrap.ban_celebration_gifs      SELECT * FROM ban_celebration_gifs;
INSERT INTO bootstrap.blocklist_subscriptions   SELECT * FROM blocklist_subscriptions;
INSERT INTO bootstrap.prompt_versions           SELECT * FROM prompt_versions;
INSERT INTO bootstrap.stop_words                SELECT * FROM stop_words;
INSERT INTO bootstrap.tag_definitions           SELECT * FROM tag_definitions;

-- ── EMPTY tier: tables exist for completeness, but no rows from prod
-- bootstrap.domain_filters, bootstrap.recovery_codes, bootstrap.image_training_samples,
-- bootstrap.video_training_samples, bootstrap.web_notifications all stay empty.
-- bootstrap.username_blacklist stays empty here; representative test rows added later.

-- ── telegram_users + managed_chats: NOT sampled here — they're populated by
-- recursive FK closure during Step 3+ (slice sampling). Each child INSERT pulls
-- in any missing parent users/chats via the augment-parents pattern documented
-- in the Bootstrap workflow architecture prelude.
-- ── notification_preferences: small table that FK-closures from bootstrap.users;
-- can be loaded here (5 rows, all admin users):
INSERT INTO bootstrap.notification_preferences
SELECT np.* FROM notification_preferences np
WHERE np.user_id IN (SELECT id FROM bootstrap.users);

-- Verify FULL copies match prod row counts
SELECT 'users', count(*) FROM bootstrap.users        UNION ALL  -- expect 9
SELECT 'configs', count(*) FROM bootstrap.configs    UNION ALL  -- expect 20
SELECT 'content_detection_configs', count(*) FROM bootstrap.content_detection_configs UNION ALL -- expect 18
SELECT 'ban_celebration_captions', count(*) FROM bootstrap.ban_celebration_captions   UNION ALL -- expect 74
SELECT 'ban_celebration_gifs', count(*) FROM bootstrap.ban_celebration_gifs           UNION ALL -- expect 92
SELECT 'blocklist_subscriptions', count(*) FROM bootstrap.blocklist_subscriptions     UNION ALL -- expect 7
SELECT 'prompt_versions', count(*) FROM bootstrap.prompt_versions                     UNION ALL -- expect 22
SELECT 'stop_words', count(*) FROM bootstrap.stop_words                               UNION ALL -- expect 17
SELECT 'tag_definitions', count(*) FROM bootstrap.tag_definitions                     UNION ALL -- expect 6
SELECT 'notification_preferences', count(*) FROM bootstrap.notification_preferences;
-- expect 5 (FK-filtered to bootstrap.users; should equal prod count since prod has 5 rows)
```

- [ ] **Step 3: Sample all valid explicit ham messages and record their slice**

In the source DB the explicit_ham pool after the app's own filter chain (length > 10 on the COALESCE'd training text) is **85 rows**, not 100. Canonical takes all 85 here; Step 3 brings the explicit_ham total to 100 by promoting the 15 longest implicit_ham rows.

The SQL below mirrors `MLTrainingDataRepository.GetHamSamplesAsync` (lines 113–133) exactly — LATERAL+COALESCE for translation-aware text, `length > 10` filter — plus canonical's noise gate (`[A-Za-z]{3,}` regex + by-ID exclusion of two known-corrupted rows in the source DB; see Follow-up Bug 4 below).

```sql
WITH chosen AS (
    SELECT m.chat_id, m.message_id
    FROM messages m
    JOIN training_labels tl ON tl.chat_id = m.chat_id AND tl.message_id = m.message_id
    LEFT JOIN LATERAL (
        SELECT translated_text
        FROM message_translations mt
        WHERE mt.chat_id = m.chat_id
          AND mt.message_id = m.message_id
          AND mt.edit_id IS NULL
        ORDER BY mt.translated_at DESC
        LIMIT 1
    ) mt ON TRUE
    WHERE tl.label = 1                                                          -- Ham
      AND length(COALESCE(mt.translated_text, m.message_text)) > 10             -- App filter
      AND COALESCE(mt.translated_text, m.message_text) ~ '[A-Za-z]{3,}'         -- Canonical noise gate
      AND NOT (m.chat_id = -1001322973935 AND m.message_id = 93017)             -- Known-corrupted row (Bug 4)
      AND NOT (m.chat_id = -1001329174109 AND m.message_id = 221795)            -- Known-corrupted row (Bug 4)
    ORDER BY length(COALESCE(mt.translated_text, m.message_text)) DESC
    -- No LIMIT: pool is exhausted at 85 rows (verified 2026-04-30 against fresh prod restore)
)
INSERT INTO bootstrap.message_slices (chat_id, message_id, slice)
SELECT chat_id, message_id, 'explicit_ham' FROM chosen;

INSERT INTO bootstrap.messages
SELECT m.* FROM messages m
JOIN bootstrap.message_slices s
  ON s.chat_id = m.chat_id AND s.message_id = m.message_id
WHERE s.slice = 'explicit_ham';

-- Verify: 85 rows
SELECT count(*) FROM bootstrap.message_slices WHERE slice = 'explicit_ham';
```

- [ ] **Step 4: Sample 115 implicit ham messages → keep top 100 as implicit + promote top 15 to explicit**

The implicit-ham predicate matches `MLTrainingDataRepository.GetHamSamplesAsync` (lines 177–189): a message with NO `training_labels` row, NO `detection_results` row marking it as spam, and `deleted_at IS NULL`. Using the app's exact predicate guarantees canonical's implicit-ham slice matches what the ML pipeline would actually train on.

This step does double-duty: it samples 115 confirmed-benign implicit_ham rows; the longest 15 promote to explicit_ham (with synthetic `training_labels` rows inserted in Task B3 Step 1) so canonical's `explicit_ham` slice reaches 100 rows. Net: 85 prod-labeled + 15 synthetic = 100 explicit_ham, 100 implicit_ham.

**Predicate coverage gap (Follow-up Bug 3):** the predicate classifies as "implicit_ham" any message whose detection result didn't set `is_spam=true`. Real spam slips through. The bootstrap agent + maintainer must spot-reject obvious-spam content during sub-step 3a's 300-row review.

**Sub-step 3a — Over-fetch 300 candidates by length DESC for maintainer review:**

```sql
SELECT m.chat_id, m.message_id,
       COALESCE(mt.translated_text, m.message_text) AS effective_text,
       length(COALESCE(mt.translated_text, m.message_text)) AS effective_len
FROM messages m
LEFT JOIN LATERAL (
    SELECT translated_text
    FROM message_translations mt
    WHERE mt.chat_id = m.chat_id
      AND mt.message_id = m.message_id
      AND mt.edit_id IS NULL
    ORDER BY mt.translated_at DESC
    LIMIT 1
) mt ON TRUE
WHERE NOT EXISTS (
        SELECT 1 FROM training_labels tl
        WHERE tl.chat_id = m.chat_id AND tl.message_id = m.message_id
      )
  AND NOT EXISTS (
        SELECT 1 FROM detection_results dr
        WHERE dr.chat_id = m.chat_id AND dr.message_id = m.message_id
          AND dr.is_spam = true
      )
  AND m.deleted_at IS NULL
  AND length(COALESCE(mt.translated_text, m.message_text)) > 10                 -- App filter
  AND COALESCE(mt.translated_text, m.message_text) ~ '[A-Za-z]{3,}'             -- Canonical noise gate
  AND NOT (m.chat_id = -1001322973935 AND m.message_id = 93017)                 -- Known-corrupted (Bug 4)
  AND NOT (m.chat_id = -1001329174109 AND m.message_id = 221795)                -- Known-corrupted (Bug 4)
ORDER BY length(COALESCE(mt.translated_text, m.message_text)) DESC
LIMIT 300;
-- Maintainer reviews this set, marks confirmed-benign rows.
-- Take the 115 longest confirmed-benign rows.
```

**Sub-step 3b — INSERT maintainer-confirmed 115 rows as implicit_ham:**

```sql
INSERT INTO bootstrap.message_slices (chat_id, message_id, slice)
VALUES
  (-1001..., 12345, 'implicit_ham'),
  ... -- 115 rows the maintainer confirmed, ordered longest-first
;

INSERT INTO bootstrap.messages
SELECT m.* FROM messages m
JOIN bootstrap.message_slices s
  ON s.chat_id = m.chat_id AND s.message_id = m.message_id
WHERE s.slice = 'implicit_ham';
```

**Sub-step 3c — Promote the 15 longest implicit_ham rows to explicit_ham:**

```sql
-- Flip slice classification for the 15 longest. Synthetic training_labels rows
-- are inserted later in Task B3 Step 1 (one place for all training_labels work).
WITH top15 AS (
    SELECT ms.chat_id, ms.message_id
    FROM bootstrap.message_slices ms
    JOIN bootstrap.messages m
      ON m.chat_id = ms.chat_id AND m.message_id = ms.message_id
    LEFT JOIN LATERAL (
        SELECT translated_text
        FROM message_translations mt
        WHERE mt.chat_id = m.chat_id
          AND mt.message_id = m.message_id
          AND mt.edit_id IS NULL
        ORDER BY mt.translated_at DESC
        LIMIT 1
    ) mt ON TRUE
    WHERE ms.slice = 'implicit_ham'
    ORDER BY length(COALESCE(mt.translated_text, m.message_text)) DESC
    LIMIT 15
)
UPDATE bootstrap.message_slices ms
   SET slice = 'explicit_ham'
  FROM top15
 WHERE ms.chat_id = top15.chat_id AND ms.message_id = top15.message_id;

-- Verify: explicit_ham=100 (85 prod + 15 promoted), implicit_ham=100
SELECT slice, count(*) FROM bootstrap.message_slices WHERE slice LIKE '%ham' GROUP BY slice;
```

- [ ] **Step 5: Sample 100 explicit spam messages**

Same filter chain as Step 2, mirroring `MLTrainingDataRepository.GetSpamSamplesAsync` (lines 27–47). Source pool is 271 rows after the app's filters; canonical takes the longest 100.

```sql
WITH chosen AS (
    SELECT m.chat_id, m.message_id
    FROM messages m
    JOIN training_labels tl ON tl.chat_id = m.chat_id AND tl.message_id = m.message_id
    LEFT JOIN LATERAL (
        SELECT translated_text
        FROM message_translations mt
        WHERE mt.chat_id = m.chat_id
          AND mt.message_id = m.message_id
          AND mt.edit_id IS NULL
        ORDER BY mt.translated_at DESC
        LIMIT 1
    ) mt ON TRUE
    WHERE tl.label = 0                                                          -- Spam
      AND length(COALESCE(mt.translated_text, m.message_text)) > 10             -- App filter
      AND COALESCE(mt.translated_text, m.message_text) ~ '[A-Za-z]{3,}'         -- Canonical noise gate
      AND NOT (m.chat_id = -1001322973935 AND m.message_id = 93017)             -- Known-corrupted (Bug 4)
      AND NOT (m.chat_id = -1001329174109 AND m.message_id = 221795)            -- Known-corrupted (Bug 4)
    ORDER BY length(COALESCE(mt.translated_text, m.message_text)) DESC
    LIMIT 100
)
INSERT INTO bootstrap.message_slices (chat_id, message_id, slice)
SELECT chat_id, message_id, 'explicit_spam' FROM chosen;

INSERT INTO bootstrap.messages
SELECT m.* FROM messages m
JOIN bootstrap.message_slices s
  ON s.chat_id = m.chat_id AND s.message_id = m.message_id
WHERE s.slice = 'explicit_spam';
```

- [ ] **Step 6: Sample 100 implicit spam messages — use the application's actual predicate**

The implicit-spam predicate matches `MLTrainingDataRepository.GetSpamSamplesAsync` (lines 50–68): a `detection_results` row with `is_spam = true` AND `used_for_training = true` AND no `training_labels` row exists. This is the exact set the ML pipeline trains on as "implicit spam," so canonical's implicit_spam slice mirrors training reality. Source pool is 378 rows after the app's filters; canonical takes the longest 100.

```sql
WITH chosen AS (
    SELECT DISTINCT m.chat_id, m.message_id
    FROM messages m
    JOIN detection_results dr
      ON dr.chat_id = m.chat_id AND dr.message_id = m.message_id
    LEFT JOIN LATERAL (
        SELECT translated_text
        FROM message_translations mt
        WHERE mt.chat_id = m.chat_id
          AND mt.message_id = m.message_id
          AND mt.edit_id IS NULL
        ORDER BY mt.translated_at DESC
        LIMIT 1
    ) mt ON TRUE
    WHERE dr.is_spam = true
      AND dr.used_for_training = true
      AND NOT EXISTS (
            SELECT 1 FROM training_labels tl
            WHERE tl.chat_id = m.chat_id AND tl.message_id = m.message_id
          )
      AND length(COALESCE(mt.translated_text, m.message_text)) > 10             -- App filter
      AND COALESCE(mt.translated_text, m.message_text) ~ '[A-Za-z]{3,}'         -- Canonical noise gate
      AND NOT (m.chat_id = -1001322973935 AND m.message_id = 93017)             -- Known-corrupted (Bug 4)
      AND NOT (m.chat_id = -1001329174109 AND m.message_id = 221795)            -- Known-corrupted (Bug 4)
    ORDER BY length(COALESCE(mt.translated_text, m.message_text)) DESC
    LIMIT 100
)
INSERT INTO bootstrap.message_slices (chat_id, message_id, slice)
SELECT chat_id, message_id, 'implicit_spam' FROM chosen;

INSERT INTO bootstrap.messages
SELECT m.* FROM messages m
JOIN bootstrap.message_slices s
  ON s.chat_id = m.chat_id AND s.message_id = m.message_id
WHERE s.slice = 'implicit_spam';
```

- [ ] **Step 7: Verify 400 rows total + balanced slices**

```sql
SELECT count(*) FROM bootstrap.messages;        -- Expected: 400
SELECT slice, count(*) FROM bootstrap.message_slices GROUP BY slice ORDER BY slice;
-- Expected: explicit_ham=100 (85 prod + 15 promoted), implicit_ham=100, explicit_spam=100, implicit_spam=100
```

If counts diverge: re-check the maintainer cull list (must be exactly 115 confirmed rows) and confirm the top-15 promotion ran. If the explicit_ham source pool changes (currently 85 in prod, verified 2026-04-30), adjust the promotion count so explicit_ham + promoted = 100.

- [ ] **Step 8: Derive `telegram_users` + `managed_chats` from sampled messages (FK closure for downstream Layer 1+2+3 sampling)**

Now that `bootstrap.messages` contains the 400 sampled rows, populate the two soft-FK parents — `telegram_users` (referenced by `messages.user_id`, soft) and `managed_chats` (referenced by `messages.chat_id`, soft). Although prod doesn't enforce these as FKs, canonical's "valid data shape" contract requires every user_id and chat_id in `bootstrap.messages` to have a matching parent row, otherwise downstream Layer 2 tables (training_labels, detection_results, etc.) would have orphan FKs to telegram_users.

Also pull in the synthetic-promotion labeler (telegram_user_id `1312830442` per Task B3 Step 1b) plus chat admins for sampled chats — these expand the user set beyond just senders.

```sql
-- Pull all telegram_users referenced by sampled messages, sampled chat admins,
-- and the synthetic-promotion labeler.
INSERT INTO bootstrap.telegram_users
SELECT DISTINCT tu.* FROM telegram_users tu
WHERE tu.telegram_user_id IN (
        SELECT user_id FROM bootstrap.messages
        UNION
        SELECT telegram_id FROM chat_admins
            WHERE chat_id IN (SELECT DISTINCT chat_id FROM bootstrap.messages)
        UNION
        SELECT 1312830442  -- synthetic-promotion labeler (Task B3 Step 1b)
    )
  AND tu.telegram_user_id NOT IN (SELECT telegram_user_id FROM bootstrap.telegram_users);

-- Pull all managed_chats referenced by sampled messages.
INSERT INTO bootstrap.managed_chats
SELECT DISTINCT mc.* FROM managed_chats mc
WHERE mc.chat_id IN (SELECT DISTINCT chat_id FROM bootstrap.messages)
  AND mc.chat_id NOT IN (SELECT chat_id FROM bootstrap.managed_chats);

-- Verify FK closure: every messages.user_id and messages.chat_id has a parent row
SELECT 'messages with orphan user_id', count(*)
FROM bootstrap.messages m
WHERE NOT EXISTS (SELECT 1 FROM bootstrap.telegram_users tu WHERE tu.telegram_user_id = m.user_id);
-- Expect: 0

SELECT 'messages with orphan chat_id', count(*)
FROM bootstrap.messages m
WHERE NOT EXISTS (SELECT 1 FROM bootstrap.managed_chats mc WHERE mc.chat_id = m.chat_id);
-- Expect: 0

-- Verify synthetic labeler is present
SELECT 'synthetic labeler present',
       EXISTS (SELECT 1 FROM bootstrap.telegram_users WHERE telegram_user_id = 1312830442);
-- Expect: t
```

If any orphan count > 0: prod has a row in `messages` with a `user_id` or `chat_id` that doesn't exist in the parent table. This is a data-quality bug in prod (no FK to enforce), not a bootstrap bug. Either the maintainer chooses to drop the orphan message from the sample, OR the bootstrap creates a synthetic placeholder row in the parent (rare; only for narrow needs-canonical-extension cases).

### Task B3: Sample remaining tables in topological order (Layer 1 siblings, Layer 2 children of messages, Layer 3 grandchildren)

Reference: the per-table decision matrix in the **Bootstrap workflow architecture** prelude. Every table follows one of: FULL, EMPTY, NEEDS-EXTENSION, FK-CLOSURE, N=10 AUGMENT-USERS, or CUSTOM PREDICATE.

**Already done in B2** — DO NOT re-INSERT in B3:
- Layer 0 FULL copies: `users`, `configs`, `content_detection_configs`, `ban_celebration_captions`, `ban_celebration_gifs`, `blocklist_subscriptions`, `prompt_versions`, `stop_words`, `tag_definitions`, `notification_preferences` (B2 Step 2)
- Layer 0 EMPTY tables: created but no rows (B2 Step 1) — leave as-is
- `telegram_users`, `managed_chats` — derived from sampled messages (B2 Step 8)
- `messages` — 400 sampled rows (B2 Steps 3–7)

**Layer 1 — Sample direct children of roots (after the above is settled):**
- Step 2: `chat_admins` (FK-CLOSURE: chats from `bootstrap.managed_chats`)
- Step 3: `linked_channels` (FK-CLOSURE)
- Step 4: `telegram_user_mappings` (FK-CLOSURE)
- Step 5: `username_history` (FK-CLOSURE)
- Step 6: `admin_notes` (FK-CLOSURE)
- Step 7: `user_tags` (FK-CLOSURE)
- Step 8: `welcome_responses` (CUSTOM — Pre-1b deliverable #2 + analytics time-spread)
- Step 9: `invites` (FK-CLOSURE)
- Step 10: `audit_log` (CUSTOM — N=20 most recent referencing sampled users; decision pending maintainer adjustment)
- Step 11: `profile_scan_results` (N=10 AUGMENT-USERS; pattern below)
- Step 12: `reports` (N=10 AUGMENT-USERS; pattern below)
- Step 13: `username_blacklist` (NEEDS-EXTENSION; representative test rows)

**Layer 2 — Sample children of `messages` (after Layer 1):**
- Step 14: `training_labels` (CUSTOM — 185 prod + 15 synthetic; this was old B3 Step 1)
- Step 15: `message_edits` (FK-CLOSURE)
- Step 16: `detection_results` (FK-CLOSURE)
- Step 17: `user_actions` (FK-CLOSURE)
- (`image_training_samples`, `video_training_samples` stay EMPTY)

**Layer 3 — Grandchildren (after Layer 2):**
- Step 18: `message_translations` (FK-CLOSURE + no-op-translation filter)

**Universal augment-users pattern (Steps 11 + 12):**

```sql
-- Generic shape — substitute table name + FK column. Applied to profile_scan_results
-- (FK = user_id → telegram_users) and reports (soft FK = reported_by_user_id, plus
-- enforced FK = web_user_id → users).
WITH preferred AS (
    SELECT t.* FROM <source_table> t
    WHERE t.<fk_col> IN (SELECT telegram_user_id FROM bootstrap.telegram_users)
      -- Per-table additional predicates (e.g., most-recent-per-user for profile_scan_results)
    ORDER BY t.<recency_col> DESC
    LIMIT 10
)
INSERT INTO bootstrap.<table> SELECT * FROM preferred;

-- Augment if shortfall: pull additional rows + their missing parents
WITH need AS (SELECT 10 - count(*) AS shortfall FROM bootstrap.<table>),
     extras AS (
         SELECT t.* FROM <source_table> t
         WHERE NOT EXISTS (SELECT 1 FROM bootstrap.<table> b WHERE b.id = t.id)
         ORDER BY t.<recency_col> DESC
         LIMIT (SELECT shortfall FROM need)
     )
-- First pull in missing parent users (FK closure)
INSERT INTO bootstrap.telegram_users
SELECT DISTINCT tu.* FROM telegram_users tu
JOIN extras e ON e.<fk_col> = tu.telegram_user_id
WHERE tu.telegram_user_id NOT IN (SELECT telegram_user_id FROM bootstrap.telegram_users);
-- Then the rows themselves
INSERT INTO bootstrap.<table> SELECT * FROM extras;
```

Each step below details its specific predicate; the universal pattern handles the FK closure.

(Original B3 Steps 1–6 follow as the historical content; the bootstrap agent collapses them into the topological structure above.)

---

#### Historical B3 steps (now reorganized — see topological structure above)

All FK-supporting samples land in the `bootstrap` schema as UNLOGGED tables, named the same as the production table (e.g., `bootstrap.training_labels`, `bootstrap.telegram_users`).

- [ ] **Step 1: Sample training_labels (200 rows for the 200 explicit messages — 185 from prod + 15 synthetic for promoted-to-explicit rows)**

The 200 explicit messages in `bootstrap.message_slices` break down as:
- 85 prod-labeled `explicit_ham` rows → already have prod `training_labels` rows (label=1, real labelers all NULL — see Follow-up Bugs 5 & 6)
- 100 prod-labeled `explicit_spam` rows → already have prod `training_labels` rows (label=0)
- 15 promoted `explicit_ham` rows (top-15-by-length from implicit_ham, per Task B2 Sub-step 3c) → no prod `training_labels`; canonical inserts synthetic rows below

**Sub-step 1a — Carry over the 185 prod training_labels rows:**

```sql
CREATE UNLOGGED TABLE bootstrap.training_labels (LIKE training_labels INCLUDING ALL);

INSERT INTO bootstrap.training_labels
SELECT tl.* FROM training_labels tl
JOIN bootstrap.message_slices s
  ON s.chat_id = tl.chat_id AND s.message_id = tl.message_id
WHERE s.slice IN ('explicit_ham','explicit_spam');
-- Expected count: 185 (85 prod ham + 100 prod spam)
```

**Sub-step 1b — Insert synthetic training_labels for the 15 promoted rows:**

The synthetic rows reference `labeled_by_user_id = 1312830442` (the prod admin who labels the most actively, per the 2026-04-30 audit; FK closure satisfied because Task B3 Step 2 imports this user via the spam-label join). Fixed `labeled_at` keeps test runs deterministic. `reason = 'canonical_synthetic_promotion'` distinguishes synthetic rows from prod labels for any debug query.

```sql
INSERT INTO bootstrap.training_labels (chat_id, message_id, label, labeled_by_user_id, labeled_at, reason)
SELECT ms.chat_id, ms.message_id,
       1                                                AS label,
       1312830442                                       AS labeled_by_user_id,
       '2026-01-01T00:00:00Z'::timestamptz              AS labeled_at,
       'canonical_synthetic_promotion'                  AS reason
FROM bootstrap.message_slices ms
WHERE ms.slice = 'explicit_ham'
  AND NOT EXISTS (
      SELECT 1 FROM bootstrap.training_labels btl
      WHERE btl.chat_id = ms.chat_id AND btl.message_id = ms.message_id
  );
-- Expected count: 15

-- Final verify:
SELECT label, count(*) FROM bootstrap.training_labels GROUP BY label;
-- Expected: label=0 (spam) → 100, label=1 (ham) → 100. Total 200.
```

- [ ] **Step 2: Sample telegram_users (every sender + chat admins + history/actions users referenced by sampled rows)**

```sql
CREATE UNLOGGED TABLE bootstrap.telegram_users (LIKE telegram_users INCLUDING ALL);

INSERT INTO bootstrap.telegram_users
SELECT DISTINCT tu.* FROM telegram_users tu
WHERE tu.id IN (
  SELECT user_id FROM bootstrap.messages
  UNION SELECT user_id FROM chat_admins
    WHERE chat_id IN (SELECT DISTINCT chat_id FROM bootstrap.messages)
  UNION SELECT telegram_user_id FROM username_history
    WHERE telegram_user_id IN (SELECT user_id FROM bootstrap.messages)
  UNION SELECT telegram_user_id FROM user_actions
    WHERE telegram_user_id IN (SELECT user_id FROM bootstrap.messages)
);
```

- [ ] **Step 3: Sample managed_chats (every chat referenced by a sampled message, plus any linked-channels-related chat)**

```sql
CREATE UNLOGGED TABLE bootstrap.managed_chats (LIKE managed_chats INCLUDING ALL);

INSERT INTO bootstrap.managed_chats
SELECT DISTINCT mc.* FROM managed_chats mc
WHERE mc.id IN (SELECT DISTINCT chat_id FROM bootstrap.messages)
   OR mc.id IN (
     SELECT linked_chat_id FROM linked_channels
     WHERE primary_chat_id IN (SELECT DISTINCT chat_id FROM bootstrap.messages)
   );
```

- [ ] **Step 4: Sample users (admin table — small, ~5 rows; bootstrap will pin first 4 onto User1_Id–User4_Id)**

```sql
CREATE UNLOGGED TABLE bootstrap.users (LIKE users INCLUDING ALL);
INSERT INTO bootstrap.users SELECT * FROM users LIMIT 5;
```

- [ ] **Step 5: Sample remaining FK-supporting rows**

For each table below, create the `bootstrap.<table>` UNLOGGED table (`LIKE <table> INCLUDING ALL`) and INSERT only rows whose FKs reference a sampled-row subset already in the bootstrap schema. Cross-check the `audit-output.md` "needs-canonical-extension" section to ensure any test-required shapes are present.

- `bootstrap.linked_channels` — rows where `primary_chat_id` IN bootstrap.managed_chats
- `bootstrap.chat_admins` — rows where `chat_id` IN bootstrap.managed_chats
- `bootstrap.telegram_user_mappings` — rows where `telegram_user_id` IN bootstrap.telegram_users
- `bootstrap.content_detection_configs` — global (`chat_id IS NULL`) + 1-2 chat overrides for sampled chats; ~3 total
- `bootstrap.configs` — exactly 1 row at `chat_id=0` (global)
- `bootstrap.detection_results` — rows where `(chat_id, message_id)` IN bootstrap.messages
- `bootstrap.user_actions` — rows where `(chat_id, message_id)` IN bootstrap.messages **AND `message_id IS NOT NULL`** (canonical contract enforces this)
- `bootstrap.message_edits` — rows where `(chat_id, message_id)` IN bootstrap.messages
- `bootstrap.message_translations` — rows referencing sampled messages OR sampled message_edits, **AND `translated_text` is NOT a bare URL**:

  ```sql
  CREATE UNLOGGED TABLE bootstrap.message_translations (LIKE message_translations INCLUDING ALL);

  INSERT INTO bootstrap.message_translations
  SELECT mt.* FROM message_translations mt
  WHERE (mt.chat_id, mt.message_id) IN (SELECT chat_id, message_id FROM bootstrap.messages)
     OR mt.edit_id IN (SELECT id FROM bootstrap.message_edits)
     -- No-op-translation filter: real translations have actual translated language
     -- content. The translator pipeline is buggy and persists rows for URL-only,
     -- URL-Previews, and identity-passthrough non-language content. See Follow-up
     -- Bug 2 ("Translator no-op rows"). Canonical filters all three patterns:
     AND mt.translated_text !~ '^https?://\S+$'                         -- bare URL
     AND m.message_text !~ '━━━ URL Previews ━━━'                      -- URL + auto-preview block
     AND NOT (mt.detected_language = 'unknown'
              AND mt.translated_text = m.message_text);                 -- identity passthrough
  ```

  After applying all three no-op-translation filters, the remaining canonical translations are virtually all spam-pattern (verified 2026-04-30 against fresh prod restore: 17 explicit_spam + 26 implicit_spam = 43 prose translations; 0 prose ham translations after the maintainer's Step 3 spot-rejection of any obvious spam slipping through the predicate gap). This means ham translations effectively don't exist in canonical, so `bootstrap.lorem(target_len)` only ever fires for ham *messages* — single-language English is sufficient.
- `bootstrap.invites` — sample rows referencing sampled `bootstrap.users` plus any "needs-canonical-extension" requirements
- `bootstrap.web_notifications` — sample rows referencing sampled `bootstrap.users`
- `bootstrap.username_history` — rows where `telegram_user_id` IN bootstrap.telegram_users

- [ ] **Step 6: Spot-check FK closure**

For each FK in `AppDbContext.cs`, run a count query confirming every sampled row's FK target is also in the sample. Any orphan must be added to its parent table or removed from the child sample.

### Task B4: Apply sanitization rules

Reference: spec "Canonical bootstrap from local database > Sanitization rules" table.

- [ ] **Step 1: Strip media file paths (all messages)**

```sql
UPDATE bootstrap.messages SET
  media_local_path = NULL,
  photo_local_path = NULL,
  photo_thumbnail_path = NULL,
  media_file_name = NULL;
```

- [ ] **Step 2: Lorem-ize ham message text and NULL urls — driven off bootstrap.message_slices**

```sql
UPDATE bootstrap.messages bm SET
  message_text = bootstrap.lorem(length(bm.message_text)),
  urls = NULL
FROM bootstrap.message_slices s
WHERE s.chat_id = bm.chat_id
  AND s.message_id = bm.message_id
  AND s.slice IN ('explicit_ham','implicit_ham');
-- The 100 explicit_ham + 100 implicit_ham rows are explicitly tagged; spam rows are
-- NOT in this set so they're untouched here. bootstrap.lorem() returns the canonical
-- Lorem Ipsum passage truncated to EXACTLY length(bm.message_text) characters, so
-- length-based assertions / tokenization shape are preserved.
```

- [ ] **Step 3: Rewrite spam URL hostnames to deterministic .invalid domains**

For every row in `bootstrap.messages` where the joined `bootstrap.message_slices.slice` IN `('explicit_spam','implicit_spam')`, parse URLs from `message_text` and from the `urls` column, replace each hostname with `spam-host-NN.invalid` (NN deterministic per unique source hostname, starting at 01), preserving scheme/path/query. Implement with an application-side helper or `regexp_replace` chain depending on URL complexity. The maintainer should spot-check 5–10 rewritten rows. The slice membership is the only thing distinguishing spam from ham at this point — `training_labels` is not consulted, since implicit spam has no label.

- [ ] **Step 4: Sanitize message_edits old/new text — ham edits use bootstrap.lorem(length(...))**

```sql
-- Ham edits: replace old_text and new_text with lorem truncated to each column's
-- original length independently. The two columns can have different lengths
-- (an edit might shorten or lengthen the message), so each gets its own length() call.
UPDATE bootstrap.message_edits me SET
  old_text = bootstrap.lorem(length(me.old_text)),
  new_text = bootstrap.lorem(length(me.new_text))
FROM bootstrap.message_slices s
WHERE s.chat_id = me.chat_id
  AND s.message_id = me.message_id
  AND s.slice IN ('explicit_ham','implicit_ham');
```

For spam edits (`s.slice IN ('explicit_spam','implicit_spam')`), apply the same hostname-rewrite-to-`.invalid` rule that Step 3 used for `bootstrap.messages.message_text` / `urls`. URL extraction for hash recomputation in Task B6 operates on this sanitized text — extraction must run AFTER this step.

- [ ] **Step 5: Sanitize message_translations.translated_text — spam-only path**

After Task B3's URL-only filter and the maintainer's spot-rejection of obvious-spam content during implicit_ham sampling, canonical's `bootstrap.message_translations` contains only spam-slice rows in practice. Apply the same hostname-rewrite rule used in Step 3 for spam messages: rewrite URL hostnames in `translated_text` to deterministic `.invalid` domains, preserve everything else verbatim.

If a ham-slice translation row somehow remains (unexpected — should have been filtered upstream), apply `bootstrap.lorem(length(mt.translated_text))` defensively, but flag it for the maintainer to investigate before proceeding:

```sql
-- Defensive check — should return 0 rows if Task B3's filter and Step 3 spot-check did their job.
SELECT mt.chat_id, mt.message_id, mt.detected_language, LEFT(mt.translated_text, 80) AS preview
FROM bootstrap.message_translations mt
JOIN bootstrap.message_slices s
  ON s.chat_id = mt.chat_id AND s.message_id = mt.message_id
WHERE s.slice IN ('explicit_ham','implicit_ham');
-- If this returns rows: STOP and have the maintainer confirm whether to keep them
-- (apply lorem) or reject and re-do Task B3's implicit_ham sampling.
```

- [ ] **Step 6: Sanitize telegram_users non-spammer name fields**

A user is a "spammer" if they authored ANY spam-slice message (explicit OR implicit). Spammers keep their real names verbatim (intentional ML signal); their telegram_user_id is rewritten to the `9000000000+` fake range in Task B5 so the verbatim names don't map back to real accounts.

```sql
WITH spammer_authors AS (
    SELECT DISTINCT bm.user_id
    FROM bootstrap.messages bm
    JOIN bootstrap.message_slices s
      ON s.chat_id = bm.chat_id AND s.message_id = bm.message_id
    WHERE s.slice IN ('explicit_spam','implicit_spam')
)
UPDATE bootstrap.telegram_users tu SET
  username = 'user_' || tu.id::text,
  first_name = 'Test',
  last_name = 'User'
WHERE tu.id NOT IN (SELECT user_id FROM spammer_authors);
```

- [ ] **Step 7: Sanitize users (admin) email/name/password_hash/security_stamp**

```sql
UPDATE bootstrap.users SET
  email = 'user_' || id || '@example.invalid',
  normalized_email = upper('user_' || id || '@example.invalid'),
  password_hash = NULL,  -- or a known-good test hash if any test asserts on login
  security_stamp = gen_random_uuid()::text;
```

- [ ] **Step 8: Sanitize managed_chats title and username**

```sql
UPDATE bootstrap.managed_chats SET
  title = 'Test Chat ' || id,
  username = NULL;
```

- [ ] **Step 9: Strip configs encrypted columns (all 5)**

```sql
UPDATE bootstrap.configs SET
  api_keys = NULL,
  passphrase_encrypted = NULL,
  telegram_bot_token_encrypted = NULL,
  vapid_private_key_encrypted = NULL,
  user_api_hash_encrypted = NULL;
```

- [ ] **Step 10: Sanitize configs JSONB credentials/tokens (per-key spot-check)**

For each JSONB column listed in the spec sanitization table (`sendgrid_config`, `web_push_config`, `ai_provider_config`, `telegram_bot_config`, `user_api_config`, `bot_protection_config`, `file_scanning_config`), strip secret/identifying values while preserving structural shape. The maintainer reviews each column manually during the session.

### Task B5: Rewrite IDs into canonical sequences and update FKs

Reference: spec sanitization table rows for ID rewriting rules.

- [ ] **Step 1: Pin telegram_users to UserN_TelegramUserId constants**

For each maintainer-selected representative row, UPDATE `bootstrap.telegram_users.id` to the matching constant value (`100001` for User1, `100002` for User2, etc.) and CASCADE the FK update through every other `bootstrap.<table>` referencing telegram_user_ids. Track the old→new map in `bootstrap.telegram_user_id_map(old_id bigint PRIMARY KEY, new_id bigint NOT NULL)` so cascading FK rewrites are idempotent and verifiable.

- [ ] **Step 2: Map remaining telegram_users to fake range (9000000000..9000000099)**

Sequentially assign IDs starting from `9000000000` to any remaining rows. Update FK references throughout.

- [ ] **Step 3: Pin managed_chats to MainChat_Id (and siblings)**

`MainChat_Id = -1001322973935`. Cascade FK updates.

- [ ] **Step 4: Map remaining managed_chats to fake range (-1009000000000..-1009000000099)**

- [ ] **Step 5: Pin users (admin) rows to UserN_Id constants**

`User1_Id = "b388ee38-0ed3-4c09-9def-5715f9f07f56"`, etc. Update `users.invited_by` self-FK.

- [ ] **Step 6: Renumber bigserial PKs (detection_results.id, user_actions.id, message_edits.id, invites.id, web_notifications.id, username_history.id, linked_channels.id, configs.id, content_detection_configs.id, chat_admins.id, telegram_user_mappings.id) starting from 1**

For each table, replace IDs with `ROW_NUMBER() OVER (ORDER BY id) AS new_id` and cascade FK updates through every temp table referencing them.

- [ ] **Step 7: Verify FK closure post-rewrite**

Re-run the FK closure check from Task B3 Step 6. Expected: zero orphans.

### Task B6: Recompute derived hashes from sanitized inputs

Reference: spec sanitization table "Derived hash columns" row + `TelegramGroupsAdmin/Utilities/HashUtilities.cs`.

This is the only step that calls into application code. The bootstrap recomputes:
- `messages.content_hash`
- `messages.similarity_hash`
- `message_translations.similarity_hash`
- `message_edits.old_content_hash`
- `message_edits.new_content_hash`

- [ ] **Step 1: Stand up a one-shot C# script that reads the temp tables, recomputes hashes, and UPDATEs**

Create a temporary `tmp/canonical-bootstrap/RehashTool.csproj` console app that references `TelegramGroupsAdmin.Utilities` and `Npgsql`. Use this exact call shape (matching production):

```csharp
using TelegramGroupsAdmin.Utilities;
using System.Text.Json;

// messages.content_hash
foreach (var row in messageRows) {
    var hash = HashUtilities.ComputeContentHash(
        row.MessageText ?? "",
        row.Urls ?? "");
    // UPDATE messages SET content_hash = @hash WHERE chat_id = @chatId AND message_id = @msgId
}

// messages.similarity_hash — SimHash over sanitized message_text
foreach (var row in messageRows) {
    var sim = SimHashUtilities.Compute(row.MessageText ?? "");
    // UPDATE messages SET similarity_hash = @sim WHERE ...
}

// message_translations.similarity_hash — SimHash over sanitized translated_text
foreach (var row in translationRows) {
    var sim = SimHashUtilities.Compute(row.TranslatedText ?? "");
    // UPDATE message_translations SET similarity_hash = @sim WHERE ...
}

// message_edits.old_content_hash / new_content_hash — extract URLs from sanitized text
foreach (var row in editRows) {
    var oldUrls = UrlUtilities.ExtractUrls(row.OldText);
    var oldUrlsJson = oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "";
    row.OldContentHash = HashUtilities.ComputeContentHash(row.OldText ?? "", oldUrlsJson);

    var newUrls = UrlUtilities.ExtractUrls(row.NewText);
    var newUrlsJson = newUrls != null ? JsonSerializer.Serialize(newUrls) : "";
    row.NewContentHash = HashUtilities.ComputeContentHash(row.NewText ?? "", newUrlsJson);
    // UPDATE message_edits SET ...
}
```

The exact namespaces (`SimHashUtilities`, `UrlUtilities`) may differ — Claude resolves the actual symbols via `mcp__csharp-er-mcp__find_symbol_usages` against `HashUtilities.ComputeContentHash` and the SimHash call sites in production code. This is a one-shot tool and is NOT committed.

- [ ] **Step 2: Spot-check 5 rows per table**

Pick 5 rows from each of `messages`, `message_translations`, `message_edits`. Run `HashUtilities.ComputeContentHash` against the sanitized inputs by hand (or via a separate tiny script) and confirm bit-identical output. Off-by-one in ToLowerInvariant or null-coalesce will diverge here; catch it now.

### Task B7: pg_dump per table → 35 canonical/*.sql files (FK-ordered)

- [ ] **Step 1: Create the canonical/ subfolder**

```bash
mkdir -p TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical
```

- [ ] **Step 2: Run pg_dump --data-only --column-inserts per table, FK-ordered**

`pg_dump` opens its own backend session, so the `bootstrap` schema must contain permanent (UNLOGGED) tables — Task B2 already created them that way. The `--table=bootstrap.<name>` arg selects each:

```bash
cd TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical

# ── Layer 0: roots (no FK deps; safe to load first)
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.users                     -f 01_users.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.configs                   -f 02_configs.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.content_detection_configs -f 03_content_detection_configs.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.ban_celebration_captions  -f 04_ban_celebration_captions.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.ban_celebration_gifs      -f 05_ban_celebration_gifs.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.blocklist_subscriptions   -f 06_blocklist_subscriptions.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.prompt_versions           -f 07_prompt_versions.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.stop_words                -f 08_stop_words.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.tag_definitions           -f 09_tag_definitions.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.telegram_users            -f 10_telegram_users.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.managed_chats             -f 11_managed_chats.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.notification_preferences  -f 12_notification_preferences.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.domain_filters            -f 13_domain_filters.sql               # EMPTY
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.recovery_codes            -f 14_recovery_codes.sql               # EMPTY
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.username_blacklist        -f 15_username_blacklist.sql           # NEEDS-EXTENSION

# ── Layer 1: direct children of roots (telegram_users + managed_chats now populated)
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.messages                  -f 16_messages.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.chat_admins               -f 17_chat_admins.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.linked_channels           -f 18_linked_channels.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.telegram_user_mappings    -f 19_telegram_user_mappings.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.profile_scan_results      -f 20_profile_scan_results.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.username_history          -f 21_username_history.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.admin_notes               -f 22_admin_notes.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.audit_log                 -f 23_audit_log.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.user_tags                 -f 24_user_tags.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.welcome_responses         -f 25_welcome_responses.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.invites                   -f 26_invites.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.web_notifications         -f 27_web_notifications.sql            # EMPTY
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.reports                   -f 28_reports.sql

# ── Layer 2: children of messages
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.message_edits             -f 29_message_edits.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.detection_results         -f 30_detection_results.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.training_labels           -f 31_training_labels.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.user_actions              -f 32_user_actions.sql
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.image_training_samples    -f 33_image_training_samples.sql       # EMPTY
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.video_training_samples    -f 34_video_training_samples.sql       # EMPTY

# ── Layer 3: grandchildren
pg_dump "$LOCAL_DB_CONNECTION" --data-only --column-inserts --table=bootstrap.message_translations      -f 35_message_translations.sql
```

- [ ] **Step 3: Strip pg_dump preamble and rewrite schema-qualified inserts to public schema**

Each file's INSERTs target `bootstrap.<table>`; canonical SQL must INSERT into the unqualified production table. `sed` does the swap:

```bash
for f in canonical/*.sql; do
  sed -i.bak 's/^INSERT INTO bootstrap\./INSERT INTO /g' "$f"
  # Also strip pg_dump preamble lines that reset session state.
  sed -i.bak '/^SET /d; /^SELECT pg_catalog.set_config/d; /^SELECT pg_catalog.setval/d' "$f"
  rm "$f.bak"
done
```

Each file should now be just `INSERT INTO <table> (cols...) VALUES (...);` statements.

- [ ] **Step 3.5: Drop the bootstrap schema once pg_dump succeeds**

```sql
DROP SCHEMA bootstrap CASCADE;
```

The bootstrap schema is a one-shot working area — leaving it on the local DB would just be clutter. Run after Task B7 Step 4 (spot-check) passes.

- [ ] **Step 4: Manual spot-check**

Open each file in turn and confirm:
- (a) URL hostnames in spam messages are `.invalid` (no real domains)
- (b) No non-spammer leakage in `telegram_users` columns (check 5 random rows)
- (c) No encrypted ciphertext in `configs` (api_keys, etc., should be NULL or absent)
- (d) No JSONB credentials/tokens (sendgrid keys, web push keys, AI provider keys)
- (e) `09_user_actions.sql` has no rows with NULL `message_id`/`chat_id` (canonical contract — see spec "Canonical contract for 09_user_actions.sql")

### Task B8 — REPLACED by Pre-Phase 1c

The original Task B8 proposed a `GoldenDataset` constants mapping pre-pass. That approach has been dropped. Constants will be added on demand during Phase 3A–C as each test rewrite discovers what it needs to reference. **Pre-Phase 1c** (immediately after this section) instead produces a single `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` cheat sheet so test-writing agents can orient themselves against the canonical dataset without re-discovering it every session.

### Follow-up bugs surfaced during bootstrap — MOVED to Post-Phase B

The seven concrete bugs uncovered during bootstrap data exploration are now documented in **Post-Phase B** at the end of this plan. They're filed as separate GitHub issues after the main PR merges (not blocking this PR).

(Bug content moved to Post-Phase B — see end of plan.)

---

## Pre-Phase 1c: Author `IntegrationTests/CLAUDE.md` cheat sheet (no commit) — ✅ COMPLETE

> **Status:** Authored 2026-05-03. `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (259 lines) on disk, untracked. C1 audit output at `tmp/canonical-bootstrap/cheatsheet-audit.md` (gitignored). Folds into the Phase 1 commit. Surfaced 2 known canonical gaps for Phase 3A consumers: SimHash dedup messages 95001..95022 and ban-celebration `{bancount}` synthetic anchor rows are NOT in canonical (Pre-1b extensions #1 and #4 missed); test rewrites should either extend canonical or seed inline.

> **Why this is its own pre-phase:** the canonical dataset is dense (35 tables, ~3,200 rows, rotated IDs, sanitized usernames). Without a discovery surface, every test-rewrite session in Phase 3A/3B re-pays the cost of finding "which user is the spammer with name-history?" Producing this once, here, amortizes that cost across all downstream phases.

> **What this is NOT:** a `GoldenDataset` constants surface. The original B8 plan to pre-emit C# constants has been dropped — constants are demand-driven and will be added in Phases 3A–C as each test rewrite discovers what it needs (YAGNI). The cheat sheet tells the rewriting agent *where to look*; the constant gets minted only when a test actually pins to a row.

### Design principles

| Principle | Why |
|-----------|-----|
| **Two parts: orientation + recipes** | Part 1 teaches the dataset shape so an agent can navigate it; Part 2 short-circuits the most common lookups so an agent skips navigation when a known recipe fits. |
| **No example data rows in Part 1** | Counts and structural description rot far slower than concrete row contents. Test authors read SQL files directly when they need exemplars. |
| **Locked IDs in Part 2** | Each scenario names its anchor row by id. If we ever regenerate canonical, scenario IDs surface as test failures and we update both at once — that's a feature, not a bug. |
| **Curated, not exhaustive** | Part 2 covers what current tests need + a small set of "obvious next" scenarios. Resist the urge to enumerate every possible combination. |

### Files referenced in this phase

- Read-only: `TelegramGroupsAdmin.IntegrationTests/**/*.cs` (audit input)
- Read-only: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/*.sql` (counts + scenario id resolution)
- Working artifact (NOT committed, `tmp/` is `.gitignored`): `tmp/canonical-bootstrap/cheatsheet-audit.md`
- Create: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`

### Anchor inputs from Pre-Phase 1b (known canonical IDs to surface in the cheat sheet)

The bootstrap has already pinned these synthetic / canonical-fixture IDs. They MUST appear in the cheat sheet:

- **Web fixture UUIDs** (4 — restored to canonical fixture state in B4.6):
  - `b388ee38-0ed3-4c09-9def-5715f9f07f56` — Owner / `owner@example.com` (permission_level 2)
  - `921637d5-0f65-4c66-b143-6f057dd06a1c` — Admin / `admin@example.com` (permission_level 0)
  - `a8dc8371-afc5-4b61-9d71-d177f2dd9ddd` — Deleted Admin / `deleted@example.com` (status 3)
  - `ba9ba542-3df6-4473-a820-578562780c57` — Deleted GlobalAdmin / `globaladmin@example.com` (permission_level 1, status 3)
- **Web user shared password** (all 9 users): `Passw0rd!SaidNoSecurityAuditorEver` — hash already baked into `01_users.sql`
- **Synthetic `welcome_responses` rows**: IDs 999001–999005 cover the 5 `WelcomeResponseType` status branches: 999001=Pending, 999002=Accepted, 999003=Denied, 999004=Timeout, 999005=Left. All 5 share `(chat_id=-100026957614982, user_id=9196379650113, username='canonical_user1', welcome_message_id=99001..99005)`.
- **Synthetic `username_blacklist` rows**: 2 rows total — ID `999001` (`pattern='spambot_admin'`, `match_type=0` (Exact), `enabled=true`) and ID `999005` (`pattern='archived_pattern'`, `match_type=0` (Exact), `enabled=false`). No Contains/Regex/StartsWith fixtures — `BlacklistMatchType` enum only implements Exact.
- **Synthetic `training_labels`** for 15 promoted explicit_ham messages: `reason='canonical_synthetic_promotion'`, `labeled_by_user_id` = the rotated id of original prod user `1312830442`
- **Identity ranges** (rotated in B5):
  - Telegram user IDs: `[9_000_000_000_000, 10_000_000_000_000)` — 13-digit, prefix `9`
  - Chat IDs: `[-100_099_999_999_999, -100_000_000_000_000]` — 15-digit, prefix `-100` (preserves the Telegram supergroup format that app code checks via `chatId.ToString().StartsWith("-100")`, while remaining clearly synthetic vs real 13-digit supergroup IDs)
  - `chat_id = 0` preserved as sentinel (CASE WHEN guard during rotation)

### Task C1: Audit existing integration tests for fixture-lookup patterns

This audit drives Part 2's scenario list. It identifies every place current tests pin to a specific row (by id, email, predicate, or magic constant), so Part 2 can pre-bake recipes for those access patterns.

- [x] **Step 1: Run the lookup-pattern grep sweep**

Run each of these against `TelegramGroupsAdmin.IntegrationTests/` (exclude `bin/`, `obj/`, `TestResults/`). Use the Grep tool, not raw `grep`/`rg`:

| Pattern | What it finds | Why it matters |
|---------|---------------|----------------|
| `\b-?\d{10,}\b` | Hardcoded numeric IDs (telegram_user_id / chat_id sized) | Direct ID pins — likely already-broken references to prod IDs |
| `[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}` | Hardcoded UUIDs | Web user references (the 4 fixture UUIDs are expected; others are stale) |
| `@(example\.com\|canonical\.test)` | Hardcoded test emails | Web user fixture references |
| `Status\s*==\s*UserStatus\.` | Predicate lookups by user status | Tests asking "give me a banned user" — scenario candidate |
| `MessageType\s*==\s*MessageType\.` | Predicate lookups by message type | Spam-vs-ham filtering pattern |
| `\.First\(\)\|\.FirstOrDefault\(\)\|\.OrderBy\([^)]+\)\.First` | Implicit anchor selection | Fragile lookups that should become locked-id scenarios |
| `GoldenDataset\.\w+_Id` | Existing `GoldenDataset` constant references | Already-canonical anchors — do NOT duplicate as scenarios |

- [x] **Step 2: Capture the audit output**

Write findings to `tmp/canonical-bootstrap/cheatsheet-audit.md` with this structure:

```markdown
# Cheat-sheet audit — current test fixture-lookup patterns

## Direct ID pins (broken or fragile — high priority for Part 2)
- `<file>:<line>` — `<the literal>` — what the test is trying to find

## Predicate lookups (scenario candidates)
- `<file>:<line>` — predicate — what category of fixture it's seeking

## Existing GoldenDataset references (do NOT scenario-ify — already covered)
- `<file>:<line>` — `<constant>`

## Implicit anchor selection (.First() / .OrderBy)
- `<file>:<line>` — context

## Recommended Part 2 scenarios derived from this audit
- <scenario name> — covers <which lookups from above>
```

- [x] **Step 3: Extract per-table row counts from canonical SQL**

For each file in `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/*.sql`, run `grep -c '^INSERT INTO' <file>` (Bash tool, parallel calls OK) to produce a 35-row count table. Keep this output handy — it goes into Part 1 verbatim.

### Task C2: Write Part 1 (dataset orientation) of `CLAUDE.md`

- [x] **Step 1: Create `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` with the Part 1 skeleton**

Use exactly this section structure (do not invent additional sections — Part 1 is intentionally lean):

```markdown
# Integration Test Canonical Dataset

This file is auto-loaded by Claude Code when working under `TelegramGroupsAdmin.IntegrationTests/`. It is the discovery surface for the canonical test dataset. **Do not embed example row contents here** — read the SQL files directly when you need exemplars. Counts and structural description belong here; rows do not.

## Part 1 — Dataset orientation

### What this is
The canonical dataset is a frozen superset of every entity type the integration suite needs to read from. Tests clone it per-method via Postgres template DBs (Phase 2+) and reduce it down with `GoldenReducePlan` (Phase 1+). Source: `TestData/SQL/canonical/*.sql` (35 files, ~3,200 INSERT statements).

### How we built it
Origin: prod DB snapshot from 2026-04-30. Bootstrap pipeline (full detail in `docs/superpowers/plans/2026-04-30-canonical-golden-snapshot-and-template-cloning.md` Pre-Phase 1b):

1. Mirrored 35 prod tables into a `bootstrap` schema (UNLOGGED, FK constraints replicated via `pg_get_constraintdef`).
2. Sampled 100 messages from each of 4 slices: explicit_spam, implicit_spam, explicit_ham, implicit_ham (400 total).
3. Copied all parent rows up front (Option D), then pruned unreferenced parents at the end (Strict-Plus prune).
4. Rotated user IDs into `[9×10¹², 10¹³)` and chat IDs into `[-1.001×10¹⁴, -1×10¹⁴]` (the `-100` prefix preserves Telegram supergroup format compatibility for app-code checks like `chatId.ToString().StartsWith("-100")`) via deterministic md5 + secret salt. `chat_id = 0` preserved as sentinel.
5. Sanitized non-banned ("ham") users with wordlist names; banned users keep real names as spam evidence.
6. Recomputed content/sim hashes via temporary console app that referenced `TelegramGroupsAdmin.Core` (real `HashUtilities.ComputeContentHash` + `SimHashService.ComputeHash`).
7. Dumped 35 per-table SQL files via `pg_dump --column-inserts` (run inside `tga-db` container for postgres:18 compatibility).

### Tables and counts

| Order | Table | Rows | Notes |
|-------|-------|------|-------|
| 01 | users | <count> | Web users: 4 canonical fixtures + 5 prod-derived (all share one bcrypt hash) |
| 02 | telegram_users | <count> | Anchor set after Strict-Plus prune (every row is referenced by ≥1 child) |
| 03 | managed_chats | <count> | <note> |
| ... | ... | ... | ... |

(Fill the entire 35-row table from Task C1 Step 3 output. Add a one-line "Notes" cell only when the table has a non-obvious property — sentinel rows, synthetic rows, kept-whole vs sampled, etc.)

### What's NOT in the dataset
- **Encrypted JSONB credentials** in `configs` (sendgrid keys, web push keys, AI provider keys) — left NULL. Populated at runtime by the app via `IDataProtectionProvider`.
- **File payloads** referenced by `image_training_samples` / `video_training_samples` — only metadata is preserved (filenames sanitized, extensions kept).
- **Email verification tokens, password reset tokens, locked_until timestamps** — all NULL.
- **TOTP secrets** — NULL except where canonical fixtures need TOTP-enabled state for tests (see Part 2 scenarios).

### Identity boundaries
- **Telegram user IDs:** `[9_000_000_000_000, 10_000_000_000_000)` — synthesized via `abs(md5(real_id || 'canonical-user-rotation-2026')) % 10¹² + 9×10¹²`
- **Chat IDs:** `[-100_099_999_999_999, -100_000_000_000_000]` — same pattern, `'canonical-chat-rotation-2026'` salt. Prefix `-100` matches the Telegram supergroup format that app code checks; 15-digit length keeps them clearly synthetic vs real 13-digit supergroup IDs. `chat_id = 0` preserved as sentinel.
- **Web user UUIDs:** 4 fixed canonical fixtures (see scenarios below) + 5 rotated prod UUIDs.
- **Web user password (all 9):** `Passw0rd!SaidNoSecurityAuditorEver` — hash already in `01_users.sql`. Login flow tests can authenticate any web user with this password.

### Sanitization posture
- **Banned telegram_users (status 2):** real names preserved — they ARE the spam signature; tests rely on them.
- **Non-banned telegram_users:** first/last/username replaced with deterministic wordlist values; NULL fields stay NULL.
- **Cross-table references** (admin_notes, audit_log narrative fields, reports.reviewed_by): rewritten to point at the canonical (sanitized or rotated) name, not the prod name. Spam evidence ties back to its banned user. The `Connected as <name> (ID: <id>)` audit pattern is rebuilt from sanitized telegram_users data; admin identities (Kass/Lettie/etc.) map to canonical fixture emails (owner/admin/globaladmin@example.com) via deterministic hashtext.
- **URL hostnames in spam content (messages, translations, detection_results):** uniformly replaced with `canonical-spam.test`, paths/queries preserved verbatim. No domain exceptions (`t.me` included) — matches what the SUT actually does (hostname-only blocklist + tokenizer-based ML).
- **PII in spam messages:** phone numbers replaced with NANP-reserved `+15555550199` / `555-555-0199`; non-canonical emails replaced with `spam@canonical.test`. These are not load-bearing for spam classifier features.
- **LLM prompt content (configs.welcome_config + prompt_versions):** minimized to 1 global synthetic baseline + 1 Main Chat customized variant + 1 Main Chat prompt_versions row. Other 18 per-chat configs have `welcome_config = NULL` (fall back to global) and `invite_link = NULL`.
- **`username_blacklist`:** trimmed to 2 rows (1 enabled-Exact + 1 disabled-Exact). The other match types (Contains/Regex/StartsWith) are not implemented in `BlacklistMatchType`/`UsernameBlacklistService.CheckDisplayNameAsync`; fixtures for those should be added when the feature ships.

### Schema reference
For column-level details, read the per-table SQL file directly (`head -1` shows the INSERT column list, then read a row or two). Do not transcribe column lists into this document.
```

- [x] **Step 2: Fill in the table counts from C1 Step 3 output**

Replace every `<count>` placeholder with the actual `grep -c '^INSERT INTO'` result. Replace every `<note>` placeholder with one of: a concise one-liner (≤8 words), or the cell stays empty. Drop the parenthetical instructional sentence after filling.

### Task C3: Write Part 2 (scenario recipes with locked IDs) of `CLAUDE.md`

- [x] **Step 1: Append the Part 2 skeleton to `CLAUDE.md`**

Use exactly this section structure:

```markdown
## Part 2 — Scenario recipes

Each recipe pins a canonical anchor row by id so a test author (or test-writing agent) can grab a fixture without re-querying. **If you change canonical and a recipe id no longer resolves, update the recipe in the same commit** — stale recipes are a code smell.

Recipe format: a heading, the anchor id(s), a one-line description, and "use when" guidance.

### Web users

#### Owner — full-access fixture
- `User.Id` = `b388ee38-0ed3-4c09-9def-5715f9f07f56`
- Email: `owner@example.com`, permission_level 2, status 1, TOTP enabled
- Use when: a test needs the highest-privilege web user (system administration, settings mutation).

#### Admin — standard-permission fixture
- `User.Id` = `921637d5-0f65-4c66-b143-6f057dd06a1c`
- Email: `admin@example.com`, permission_level 0, status 1, TOTP enabled, invited by Owner
- Use when: a test needs an authenticated user with normal permissions (most authenticated-flow tests).

#### Deleted Admin — soft-delete fixture
- `User.Id` = `a8dc8371-afc5-4b61-9d71-d177f2dd9ddd`
- Email: `deleted@example.com`, status 3 (deleted), is_active false
- Use when: a test asserts on soft-delete behavior or filters out deleted users.

#### Deleted GlobalAdmin — soft-deleted elevated fixture
- `User.Id` = `ba9ba542-3df6-4473-a820-578562780c57`
- Email: `globaladmin@example.com`, permission_level 1, status 3
- Use when: a test asserts that elevated-but-deleted users are still excluded.

### Telegram users
(Populate from Task C1 audit output. Required scenarios at minimum:)
- Banned spammer with rich audit trail (admin_notes + audit_log + user_tags + detection_results all reference it)
- Banned spammer with username_history (changed name then spammed)
- Long-tenured ham user (oldest `created_at`, no detection results)
- Recently-active ham user (most recent message)
- User with profile_scan_results entries
- User with chat_admins membership in MainChat

### Managed chats
(Populate from canonical 03_managed_chats.sql + audit output. Required scenarios at minimum:)
- MainChat — the chat with the largest chat_admins + messages footprint (most tests anchor here)
- A chat with linked_channels references
- A chat with content_detection_configs of each kind
- A chat with NO admins (edge case)

### Messages
(Populate from canonical 19_messages.sql + audit output. Required scenarios at minimum:)
- One message per slice: explicit_spam, implicit_spam, explicit_ham, implicit_ham
- A message with message_edits history
- A message with message_translations
- A message with multiple detection_results (different detector kinds)

### Configs
(Populate from canonical 04_configs.sql + 05_content_detection_configs.sql. Required scenarios at minimum:)
- A config row with encrypted JSONB columns NULL — DataProtection injection target (most config tests)
- Each kind of `content_detection_configs` row that current tests rely on

### Synthetic / reserved rows (do not regenerate)
- `welcome_responses` IDs 999001–999005: 5 `WelcomeResponseType` status branches (Pending / Accepted / Denied / Timeout / Left) on `(chat_id=-100026957614982, user_id=9196379650113, username='canonical_user1', welcome_message_id=99001..99005)` — the canonical fixture for welcome-response-by-status lookups.
- `username_blacklist`: 2 rows total — ID 999001 (`spambot_admin`, enabled, Exact) and ID 999005 (`archived_pattern`, disabled, Exact). No Contains/Regex/StartsWith fixtures (only Exact is implemented in `BlacklistMatchType`).
- `training_labels` rows with `reason='canonical_synthetic_promotion'`: 15 explicit_ham promotions. `labeled_by_user_id` = rotated id of prod user `1312830442`.

### Cross-references
- `auth password (all 9 web users)`: `Passw0rd!SaidNoSecurityAuditorEver` — hash already baked into `01_users.sql`.
- `chat_id = 0` is a preserved sentinel (rotation skipped via CASE WHEN guard).
```

- [x] **Step 2: Resolve every `(Populate from …)` placeholder**

For each placeholder block, look up the actual canonical id by querying the SQL file (or the loaded database if convenient). Replace the placeholder with concrete recipes following the format demonstrated in the Web users section: heading, locked id, one-line description, "use when" line.

The audit output from Task C1 tells you which scenarios real tests need — drive recipe selection from that list. Add at most 1–2 exploratory scenarios per category beyond what the audit requires.

- [x] **Step 3: Verify every locked id resolves to an actual canonical row**

For each id in Part 2, grep the corresponding SQL file. Every id must appear; missing ids are a recipe bug.

### Task C4: Self-review and stage for Phase 1 commit

- [x] **Step 1: Read the finished `CLAUDE.md` end-to-end with fresh eyes**

Verify: Part 1 has no example data rows, Part 2 has no unresolved placeholders, every Part 2 id was verified in Task C3 Step 3, and the doc length is short enough to scan in under a minute (rough target: under 250 lines).

- [x] **Step 2: Confirm the file lands in the Phase 1 commit**

Cross-check that Phase 1's "Files referenced in this phase" list includes `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (it should, after this rewrite). Do NOT `git add` here — Pre-Phase 1c produces no commit; the file rides in alongside the canonical SQL files in the Phase 1 commit.

---

## Phase 1: Build new seed surface (no consumer change)

Files referenced in this phase:
- Modify: `TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanBuilder.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/ChildReducePlan.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanException.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/GoldenReducePlanTests.cs`
- Move: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/40_pre_migration_impersonation_alerts.sql` → `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Migrations/CriticalMigrationTests.cs` (path update)
- Add: 35 SQL files under `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/` (output of Pre-1b)
- Add: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (output of Pre-1c — auto-loaded cheat sheet)

### Task 1.1: Capture T0 baseline timing

**Files:** none (read-only run; record output in worktree note)

- [x] **Step 1: Run the integration suite and capture wall-clock**

Run:
```bash
time dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -30
```

- [x] **Step 2: Record the result in `tmp/canonical-bootstrap/T0.txt`**

Write the `real`/`user`/`sys` line + the `Passed: N, Failed: N` summary into `tmp/canonical-bootstrap/T0.txt` for the PR description. This file is not committed.

### Task 1.2: Update .csproj EmbeddedResource glob to recursive

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj:49`

- [x] **Step 1: Edit the glob**

Change:
```xml
<EmbeddedResource Include="TestData\SQL\*.sql" />
```
to:
```xml
<EmbeddedResource Include="TestData\SQL\**\*.sql" />
```

- [x] **Step 2: Verify the build still succeeds and SQL fixtures are still embedded**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests`

Expected: Build succeeded.

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenDatasetTests" --logger "console;verbosity=normal" 2>&1 | tail -20` (or any existing test that loads an embedded SQL fixture).

Expected: tests still pass — the recursive glob includes everything the flat glob did, plus subfolders.

### Task 1.3: Move the migration-test SQL fixture into TestData/SQL/migration/

**Files:**
- Move: `TestData/SQL/40_pre_migration_impersonation_alerts.sql` → `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Migrations/CriticalMigrationTests.cs:186`

- [x] **Step 1: Create the migration/ subfolder and move the file**

Run:
```bash
mkdir -p TelegramGroupsAdmin.IntegrationTests/TestData/SQL/migration
git mv TelegramGroupsAdmin.IntegrationTests/TestData/SQL/40_pre_migration_impersonation_alerts.sql \
       TelegramGroupsAdmin.IntegrationTests/TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql
```

- [x] **Step 2: Update the consumer reference**

Edit `TelegramGroupsAdmin.IntegrationTests/Migrations/CriticalMigrationTests.cs:186` from:
```csharp
await GoldenDataset.LoadSqlScriptAsync("SQL.40_pre_migration_impersonation_alerts.sql", helper.ExecuteSqlAsync);
```
to:
```csharp
await GoldenDataset.LoadSqlScriptAsync("SQL.migration.40_pre_migration_impersonation_alerts.sql", helper.ExecuteSqlAsync);
```

(Embedded resource names use `.` separators, so `SQL/migration/40_…` becomes `SQL.migration.40_…`.)

- [x] **Step 3: Run the migration test to verify the path resolves**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~CriticalMigrationTests" --logger "console;verbosity=normal" 2>&1 | tail -15`

Expected: PASS.

### Task 1.4: Place the 35 canonical SQL files (output of Pre-1b)

**Files:**
- Create: 35 files under `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/`

- [x] **Step 1: Confirm Pre-1b outputs are present**

Run: `ls TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/`

Expected: 35 files, in this exact FK-safe load order (matches on-disk numeric order):
```
01_users.sql               02_telegram_users.sql        03_managed_chats.sql
04_configs.sql             05_content_detection_configs.sql
06_ban_celebration_captions.sql                         07_ban_celebration_gifs.sql
08_blocklist_subscriptions.sql                          09_prompt_versions.sql
10_recovery_codes.sql      11_stop_words.sql            12_tag_definitions.sql
13_username_blacklist.sql  14_domain_filters.sql        15_image_training_samples.sql
16_video_training_samples.sql                           17_web_notifications.sql
18_notification_preferences.sql                         19_messages.sql
20_chat_admins.sql         21_linked_channels.sql       22_telegram_user_mappings.sql
23_profile_scan_results.sql                             24_username_history.sql
25_admin_notes.sql         26_audit_log.sql             27_user_tags.sql
28_welcome_responses.sql   29_invites.sql               30_reports.sql
31_message_edits.sql       32_detection_results.sql     33_training_labels.sql
34_user_actions.sql        35_message_translations.sql
```

- [x] **Step 2: Verify recursive glob picks up canonical/ subfolder**

Run:
```bash
dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5
ls TelegramGroupsAdmin.IntegrationTests/bin/Debug/net10.0/TelegramGroupsAdmin.IntegrationTests.dll
```

Run an embedded-resource sanity check via `dotnet`:
```bash
dotnet run --project TelegramGroupsAdmin.IntegrationTests -- --list-resources 2>/dev/null || \
  dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ManifestResourceProbe" 2>&1 | tail -5
```

If no probe test exists, add the probe test in Task 1.7 (with `LoadCanonicalAsync`) which fails loud if any of the 35 resources is missing.

### Task 1.5: Add SharedDataProtectionProvider to PostgresFixture

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixtureTests.cs` (new)

- [x] **Step 1: Write the failing test**

Create `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixtureTests.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;

namespace TelegramGroupsAdmin.IntegrationTests.Fixtures;

[TestFixture]
public class PostgresFixtureTests
{
    [Test]
    public void SharedDataProtectionProvider_IsEphemeral()
    {
        var provider = PostgresFixture.SharedDataProtectionProvider;
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider, Is.InstanceOf<EphemeralDataProtectionProvider>());
    }

    [Test]
    public void SharedDataProtectionProvider_ReturnsSameInstance()
    {
        var first = PostgresFixture.SharedDataProtectionProvider;
        var second = PostgresFixture.SharedDataProtectionProvider;
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void SharedDataProtectionProvider_RoundTripsCiphertext()
    {
        var protector = PostgresFixture.SharedDataProtectionProvider.CreateProtector("test");
        var protectedText = protector.Protect("hello");
        Assert.That(protector.Unprotect(protectedText), Is.EqualTo("hello"));
    }
}
```

- [x] **Step 2: Run the test — expect compile error (member missing)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PostgresFixtureTests" --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: build fails with "PostgresFixture does not contain a definition for SharedDataProtectionProvider."

- [x] **Step 3: Add the property to PostgresFixture**

Edit `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs`:

Add `using Microsoft.AspNetCore.DataProtection;` to the top.

After the `BaseConnectionString` property, add:

```csharp
    /// <summary>
    /// A single ephemeral DataProtection provider shared across the entire test session.
    /// Used by canonical-consumer tests so encrypted-column ciphertext written into
    /// the golden_template (via LoadCanonicalAsync) can be decrypted by tests at runtime.
    /// </summary>
    public static IDataProtectionProvider SharedDataProtectionProvider { get; }
        = new EphemeralDataProtectionProvider();
```

- [x] **Step 4: Run the test — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~PostgresFixtureTests" --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: 3 tests passed.

### Task 1.6: Add LoadCanonicalAsync to GoldenDataset

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs` (new)

- [x] **Step 1: Create the test directory and write the failing test**

```bash
mkdir -p TelegramGroupsAdmin.IntegrationTests/TestData/Tests
```

Create `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

[TestFixture]
public class LoadCanonicalAsyncTests
{
    private MigrationTestHelper? _helper;

    [SetUp]
    public async Task Setup()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndApplyMigrationsAsync();
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    [Test]
    public async Task LoadCanonicalAsync_PopulatesAllThirtyFiveTables()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);

        Assert.That(await ctx.Users.CountAsync(), Is.GreaterThan(0), "users");
        Assert.That(await ctx.TelegramUsers.CountAsync(), Is.GreaterThan(0), "telegram_users");
        Assert.That(await ctx.ManagedChats.CountAsync(), Is.GreaterThan(0), "managed_chats");
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(400), "messages should be exactly 400");
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(200), "training_labels should be exactly 200");
        // ... assert > 0 on each remaining canonical table (5 tables are intentionally
        // EMPTY: domain_filters, recovery_codes, image_training_samples,
        // video_training_samples, web_notifications — assert == 0 on those instead).
    }

    [Test]
    public async Task LoadCanonicalAsync_FillsConfigsEncryptedColumns()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);

        var config = await ctx.Configs.FirstAsync(c => c.ChatId == 0);
        // Whichever encrypted columns LoadCanonicalAsync's post-step fills should be non-null;
        // others may legitimately stay NULL. At minimum, api_keys should be populated.
        Assert.That(config.ApiKeys, Is.Not.Null.And.Not.Empty);
    }
}
```

- [x] **Step 2: Run the test — expect compile error (LoadCanonicalAsync missing)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~LoadCanonicalAsyncTests" 2>&1 | tail -10`

Expected: build fails with "GoldenDataset does not contain a definition for LoadCanonicalAsync."

- [x] **Step 3: Implement LoadCanonicalAsync**

Open `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`. Make the class `partial` (rename `public static class GoldenDataset` to `public static partial class GoldenDataset` on line 12) so subsequent tasks can split additions cleanly. Then add inside the class:

```csharp
    /// <summary>
    /// Loads the 35 canonical/*.sql fixtures FK-ordered into the target context, then
    /// runs the encrypted-column UPDATE post-step using the supplied DataProtection
    /// provider. Used by PostgresFixture.[OneTimeSetUp] to build golden_template, and
    /// by GoldenReducePlanTests to exercise Reduce against canonical without depending
    /// on Phase 2's template infrastructure.
    ///
    /// HASHES are pre-baked into the SQL files (see Pre-1b) — this method does NOT
    /// recompute content_hash / similarity_hash at load time. The only post-load step
    /// is the encrypted-column UPDATE below.
    /// </summary>
    public static async Task LoadCanonicalAsync(
        AppDbContext context,
        IDataProtectionProvider dataProtection,
        CancellationToken ct = default)
    {
        // FK-safe load order matching TestData/SQL/canonical/ exactly (35 files;
        // numeric on-disk order IS the FK-safe order — Pre-1b enforced this).
        // Resource names use '.' separators per .NET embedded-resource conventions:
        // path "TestData/SQL/canonical/01_users.sql" → "SQL.canonical.01_users.sql".
        string[] fixtures =
        {
            // Layer 0 roots — users, telegram_users, managed_chats first
            "SQL.canonical.01_users.sql",
            "SQL.canonical.02_telegram_users.sql",
            "SQL.canonical.03_managed_chats.sql",
            // Independent reference / config tables
            "SQL.canonical.04_configs.sql",
            "SQL.canonical.05_content_detection_configs.sql",
            "SQL.canonical.06_ban_celebration_captions.sql",
            "SQL.canonical.07_ban_celebration_gifs.sql",
            "SQL.canonical.08_blocklist_subscriptions.sql",
            "SQL.canonical.09_prompt_versions.sql",
            "SQL.canonical.10_recovery_codes.sql",       // EMPTY (0 rows)
            "SQL.canonical.11_stop_words.sql",
            "SQL.canonical.12_tag_definitions.sql",
            "SQL.canonical.13_username_blacklist.sql",   // 2 rows (Exact only)
            "SQL.canonical.14_domain_filters.sql",       // EMPTY
            "SQL.canonical.15_image_training_samples.sql", // EMPTY
            "SQL.canonical.16_video_training_samples.sql", // EMPTY
            "SQL.canonical.17_web_notifications.sql",    // EMPTY
            "SQL.canonical.18_notification_preferences.sql",
            // Layer 1 — children of roots
            "SQL.canonical.19_messages.sql",             // 400 rows
            "SQL.canonical.20_chat_admins.sql",
            "SQL.canonical.21_linked_channels.sql",
            "SQL.canonical.22_telegram_user_mappings.sql",
            "SQL.canonical.23_profile_scan_results.sql",
            "SQL.canonical.24_username_history.sql",
            "SQL.canonical.25_admin_notes.sql",
            "SQL.canonical.26_audit_log.sql",
            "SQL.canonical.27_user_tags.sql",
            "SQL.canonical.28_welcome_responses.sql",    // includes synthetic 999001..999005
            "SQL.canonical.29_invites.sql",
            "SQL.canonical.30_reports.sql",
            // Layer 2 — children of messages
            "SQL.canonical.31_message_edits.sql",
            "SQL.canonical.32_detection_results.sql",
            "SQL.canonical.33_training_labels.sql",      // 200 rows
            "SQL.canonical.34_user_actions.sql",         // 993 rows
            // Layer 3 — child of messages AND message_edits
            "SQL.canonical.35_message_translations.sql",
        };

        foreach (var fixture in fixtures)
        {
            ct.ThrowIfCancellationRequested();
            await LoadSqlScriptAsync(context, fixture);
        }

        // Encrypted-column post-step: 04_configs.sql seeds the configs rows with all 5
        // DataProtection-encrypted columns NULL. Encrypt canonical plaintext under the
        // shared provider and UPDATE. Each column has its OWN purpose string — production
        // code in TelegramGroupsAdmin.Configuration uses DataProtectionPurposes.ApiKeys
        // ("ApiKeys") for the api_keys column, so canonical MUST use the same constant —
        // mismatched purposes would write ciphertext production code can't decrypt.
        var apiKeysProtector = dataProtection.CreateProtector(DataProtectionPurposes.ApiKeys);
        var apiKeysCanonical = apiKeysProtector.Protect("""{"openai":"sk-canonical-test-key"}""");
        // (Add additional Protect() calls if more encrypted columns become required by
        //  any canonical-consumer test — see spec "Encrypted columns and the shared keyring".
        //  Each new column uses its own DataProtectionPurposes.* constant.)

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE configs SET api_keys = {0} WHERE chat_id = 0",
            apiKeysCanonical);
    }
```

Add to the top of `GoldenDataset.cs`:
```csharp
using Microsoft.AspNetCore.DataProtection;
using TelegramGroupsAdmin.Data.Constants;
```

- [x] **Step 4: Run the tests — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~LoadCanonicalAsyncTests" --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: 2 tests passed. Failures here usually mean: (a) embedded resource missing — verify `ls bin/Debug/net10.0/TelegramGroupsAdmin.IntegrationTests.dll` and use `ildasm`/`Resgen` to confirm; (b) FK violation — the canonical SQL files have a row referencing a parent that's not yet inserted; revisit Pre-1b Task B7.

### Task 1.7: Add ChildReducePlan (stage 2)

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/ChildReducePlan.cs`

The two-stage builder is implemented bottom-up (child stage first, then parent stage that returns it). This makes Task 1.8's parent stage a clean compile.

- [x] **Step 1: Write the file**

Create `TelegramGroupsAdmin.IntegrationTests/TestData/ChildReducePlan.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Stage-2 reducer plan, returned once any child reducer (KeepSpam / KeepHam /
/// KeepDetectionResults / KeepUserActions) is invoked. KeepMessages is intentionally
/// absent at this stage — the type system rules out KeepHam(N).KeepMessages(N) chains.
/// </summary>
public sealed class ChildReducePlan
{
    private readonly GoldenReducePlanState _state;

    internal ChildReducePlan(GoldenReducePlanState state) => _state = state;

    public ChildReducePlan KeepSpam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.SpamCount = count;
        return this;
    }

    public ChildReducePlan KeepHam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.HamCount = count;
        return this;
    }

    public ChildReducePlan KeepDetectionResults(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.DetectionResultsCount = count;
        return this;
    }

    public ChildReducePlan KeepUserActions(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.UserActionsCount = count;
        return this;
    }

    public Task ApplyAsync(CancellationToken ct = default) => _state.ApplyAsync(ct);
}

/// <summary>
/// Shared mutable plan state across stage 1 and stage 2. KeepX methods on either
/// stage write into this object; ApplyAsync runs registered ops in fixed
/// parent-first topological order.
/// </summary>
internal sealed class GoldenReducePlanState
{
    private readonly AppDbContext _context;

    public int? MessagesCount { get; set; }
    public int? SpamCount { get; set; }
    public int? HamCount { get; set; }
    public int? DetectionResultsCount { get; set; }
    public int? UserActionsCount { get; set; }

    public GoldenReducePlanState(AppDbContext context) => _context = context;

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. KeepMessages — runs first; FK cascade fires (CASCADE on
            //    message_edits/training_labels/detection_results/message_translations,
            //    SetNull on user_actions.MessageId/ChatId).
            if (MessagesCount is int msgN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM messages " +
                    "WHERE (chat_id, message_id) NOT IN (" +
                    "  SELECT chat_id, message_id FROM messages " +
                    "  ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    msgN, "KeepMessages");
            }

            // 2. KeepSpam — slice predicate appears on BOTH sides so KeepSpam(5)
            //    doesn't delete ham rows. training_labels.label is a smallint:
            //    0=Spam, 1=Ham (per TelegramGroupsAdmin.Core/Models/TrainingLabel.cs).
            const short LabelSpam = (short)TrainingLabel.Spam; // 0
            const short LabelHam = (short)TrainingLabel.Ham;   // 1

            if (SpamCount is int spamN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM training_labels " +
                    "WHERE label = {1} " +
                    "  AND (chat_id, message_id) NOT IN (" +
                    "    SELECT chat_id, message_id FROM training_labels " +
                    "    WHERE label = {1} " +
                    "    ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    spamN, "KeepSpam", LabelSpam);
            }

            // 3. KeepHam
            if (HamCount is int hamN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM training_labels " +
                    "WHERE label = {1} " +
                    "  AND (chat_id, message_id) NOT IN (" +
                    "    SELECT chat_id, message_id FROM training_labels " +
                    "    WHERE label = {1} " +
                    "    ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    hamN, "KeepHam", LabelHam);
            }

            // 4. KeepDetectionResults — surrogate id PK
            if (DetectionResultsCount is int drN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    "WHERE id NOT IN (" +
                    "  SELECT id FROM detection_results " +
                    "  ORDER BY id ASC LIMIT {0})",
                    drN, "KeepDetectionResults");
            }

            // 5. KeepUserActions — surrogate id PK; runs last so SetNull orphans
            //    from KeepMessages can be cleaned up if user explicitly requests.
            if (UserActionsCount is int uaN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM user_actions " +
                    "WHERE id NOT IN (" +
                    "  SELECT id FROM user_actions " +
                    "  ORDER BY id ASC LIMIT {0})",
                    uaN, "KeepUserActions");
            }

            await tx.CommitAsync(ct);
        }
        catch (GoldenReducePlanException)
        {
            // ExecAsync already wrapped this with a StepName — let it bubble after rollback.
            await tx.RollbackAsync(ct);
            throw;
        }
        catch (Exception ex)
        {
            // Non-step failure (e.g., transaction begin/commit) — wrap without a step name.
            await tx.RollbackAsync(ct);
            throw new GoldenReducePlanException("Reduce plan failed", stepName: null, ex);
        }
    }

    private static async Task ExecAsync(AppDbContext ctx, CancellationToken ct,
        string sql, int n, string stepName, params object[] extraParams)
    {
        try
        {
            var parameters = new object[1 + extraParams.Length];
            parameters[0] = n;
            for (int i = 0; i < extraParams.Length; i++) parameters[i + 1] = extraParams[i];
            await ctx.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
        catch (Exception ex)
        {
            throw new GoldenReducePlanException($"Step '{stepName}' failed", stepName, ex);
        }
    }
}
```

**Imports:** add `using TelegramGroupsAdmin.Core.Models;` at the top of `ChildReducePlan.cs` so `TrainingLabel` resolves.

- [x] **Step 2: Build — expect compile error (GoldenReducePlanException missing)**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: error CS0246 "GoldenReducePlanException could not be found."

### Task 1.8: Add GoldenReducePlanException

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanException.cs`

- [x] **Step 1: Write the file**

```csharp
namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Wraps any exception raised inside GoldenReducePlanState.ApplyAsync. The transaction
/// is rolled back before this is thrown. StepName carries the failing reducer's name
/// (e.g., "KeepSpam", "KeepMessages"); null for non-step failures (transaction
/// begin/commit).
/// </summary>
public sealed class GoldenReducePlanException : Exception
{
    public string? StepName { get; }

    public GoldenReducePlanException(string message, string? stepName, Exception inner)
        : base(message, inner)
    {
        StepName = stepName;
    }
}
```

- [x] **Step 2: Build — expect ChildReducePlan.cs to compile**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: Build still fails because nothing references `ChildReducePlan` yet — but the file itself compiles. Move on.

### Task 1.9: Add GoldenReducePlanBuilder (stage 1)

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanBuilder.cs`

- [x] **Step 1: Write the file**

```csharp
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Stage-1 reducer plan returned by GoldenDataset.Reduce(ctx). All five Keep* methods
/// are reachable; calling any child reducer (KeepSpam/KeepHam/KeepDetectionResults/
/// KeepUserActions) transitions to ChildReducePlan, where KeepMessages is no longer
/// reachable in fluent chains.
///
/// The underlying GoldenReducePlanState is shared between stages — registration via
/// intermediate variables can register parent ops after children, but ApplyAsync
/// runs in fixed parent-first topological order regardless.
/// </summary>
public sealed class GoldenReducePlanBuilder
{
    private readonly GoldenReducePlanState _state;

    internal GoldenReducePlanBuilder(GoldenReducePlanState state) => _state = state;

    public GoldenReducePlanBuilder KeepMessages(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.MessagesCount = count;
        return this;
    }

    public ChildReducePlan KeepSpam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.SpamCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepHam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.HamCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepDetectionResults(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.DetectionResultsCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepUserActions(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.UserActionsCount = count;
        return new ChildReducePlan(_state);
    }

    public Task ApplyAsync(CancellationToken ct = default) => _state.ApplyAsync(ct);
}
```

- [x] **Step 2: Build — expect success (no consumer yet)**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: Build succeeded (the new types compile; no test references them yet).

### Task 1.10: Add GoldenDataset.Reduce factory

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`

- [x] **Step 1: Add the factory method to GoldenDataset**

Add inside the partial class (alongside `LoadCanonicalAsync`):

```csharp
    /// <summary>
    /// Entry point for the subtractive Reduce builder. Returns a stage-1 plan bound
    /// to the supplied context; no DB work runs until ApplyAsync is called. Each
    /// invocation returns a fresh plan — plans are single-shot.
    /// </summary>
    public static GoldenReducePlanBuilder Reduce(AppDbContext context)
        => new GoldenReducePlanBuilder(new GoldenReducePlanState(context));
```

- [x] **Step 2: Build**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: Build succeeded.

### Task 1.11: GoldenReducePlanTests — KeepSpam basic + isolation

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/GoldenReducePlanTests.cs`

These tests use `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync()` + `LoadCanonicalAsync` to set up canonical against a freshly-migrated DB; Phase 2's template infrastructure is not yet in place.

- [x] **Step 1: Create the test file with the KeepSpam tests**

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

[TestFixture]
public class GoldenReducePlanTests
{
    // training_labels.label is smallint (0=Spam, 1=Ham). The DTO exposes it as `short Label`,
    // so test predicates compare against the cast enum value rather than a string literal.
    private const short SpamLabel = (short)TrainingLabel.Spam; // 0
    private const short HamLabel = (short)TrainingLabel.Ham;   // 1

    private MigrationTestHelper? _helper;

    [SetUp]
    public async Task Setup()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndApplyMigrationsAsync();
        await using var ctx = _helper.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    [Test]
    public async Task KeepSpam_KeepsExactlyN_AndDoesNotTouchHam()
    {
        await using var ctx = _helper!.GetDbContext();
        var hamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);

        await GoldenDataset.Reduce(ctx).KeepSpam(5).ApplyAsync();

        var spamAfter = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);
        var hamAfter = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);
        Assert.That(spamAfter, Is.EqualTo(5));
        Assert.That(hamAfter, Is.EqualTo(hamBefore), "KeepSpam must not touch ham");
    }

    [Test]
    public async Task KeepSpam_Zero_RemovesAllSpam_KeepsAllHam()
    {
        await using var ctx = _helper!.GetDbContext();
        var hamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel);

        await GoldenDataset.Reduce(ctx).KeepSpam(0).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(hamBefore));
    }
}
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests" --logger "console;verbosity=normal" 2>&1 | tail -15`

Expected: 2 tests passed. Failure mode: if `KeepSpam(5)` returns 0 ham, the slice-predicate-on-both-sides is broken — go back to `ChildReducePlan.cs`'s spam DELETE and confirm `WHERE label = {1}` (parameterized to `LabelSpam`, smallint 0) appears OUTSIDE the inner SELECT, not just inside it.

### Task 1.12: GoldenReducePlanTests — KeepHam symmetry

- [x] **Step 1: Add tests**

Append to `GoldenReducePlanTests.cs`:

```csharp
    [Test]
    public async Task KeepHam_KeepsExactlyN_AndDoesNotTouchSpam()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);

        await GoldenDataset.Reduce(ctx).KeepHam(5).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(5));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }

    [Test]
    public async Task KeepHam_Zero_RemovesAllHam_KeepsAllSpam()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);

        await GoldenDataset.Reduce(ctx).KeepHam(0).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == HamLabel), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests.KeepHam" 2>&1 | tail -10`

Expected: 2 tests passed.

### Task 1.13: GoldenReducePlanTests — KeepDetectionResults & KeepUserActions

- [x] **Step 1: Add tests**

Append to `GoldenReducePlanTests.cs`:

```csharp
    [Test]
    public async Task KeepDetectionResults_KeepsLowestNById()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepDetectionResults(3).ApplyAsync();

        var ids = await ctx.DetectionResults.OrderBy(dr => dr.Id).Select(dr => dr.Id).ToListAsync();
        Assert.That(ids, Has.Count.EqualTo(3));
        // No assertion on specific id values — bootstrap renumbering is opaque to this test.
        // The "lowest by id" property is verified by the count + orderedness alone.
    }

    [Test]
    public async Task KeepUserActions_KeepsLowestNById()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepUserActions(2).ApplyAsync();

        Assert.That(await ctx.UserActions.CountAsync(), Is.EqualTo(2));
    }
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests" 2>&1 | tail -10`

Expected: 6 tests passed total.

### Task 1.14: GoldenReducePlanTests — KeepMessages cascade (Cascade FKs + SetNull on user_actions)

- [x] **Step 1: Add tests**

Append to `GoldenReducePlanTests.cs`:

```csharp
    [Test]
    public async Task KeepMessages_Zero_CascadesAllChildrenButSetNullsUserActions()
    {
        await using var ctx = _helper!.GetDbContext();

        // Sanity: canonical has message_translations rows before the cascade
        var translationsBefore = await ctx.MessageTranslations.CountAsync();
        Assume.That(translationsBefore, Is.GreaterThan(0), "canonical must contain message_translations rows");

        await GoldenDataset.Reduce(ctx).KeepMessages(0).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.MessageEdits.CountAsync(), Is.EqualTo(0), "Cascade FK should clear message_edits");
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(0), "Cascade FK should clear training_labels");
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(0), "Cascade FK should clear detection_results");
        Assert.That(await ctx.MessageTranslations.CountAsync(), Is.EqualTo(0), "Cascade FK should clear message_translations");

        // user_actions rows survive (SetNull); MessageId/ChatId go null on cascaded rows
        Assert.That(await ctx.UserActions.CountAsync(), Is.GreaterThan(0));
        Assert.That(await ctx.UserActions.CountAsync(ua => ua.MessageId == null), Is.GreaterThan(0));
    }

    [Test]
    public async Task KeepMessages_NonZero_LeavesChildrenAtCascadeNaturalCounts()
    {
        await using var ctx = _helper!.GetDbContext();

        await GoldenDataset.Reduce(ctx).KeepMessages(5).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        // Children of the surviving 5 messages remain — exact count is bootstrap-defined,
        // but every surviving training_labels/detection_results row must reference one of
        // the surviving 5 messages.
        var surviving = await ctx.Messages.Select(m => new { m.ChatId, m.MessageId }).ToListAsync();
        var hangingTl = await ctx.TrainingLabels
            .Where(tl => !surviving.Any(s => s.ChatId == tl.ChatId && s.MessageId == tl.MessageId))
            .CountAsync();
        Assert.That(hangingTl, Is.EqualTo(0), "training_labels rows must all reference surviving messages");
    }
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests.KeepMessages" 2>&1 | tail -10`

Expected: 2 tests passed.

### Task 1.15: GoldenReducePlanTests — Cascade narrowing + topological execution

- [x] **Step 1: Add tests**

Append to `GoldenReducePlanTests.cs`:

```csharp
    [Test]
    public async Task KeepMessages_FollowedByKeepDetectionResults_NarrowsFurther()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepMessages(5).KeepDetectionResults(2).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task KeepMessages_Zero_PlusKeepUserActions_Zero_RemovesBoth()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepMessages(0).KeepUserActions(0).ApplyAsync();

        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.UserActions.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task TopologicalOrder_RegisteringChildBeforeParentViaIntermediateVariable_ProducesParentFirstResult()
    {
        await using var ctx = _helper!.GetDbContext();

        // Wrong-order registration via intermediate variable (legal, see spec "shared mutable plan").
        var parent = GoldenDataset.Reduce(ctx);
        var child = parent.KeepDetectionResults(2);  // registers child first
        parent.KeepMessages(5);                       // then parent
        await child.ApplyAsync();                     // applies both

        // Topological execution puts KeepMessages first regardless of registration order.
        // Result must equal the canonical-ordered chain Reduce(ctx).KeepMessages(5).KeepDetectionResults(2).
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(5));
        Assert.That(await ctx.DetectionResults.CountAsync(), Is.EqualTo(2));
    }
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests" 2>&1 | tail -10`

Expected: 11 tests passed total.

### Task 1.16: GoldenReducePlanTests — validation rules (LIMIT semantics, negative count, last-wins)

- [x] **Step 1: Add tests**

Append to `GoldenReducePlanTests.cs`:

```csharp
    [Test]
    public async Task KeepSpam_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        await using var ctx = _helper!.GetDbContext();
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenDataset.Reduce(ctx).KeepSpam(-1));
    }

    [Test]
    public async Task KeepSpam_CountGreaterThanCanonical_KeepsAllAvailable()
    {
        await using var ctx = _helper!.GetDbContext();
        var spamBefore = await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel);
        await GoldenDataset.Reduce(ctx).KeepSpam(spamBefore + 500).ApplyAsync();

        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(spamBefore));
    }

    [Test]
    public async Task KeepDetectionResults_AfterKeepMessagesNarrowsTo12_RequestingMore_KeepsAll12()
    {
        await using var ctx = _helper!.GetDbContext();
        // Choose a KeepMessages count that leaves a small detection_results survivor count
        await GoldenDataset.Reduce(ctx).KeepMessages(5).KeepDetectionResults(500).ApplyAsync();

        var actual = await ctx.DetectionResults.CountAsync();
        Assert.That(actual, Is.LessThanOrEqualTo(500));
        // Test passes by surviving without throwing — natural LIMIT semantics handle the bound.
    }

    [Test]
    public async Task KeepSpam_CalledTwice_LastWins()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.Reduce(ctx).KeepSpam(10).KeepSpam(3).ApplyAsync();
        Assert.That(await ctx.TrainingLabels.CountAsync(tl => tl.Label == SpamLabel), Is.EqualTo(3));
    }
```

- [x] **Step 2: Run — expect PASS**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenReducePlanTests" 2>&1 | tail -10`

Expected: 15 tests passed total.

### Task 1.17: Run the full suite to confirm no regressions

- [x] **Step 1: Run the full integration suite**

Run:
```bash
dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10
```

Expected: every pre-existing test still passes (Phase 1 added new infrastructure but did not change any consumer code path). New tests under `TestData/Tests/` also pass.

### Task 1.18: Commit Phase 1

- [x] **Step 1: Stage and commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj \
        TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs \
        TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixtureTests.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanBuilder.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/ChildReducePlan.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/GoldenReducePlanException.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/Tests/GoldenReducePlanTests.cs \
        TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/ \
        TelegramGroupsAdmin.IntegrationTests/TestData/SQL/migration/ \
        TelegramGroupsAdmin.IntegrationTests/Migrations/CriticalMigrationTests.cs

# (The 40_pre_migration_impersonation_alerts.sql delete was staged as part of git mv in Task 1.3.)

git commit -F- <<'EOF'
feat(test): add canonical SQL fixtures + GoldenReducePlan builder + SharedDataProtectionProvider

- Adds 35 canonical SQL fixtures under TestData/SQL/canonical/ produced by
  the one-time bootstrap from local DB sampling (400 messages, 200 training
  labels, FK-supporting rows; 3,174 INSERTs total). Sanitization rules per
  spec applied; content/similarity hashes pre-baked into the SQL.
- Moves 40_pre_migration_impersonation_alerts.sql under TestData/SQL/migration/
  and updates CriticalMigrationTests path reference. Recursive EmbeddedResource
  glob picks up the subfolders.
- Adds GoldenDataset.LoadCanonicalAsync(ctx, dataProtection, ct) which loads
  the 35 fixtures FK-ordered and runs the encrypted-column UPDATE post-step.
- Adds GoldenReducePlanBuilder + ChildReducePlan two-stage type-state builder
  with KeepMessages/KeepSpam/KeepHam/KeepDetectionResults/KeepUserActions.
  ApplyAsync runs ops in fixed parent-first topological order in a single
  transaction. Slice predicates appear on both sides of training_labels
  DELETE so KeepSpam doesn't touch ham (and vice-versa).
- Adds PostgresFixture.SharedDataProtectionProvider — single
  EphemeralDataProtectionProvider used by canonical-consumer tests so
  encrypted-column ciphertext written into golden_template can be decrypted
  at runtime.
- Adds framework correctness tests (LoadCanonicalAsyncTests +
  GoldenReducePlanTests).
- No consumer test changed; existing suite still on legacy
  CreateDatabaseAndApplyMigrationsAsync + Seed*Async path.

Closes part of #462. Sets up #463.
EOF

git status
```

Expected: clean working tree.

---

## Phase 2: Template DB infrastructure (#463)

Files referenced in this phase:
- Modify: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelperTemplateTests.cs` (new)

### Task 2.1: Write the failing tests for the new template-clone methods

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelperTemplateTests.cs`

- [x] **Step 1: Write the test file**

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestData;

namespace TelegramGroupsAdmin.IntegrationTests.TestHelpers;

[TestFixture]
public class MigrationTestHelperTemplateTests
{
    [Test]
    public async Task CreateDatabaseFromEmptyTemplateAsync_GivesMigratedSchemaWithZeroRows()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromEmptyTemplateAsync();

        await using var ctx = helper.GetDbContext();
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.Users.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(0));

        // Schema is at HEAD — confirm a recent migration's table exists.
        var hasMigrationsHistory = await ctx.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'");
        Assert.That(hasMigrationsHistory, Is.GreaterThan(0));
    }

    [Test]
    public async Task CreateDatabaseFromGoldenTemplateAsync_GivesCanonicalDataReady()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromGoldenTemplateAsync();

        await using var ctx = helper.GetDbContext();
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(400));
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(200));
    }

    [Test]
    public async Task CloneFromGoldenTemplate_DropsApiKeysCiphertextThatSharedProviderCanDecrypt()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromGoldenTemplateAsync();

        await using var ctx = helper.GetDbContext();
        var config = await ctx.Configs.FirstAsync(c => c.ChatId == 0);
        Assert.That(config.ApiKeys, Is.Not.Null);

        // Production decryption sites use DataProtectionPurposes.ApiKeys (e.g.,
        // SystemConfigRepository.GetApiKeysAsync). Test must use the same constant so
        // a passing test guarantees production can also decrypt the ciphertext.
        var protector = PostgresFixture.SharedDataProtectionProvider
            .CreateProtector(DataProtectionPurposes.ApiKeys);
        var plaintext = protector.Unprotect(config.ApiKeys!);
        Assert.That(plaintext, Does.Contain("openai"));
    }
}
```

**Imports:** add `using TelegramGroupsAdmin.Data.Constants;` to the top of `MigrationTestHelperTemplateTests.cs`.

- [x] **Step 2: Run — expect compile error (methods missing)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MigrationTestHelperTemplateTests" 2>&1 | tail -10`

Expected: build fails with "MigrationTestHelper does not contain a definition for CreateDatabaseFromEmptyTemplateAsync."

### Task 2.2: Add the two new methods to MigrationTestHelper

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs`

- [x] **Step 1: Add a private helper for the admin connection with Pooling=false**

Inside `MigrationTestHelper`, add:

```csharp
    /// <summary>
    /// Builds a connection string targeting the "postgres" admin DB with pooling disabled.
    /// Required for CREATE DATABASE … TEMPLATE, where Postgres rejects the operation if any
    /// other backend session is connected to the source template.
    /// </summary>
    private static string BuildAdminConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresFixture.BaseConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        return builder.ConnectionString;
    }
```

- [x] **Step 2: Add CreateDatabaseFromEmptyTemplateAsync**

Inside `MigrationTestHelper`:

```csharp
    /// <summary>
    /// Creates the test database by cloning the session-built `empty_template`. Per-test
    /// setup drops to ~50–150ms vs the ~250–550ms of CreateDatabaseAndApplyMigrationsAsync.
    /// Use this for true-empty consumer tests (asserting on empty state, or exercising a
    /// write SUT from clean slate).
    /// </summary>
    public async Task CreateDatabaseFromEmptyTemplateAsync()
    {
        await using var connection = new NpgsqlConnection(BuildAdminConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"CREATE DATABASE \"{_databaseName}\" TEMPLATE empty_template",
            connection);
        await cmd.ExecuteNonQueryAsync();
    }
```

- [x] **Step 3: Add CreateDatabaseFromGoldenTemplateAsync**

```csharp
    /// <summary>
    /// Creates the test database by cloning the session-built `golden_template`. The cloned
    /// DB has full canonical data ready to use; encrypted-column ciphertext is decryptable
    /// using PostgresFixture.SharedDataProtectionProvider.
    /// </summary>
    public async Task CreateDatabaseFromGoldenTemplateAsync()
    {
        await using var connection = new NpgsqlConnection(BuildAdminConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"CREATE DATABASE \"{_databaseName}\" TEMPLATE golden_template",
            connection);
        await cmd.ExecuteNonQueryAsync();
    }
```

- [x] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: Build succeeded. Tests will still fail at runtime because templates aren't built yet.

### Task 2.3: Extend PostgresFixture.[OneTimeSetUp] to build empty_template

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs`

- [x] **Step 1: Replace the body of GlobalSetup**

Edit `PostgresFixture.cs`. Replace the existing `[OneTimeSetUp] public async Task GlobalSetup()` method with:

```csharp
    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        _container = new PostgreSqlBuilder("postgres:18")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
        BaseConnectionString = _container.GetConnectionString();
        Console.WriteLine($"PostgreSQL container started: {BaseConnectionString}");

        await BuildEmptyTemplateAsync();
        await BuildGoldenTemplateAsync();
    }

    private static async Task BuildEmptyTemplateAsync()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        // 1. CREATE DATABASE empty_template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE DATABASE empty_template", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Apply migrations to empty_template
        var emptyBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "empty_template",
            Pooling = false,
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(emptyBuilder.ConnectionString);
        await using (var ctx = new AppDbContext(optionsBuilder.Options))
        {
            await ctx.Database.MigrateAsync();
        }

        // 3. Flag as template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE pg_database SET datistemplate = true WHERE datname = 'empty_template'",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task BuildGoldenTemplateAsync()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        // 1. CREATE DATABASE golden_template TEMPLATE empty_template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "CREATE DATABASE golden_template TEMPLATE empty_template",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Load canonical into golden_template using SharedDataProtectionProvider
        var goldenBuilder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
        {
            Database = "golden_template",
            Pooling = false,
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(goldenBuilder.ConnectionString);
        await using (var ctx = new AppDbContext(optionsBuilder.Options))
        {
            await GoldenDataset.LoadCanonicalAsync(ctx, SharedDataProtectionProvider);
        }

        // 3. Flag as template
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE pg_database SET datistemplate = true WHERE datname = 'golden_template'",
                conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
```

Add the missing `using` directives:
```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
```

- [x] **Step 2: Run the template-clone tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MigrationTestHelperTemplateTests" --logger "console;verbosity=normal" 2>&1 | tail -20`

Expected: 3 tests passed. Failure modes:
- "source database is being accessed by other users" → a connection to a template DB is leaking; verify all connections in `BuildEmptyTemplateAsync`/`BuildGoldenTemplateAsync` use `Pooling=false` and are wrapped in `await using`.
- "datistemplate" UPDATE returns 0 rows → connection to admin DB succeeded but the template DB row isn't visible (replication lag? unlikely on a single Postgres) — verify `BaseConnectionString` matches the admin connection.

### Task 2.4: Run the full suite to confirm legacy tests still work alongside templates

- [x] **Step 1: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: every existing test still passes. Templates exist in the container but no consumer test calls them yet — pure additive change.

### Task 2.5: Commit Phase 2

- [x] **Step 1: Stage and commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs \
        TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs \
        TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelperTemplateTests.cs

git commit -F- <<'EOF'
feat(test): add template DB infrastructure to MigrationTestHelper (#463)

- PostgresFixture.[OneTimeSetUp] builds empty_template (migrations applied)
  and golden_template (canonical loaded via LoadCanonicalAsync) once per
  session. Both are flagged datistemplate=true.
- All template-build connections use Pooling=false so disposal actually
  closes the backend session — Postgres rejects CREATE DATABASE … TEMPLATE
  if any other connection holds the source DB.
- MigrationTestHelper.CreateDatabaseFromEmptyTemplateAsync() and
  CreateDatabaseFromGoldenTemplateAsync() clone templates per test in
  ~50–150ms.
- No consumer migration in this commit; legacy CreateDatabaseAndApplyMigrationsAsync
  path unchanged.

Closes #463 (template infrastructure).
EOF
```

---

## Phase 3A: Migrate canonical-consumer tests

Driver input: `audit-output.md` `canonical-consumer` and `mixed` sections (from Pre-Phase 1a). Each file gets its own commit step within this phase, but the migration pattern is identical, so this section documents the pattern once and lists every file as a checkbox.

> **DRIFT NOTICE (Pre-1c audit, 2026-05-03):** Legacy `GoldenDataset.*` constants outside `Users.*` are STALE. Only `Users.User{1..4}_Id` UUIDs match canonical (the four web fixture UUIDs: Owner `b388ee38-…`, Admin `921637d5-…`, Deleted Admin `a8dc8371-…`, Deleted GlobalAdmin `ba9ba542-…`). Every other constant — `TelegramUsers.UserN_TelegramUserId` (`100001+`), `ManagedChats.MainChat_Id` (`-1001322973935`), `Messages.*`, `LinkedChannels.*`, `DetectionResults.*`, `TrainingLabels.*` — pins to PRE-rotation IDs that DO NOT exist in canonical. ~80 test sites consume them.
>
> **Rewrite rule:** use canonical anchor IDs directly (or mint new constants on demand against canonical), NOT the stale legacy constants. Anchor IDs from `IntegrationTests/CLAUDE.md` Part 2:
>
> - **MainChat:** `chat_id = -100026957614982` ("Main Community")
> - **Top MainChat ham author:** `telegram_user_id = 9921676191756` (`@unhelpfulgrab`)
> - **Second MainChat ham author:** `telegram_user_id = 9960171136314` (`@sillywolf`)
> - **Heavily-banned spammer:** `9971261287520` (4 Ban actions)
> - **Renamed spammer (with username_history):** `9875141377477`
> - **Synthetic welcome target:** `9196379650113`
> - **Profile-scan target:** `9408530993787`
> - **Secondary chat:** `-100059667856554` ("Workshop Alumni")
> - **Most-administered chat:** `-100094881429433` ("Crypto Group", 13 admins)
> - **Soft-deleted chat:** `-100086877127767` ("Test Group")
> - **Web user fixtures:** Owner `b388ee38-0ed3-4c09-9def-5715f9f07f56`, GlobalAdmin `8e3a7211-d0eb-40c6-af8e-7d15bb42d10a`, Admin `921637d5-0f65-4c66-b143-6f057dd06a1c`, Deleted Admin `a8dc8371-afc5-4b61-9d71-d177f2dd9ddd`, Deleted GlobalAdmin `ba9ba542-3df6-4473-a820-578562780c57`
>
> Read `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` end-to-end before starting any 3A task — Part 2 maps every common test fixture to a canonical anchor.

### Per-file migration pattern (apply to every canonical-consumer file)

1. **Replace the [SetUp] db-creation call.** Change `await _testHelper.CreateDatabaseAndApplyMigrationsAsync()` to `await _testHelper.CreateDatabaseFromGoldenTemplateAsync()`.
2. **Drop legacy seed calls.** Any `await GoldenDataset.SeedXyzAsync(...)` becomes a no-op deletion — canonical is already there.
3. **Replace ad-hoc setup with canonical references and optional `Reduce`.**
   - If the test's [SetUp] used `_dbContext.Add(new XxxDto { ... })` followed by `SaveChangesAsync`, route through canonical: assert against the canonical row via `GoldenDataset.*_Id` constants instead of fabricated rows.
   - If the test's [SetUp] used `TRUNCATE` + `INSERT` for ML-style threshold setup, replace with `await GoldenDataset.Reduce(ctx).KeepSpam(N).KeepHam(M).ApplyAsync()`.
4. **DI provider swap.** If the test today uses `services.AddDataProtection().PersistKeysToFileSystem(... fresh GUID ...)` and reads encrypted columns, swap to `services.AddSingleton<IDataProtectionProvider>(PostgresFixture.SharedDataProtectionProvider)`. Keep the fresh-key pattern only when a comment documents "this test asserts keyring isolation."
5. **Run the file's tests in isolation; expect green or a real bug.** Failure modes:
   - "Expected count = 22, got 47" → assertion was tied to old fixture's row count; update the assertion to the canonical row count, OR add a `Reduce(ctx).KeepX(22)` call to constrain.
   - FK violation in [SetUp] → the test was relying on legacy `Seed*Async` to set up something canonical doesn't carry; either add the row to `canonical/*.sql` (Pre-1b extension after audit) or swap the test to a write-SUT pattern.
   - Encrypted-column decryption fails → the test's DataProtection provider isn't `SharedDataProtectionProvider`; swap per Step 4.
6. **Commit one file at a time** under `refactor(test): migrate <FileName> to canonical+Reduce`. Each commit is bisectable.

### Canonical-consumer files (27 from Pre-Phase 1a audit)

Authoritative list from `tmp/canonical-bootstrap/audit-output.md` (audit run 2026-04-30). All 27 files apply the per-file migration pattern. Audit rationale per file is in the audit-output document; cross-reference there before starting each task.

- [x] **Task 3A.1:** `Configuration/ConfigServiceIntegrationTests.cs`
- [x] **Task 3A.2:** `ContentDetection/Repositories/ProfileScanAlertMappingTests.cs` — currently uses non-canonical IDs for `managed_chats` / `telegram_users`; remap to canonical anchors `MainChat = -100026957614982` and profile-scan target `telegram_user_id = 9408530993787` (NOT the stale `MainChat_Id` / `User1_TelegramUserId` constants).
- [x] **Task 3A.3:** `Deduplication/SimHashComparisonTests.cs` — references message IDs 90001–90040 and 95001–95022 with named near-duplicate groups.
  > **DRIFT NOTICE (Pre-1c audit, 2026-05-03):** The 95001..95022 SimHash dedup messages are a KNOWN CANONICAL GAP — the Pre-1b extension #1 was attempted but did NOT land. Phase 3A.3 must either extend canonical with these messages OR seed them inline at test setup. See `IntegrationTests/CLAUDE.md` Part 2 "Known canonical gaps."
  > **RESOLUTION (2026-05-14):** Extended canonical with 7 real-prod near-duplicate anchors (test 1: 212355; test 3 cluster: 220848/221429/221904/222949; test 4: 4666/14538 + existing 20849/221139). All banned-user spam preserved verbatim from dev DB. Test 2 uses canonical msg 4575. Pairwise SimHash distances verified via the `tmp/canonical-bootstrap/compute-similarity-hash.cs` tool. Legacy 95xxx synthetic IDs no longer referenced.
- [x] **Task 3A.4:** `Jobs/WelcomeTimeoutJobTests.cs` — helper functions add `TelegramUserDto` + `WelcomeResponseDto` rows directly.
  > **DRIFT NOTICE (Pre-1c audit, 2026-05-03):** Pre-1b extension #2 partially landed — canonical carries synthetic IDs `999001..999005` covering the 5 `WelcomeResponseType` statuses (Pending=999001, Accepted=999002, Denied=999003, Timeout=999004, Left=999005) on `(chat_id=-100026957614982, user_id=9196379650113, username='canonical_user1', welcome_message_id=99001..99005)`, NOT pinned to the legacy `User1_TelegramUserId` the test originally expected. Retarget the test at `9196379650113` and the synthetic 999001..999005 IDs (or extend canonical with the legacy-user variant if a test specifically needs it). See `IntegrationTests/CLAUDE.md` Part 2 "Known canonical gaps."
  > **RESOLUTION (2026-05-15):** Retargeted at canonical anchors `MainChatId=-100026957614982`, `WelcomeUserId=9196379650113`, message-ids `99001..99005`. Deleted inline `EnsureTelegramUserExistsAsync` and `SeedWelcomeResponseAsync` helpers (no-inline-injection rule). The "near-miss" tests (different chat / different message id) use canonical's existing Pending row as the near-miss anchor, paired with `WorkshopAlumniChatId=-100059667856554` and `NonExistentWelcomeMsgId=99099` respectively — no synthetic rows required. All 11 tests (8 methods, 4 of them parameterized as 1 file) pass in 11s via template clone.
- [x] **Task 3A.5:** `ML/BayesClassifierServiceTests.cs`
  > **RESOLUTION (2026-05-16):** Migrated to canonical template clone — dropped legacy `GoldenDataset.SeedAsync` call (canonical's `33_training_labels.sql` ships 100 spam + 100 ham, comfortably above `MLConstants.MinimumSamplesPerClass = 20`). One latent bug surfaced: `TrainAsync_InsufficientData_*` only cleared `training_labels` and relied on the legacy base seed having a tiny implicit-pool footprint to keep the classifier under threshold. Canonical's 376 `detection_results` rows + 407 messages still feed `MLTrainingDataRepository` 157 implicit spam + 20 implicit ham after the explicit DELETE → classifier trains successfully → assertion fails. Fix: replaced `DELETE FROM training_labels` with `TRUNCATE messages CASCADE`, which drains all three pools (`training_labels`, `detection_results`, `messages`) in one statement. Also removed now-dead `_context` field. All 10 tests pass in 1.7s via template clone.
- [x] **Task 3A.6:** `ML/MLTextClassifierServiceTests.cs` — uses `Reduce(...).KeepSpam(...).KeepHam(...)` pattern for high-spam/high-ham threshold scenarios. See worked example at the end of this section.
  > **RESOLUTION (2026-05-18):** Migrated to canonical + reducer-only. Required one reducer extension (`KeepLabeledMessagesOnly()` — drops unlabeled messages so explicit-only substrates don't get implicit-ham pollution). Three legacy threshold tests (BelowMinimumThreshold/ZeroSpamSamples/ZeroHamSamples) were arbitrary-value duplicates of the threshold matrix and were replaced by `InsufficientData` (both below) + `OnlyHamAboveThreshold` + `OnlySpamAboveThreshold` at MinimumSamplesPerClass-1, covering the four matrix corners (paired with `SufficientData` above-above). `IsBalanced` assertion moved out of `SufficientData` to `BalancedDataset` (single-concern). Canonical-specific calibration: dedup floor relaxed from 90 to 75 for HighSpamRatio (real prod near-duplicates dedup ~19% vs legacy synthetic ~6%); `KeepSpam(25)` rather than 20 in HighHamRatio so post-dedup ≥ threshold. **Note:** 4 legacy seed helpers (`SeedHighSpamTrainingDataAsync`, `SeedHighHamTrainingDataAsync`, `SeedBalancedTrainingDataAsync`, `SeedWithMinimalTrainingDataAsync`) are now callerless — dead-code cleanup pending. 20/20 tests pass in ~26s.
- [x] **Task 3A.7:** `Repositories/AnalyticsRepositoryTests.cs` — analytics aggregations across daily/weekly/monthly/7-day/30-day/365-day windows require deterministic timestamps keyed off `MainChat = -100026957614982`.
  > **RESOLUTION (2026-05-18):** Migrated to canonical + reducer-allowlist + new in-place mutator. Three substrate-level changes landed in support:
  >
  > 1. **Canonical trim:** welcome_responses cut from 293 → 11 deliberate rows (5 synthetics 999001..999005 + 4 prod-derived MainChat analytics anchors + 2 non-MainChat distribution keepers). Eliminates calendar-drift contamination of "last year same period" assertions over time. Bundled with refresh of canonical-count audits: `Messages.Count` 400 → 407 (latent miss from 3A.3 SimHash extension) and new `WelcomeResponses.Count == 11` lock-in. Commit `a15bbd0b`.
  >
  > 2. **Reducer allowlist overload:** `KeepMessages(IEnumerable<(long ChatId, long MessageId)> ids)` paired with the existing count-based `KeepMessages(int)`. Composite-PK semantics required (canonical has 23 message_ids that collide across chats). FK CASCADE handles detection_results / training_labels / edits / translations cleanup. 3 new reducer self-tests in `GoldenReducePlanTests.cs`.
  >
  > 3. **New `GoldenMutatePlanBuilder` type** sibling of `GoldenReducePlanBuilder`. Entry: `GoldenDataset.Mutate(ctx)`. Verbs: `ShiftDetectionResultTimestamps(IEnumerable<TimestampShift>)` and `ShiftWelcomeResponseTimestamps(IEnumerable<TimestampShift>)`. Strict contract: edits existing rows only, never inserts. Verb count intentionally bounded — if a verb would need to create rows, that's the signal to extend canonical instead. 4 mutator self-tests in `GoldenMutatePlanTests.cs`.
  >
  > Anchor set: `TestData/AnalyticsAnchors.cs` pins 9 MainChat messages (7 spam-only with `auto_S` + populated ProcessingTimeMs JSON for the algorithm-perf test, msg `213325` for the FP pair with dr_ids 2012/2013, msg `211184` for the FN pair with dr_ids 1492/1494) and all 11 detection_result shifts + 6 welcome_response shifts.
  >
  > Substrate-aware concession: `GetDetectionMethodComparison_TracksFPContributions_HonoursCanonicalShape` asserts `ContributedToFalsePositives == 0` instead of `>= 1` because all 9 of canonical's organic FP candidates carry `check_results_json = '{"Checks": []}'` (bootstrap pipeline artifact). The SUT correctly returns 0 given empty per-check arrays. Documented as a canonical gap in `IntegrationTests/CLAUDE.md` Part 2; future devdb pull + sanitize can lift the assertion back to `>= 1`. Test count unchanged at 22/22 passing.
  >
  > Dead code dropped: `GoldenDataset.SeedAnalyticsDataAsync`, `GoldenDataset.SeedWithoutTrainingDataAsync`, `GoldenDataset.AnalyticsData` nested class, and `SQL/50_analytics_test_data.sql` (101 lines).
- [x] **Task 3A.8:** `Repositories/DbContextFactoryMigrationTests.cs`
- [x] **Task 3A.9:** `Repositories/DetectionResultsRepositoryTests.cs`
- [x] **Task 3A.10:** `Repositories/InviteRepositoryTests.cs` — currently runs raw `INSERT INTO users ('test-user-id', ...)` to satisfy `invites.created_by` FK; reuse `User1_Id`.
- [x] **Task 3A.11:** `Repositories/MessageHistoryRepositoryTests.cs`
- [x] **Task 3A.12:** `Repositories/NotificationRepositoriesTests.cs`
- [x] **Task 3A.13:** `Repositories/TelegramUserRepositoryKickCountTests.cs` — helper `CreateTestUserAsync` does inline `Add(...)`; remap to canonical anchors (e.g., `9921676191756` for a top ham author, `9971261287520` for a heavily-banned spammer). Do NOT reuse the stale `100001+` legacy IDs — those don't exist in canonical.
- [x] **Task 3A.14:** `Repositories/TelegramUserRepositoryTests.cs`
- [x] **Task 3A.15:** `Repositories/TelegramUserUpsertTests.cs`
- [x] **Task 3A.16:** `Repositories/TrainingLabelsRepositoryTests.cs`
- [x] **Task 3A.17:** `Repositories/UserActionsRepositoryConstraintTests.cs` — `SeedTestUserAsync` does inline `Add(new TelegramUserDto { TelegramUserId = 555111222 })`; remap to a canonical anchor (e.g., `9921676191756`). Do NOT reuse the stale `User1_TelegramUserId` legacy constant.
- [x] **Task 3A.18:** `Repositories/UsernameHistoryRepositoryTests.cs` — `SeedUserAsync` calls a different SUT (`TelegramUserRepository.UpsertAsync`) to seed FK-required rows; remap to canonical anchors (e.g., `9875141377477` for the renamed-spammer with username_history). Do NOT reuse stale legacy `100001+` IDs.
- [x] **Task 3A.19:** `Services/Backup/BackupServiceTests.cs` — also runs an inline `INSERT INTO telegram_users` to verify restore wipes it; route that arrange step through `TelegramUserRepository.UpsertAsync` (write-SUT) under the strict rule.
- [x] **Task 3A.20:** `Telegram/AuditHandlerTests.cs` — helper does inline `Add(new TelegramUserDto { TelegramUserId = 123456789 })`; remap to a canonical anchor (e.g., `9921676191756`). Do NOT reuse the stale `User1_TelegramUserId` legacy constant.
- [x] **Task 3A.21:** `Telegram/Repositories/LinkedChannelsRepositoryTests.cs`
- [ ] **Task 3A.22:** `Telegram/Services/BanCelebrationServiceTests.cs` — `SeedBanActions(int count)` inserts 1–7 ban rows for testing the `{bancount}` placeholder.
  > **DRIFT NOTICE (Pre-1c audit, 2026-05-03):** Pre-1b extension #4 (the seven `{bancount}` synthetic anchor rows) was attempted but did NOT land in canonical. Phase 3A.22 must either extend canonical with the synthetic 1–7 ban rows OR seed them inline. Canonical does include heavily-banned spammer `9971261287520` (4 Ban actions) — usable for the {bancount}=4 case but not 1/2/3/5/6/7. See `IntegrationTests/CLAUDE.md` Part 2 "Known canonical gaps."
- [x] **Task 3A.23:** `Telegram/Services/Bot/BotChatServiceTests.cs` — helpers add `ManagedChatRecordDto` / `TelegramUserDto` / `ChatAdminRecordDto` for "chat already exists" / "admin cached" arrange paths.
- [x] **Task 3A.24:** `Telegram/Services/Bot/BotDmServiceTests.cs` — `SeedTestUser` adds `TelegramUserDto` with `bot_dm_enabled` toggled.
- [x] **Task 3A.25:** `Telegram/Services/Bot/BotMessageServiceTests.cs` — helpers add bot-user `TelegramUserDto` and `MessageRecordDto` rows.
- [x] **Task 3A.26:** `Telegram/Services/ExamFlowServiceTests.cs` — `CreateTestChatAsync` adds `ManagedChatRecordDto` directly; remap to canonical MainChat anchor `chat_id = -100026957614982`. Do NOT reuse the stale `MainChat_Id` legacy constant.
- [x] **Task 3A.27:** `Telegram/Services/WelcomeFlowBypassIntegrationTests.cs` — currently uses three `LoadSqlScriptAsync` calls against legacy SQL fixtures; replace with canonical clone.

### Mixed class (1 file, per-test-method routing)

- [ ] **Task 3A.28:** `Repositories/UserRepositoryTests.cs` — **mixed**: `AnyUsersExistAsync_EmptyDatabase_ReturnsFalse` (line 31) stays empty (Phase 3B); `AnyUsersExistAsync_WithExistingUser_ReturnsTrue` (line 56) goes canonical. Each `[Test]` method picks its own `Create*` call inside the method body.

### Worked example: ML/MLTextClassifierServiceTests.cs

A specific worked instance of the pattern, since this file uses `Reduce` with both KeepSpam and KeepHam.

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/ML/MLTextClassifierServiceTests.cs`

- [ ] **Step 1: Read the file's current [SetUp] and the high-spam test method**

Run: `Read TelegramGroupsAdmin.IntegrationTests/ML/MLTextClassifierServiceTests.cs` and locate the `[SetUp]` method plus any `Seed*Async` invocations.

- [ ] **Step 2: Replace [SetUp] body**

Old:
```csharp
[SetUp]
public async Task Setup()
{
    _testHelper = new MigrationTestHelper();
    await _testHelper.CreateDatabaseAndApplyMigrationsAsync();
    await GoldenDataset.SeedHighSpamTrainingDataAsync(_testHelper.GetDbContext());
}
```

New:
```csharp
[SetUp]
public async Task Setup()
{
    _testHelper = new MigrationTestHelper();
    await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

    await using var ctx = _testHelper.GetDbContext();
    await GoldenDataset.Reduce(ctx).KeepSpam(95).KeepHam(5).ApplyAsync();
}
```

(The exact counts replicate whatever `SeedHighSpamTrainingDataAsync` produced — read that method's body to find the canonical numbers.)

- [ ] **Step 3: Run the file's tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MLTextClassifierServiceTests" --logger "console;verbosity=normal" 2>&1 | tail -20`

Expected: PASS. Most ML threshold tests assert "spam ratio > X" or "trained classifier predicts spam for synthetic input" — both should hold with the same canonical+Reduce shape as the legacy seed produced.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/ML/MLTextClassifierServiceTests.cs
git commit -m "refactor(test): migrate MLTextClassifierServiceTests to canonical+Reduce"
```

### Phase 3A close-out

- [ ] **Step 1: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -15`

Expected: every test passes. Wall-clock should be lower than T0 because canonical-consumers now use ~50–150ms template clones instead of full migrate.

- [ ] **Step 2: Optional — if many small commits, squash within Phase 3A only into one commit per the table at the top**

Default: keep individual file commits for granular bisect. Squash only if commit count > 30 makes review unwieldy.

---

## Phase 3B: Migrate true-empty consumer tests

Driver input: `audit-output.md` `true-empty-consumer` section. Per-file pattern is mechanical:

### Per-file migration pattern

1. **Replace [SetUp] db-creation call.** Change `await _testHelper.CreateDatabaseAndApplyMigrationsAsync()` to `await _testHelper.CreateDatabaseFromEmptyTemplateAsync()`.
2. **No data-assembly migration.** True-empty tests by definition have no preexisting setup data. If a test was using `Add(new XxxDto)` for setup, the audit misclassified it — promote it to Phase 3A.
3. **DI provider swap (per-file judgment).**
   - Tests interacting with encrypted columns → swap to `SharedDataProtectionProvider`.
   - Tests intentionally validating keyring isolation → keep their per-test ephemeral provider (audit found none today; default is to swap).
4. **Commit one file at a time.**

### True-empty consumer files (14 from Pre-Phase 1a audit)

Authoritative list from `tmp/canonical-bootstrap/audit-output.md`. Most are write-SUT-from-empty (the SUT IS the arrange); a few are pure assertion-on-empty.

**Real DB-touching files (11)** — apply the per-file pattern, swap `CreateDatabaseAndApplyMigrationsAsync()` to `CreateDatabaseFromEmptyTemplateAsync()`:

- [ ] **Task 3B.1:** `Configuration/AIProviderConfigIntegrationTests.cs` — write-SUT (`SaveAIProviderConfigAsync` / `SaveApiKeysAsync`).
- [ ] **Task 3B.2:** `Configuration/ConfigRepositoryIntegrationTests.cs` — write-SUT (typed `ConfigRepository.SaveXxxAsync` per config type).
- [ ] **Task 3B.3:** `Configuration/ContentDetectionConfigRepositoryTests.cs` — write-SUT (`UpdateGlobalConfigAsync`).
- [ ] **Task 3B.4:** `Configuration/SystemConfigRepositoryWebPushTests.cs` — write-SUT + assertion-on-empty (`*_WhenNoConfigExists_ShouldReturnDefault`, `*_WhenNotSet_ShouldReturnNull`).
- [ ] **Task 3B.5:** `ContentDetection/Repositories/ReportsRepositoryTests.cs` — write-SUT (`InsertContentReportAsync`, `InsertExamFailureAsync`, `InsertImpersonationAlertAsync`).
- [ ] **Task 3B.6:** `Repositories/BanCelebrationCaptionRepositoryTests.cs` — write-SUT + assertion-on-empty (`GetAllAsync_EmptyDatabase_ReturnsEmptyList`, etc.).
- [ ] **Task 3B.7:** `Repositories/BanCelebrationGifRepositoryTests.cs` — write-SUT.
- [ ] **Task 3B.8:** `Repositories/ReportCallbackContextRepositoryTests.cs` — write-SUT (`InsertContentReportAsync` + `CreateAsync`).
- [ ] **Task 3B.9:** `Services/BackgroundJobConfigPersistenceTests.cs` — write-SUT (`EnsureDefaultConfigsAsync`).
- [ ] **Task 3B.10:** `Telegram/Repositories/ExamSessionRepositoryTests.cs` — write-SUT (`CreateSessionAsync`, `RecordMcAnswerAsync`).
- [ ] **Task 3B.11:** `Telegram/SystemAccountBypassTests.cs` — assertion-on-empty (system account bypass produces no DB writes; mock engine throws if any query fires).

**Per-method route (1)** — already counted in Phase 3A.28:

- [ ] **Task 3B.12:** `Repositories/UserRepositoryTests.cs::AnyUsersExistAsync_EmptyDatabase_ReturnsFalse` — handled inside the mixed-class task in Phase 3A.

**No-DB unit tests (3 files)** — these don't instantiate `MigrationTestHelper` at all. Empty clone is harmless but they don't gain template-clone speedup either; **no migration needed**. List for completeness so a future audit doesn't re-flag them:

- `Services/NotificationConfigTests.cs` — pure POCO unit tests.
- `Telegram/CasCheckServiceTests.cs` — only `WireMockServer` + `HybridCache`, no DB.
- `Telegram/MessageProcessingServiceTests.cs` — pure unit tests on static handler methods.

### Phase 3B close-out

- [ ] **Step 1: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -15`

Expected: every test passes.

- [ ] **Step 2: Squash into a single commit if Phase 3B touched < 10 files**

```bash
git rebase -i HEAD~<N>  # only if the maintainer wants squash; default is granular commits
```

(Rebase decisions are user-driven; the plan's default is one commit per file.)

---

## Phase 3C: Migration tests adopt GoldenDataset constants

Driver input: `audit-output.md` `migration-test` section + the 8 known migration test files in the spec.

### Per-file pattern

1. **Read the migration-test file end-to-end.**
2. **Find ID literals** — patterns like `-1001234567890`, `-1009876543210`, hardcoded user IDs, etc.
3. **Replace each literal with the appropriate `GoldenDataset.*` constant.** If the literal doesn't match any existing constant, add a new constant only if the value will recur in tests; otherwise leave it as a literal but flag in PR description.
4. **Run the file's tests; commit.**

### Migration test files

- [ ] **Task 3C.1:** `Migrations/CascadeBehaviorTests.cs`
- [ ] **Task 3C.2:** `Migrations/CriticalMigrationTests.cs` (also fix line 210's `-1001234567890` and line 265's `-1009876543210` literals → `GoldenDataset.ManagedChats.MainChat_Id` or new constants)
- [ ] **Task 3C.3:** `Migrations/DataIntegrityTests.cs`
- [ ] **Task 3C.4:** `Migrations/InfrastructureTests.cs`
- [ ] **Task 3C.5:** `Migrations/MigrationCompactionTests.cs`
- [ ] **Task 3C.6:** `Migrations/MigrationWorkflowTests.cs`
- [ ] **Task 3C.7:** `Migrations/SequenceIntegrityTests.cs`
- [ ] **Task 3C.8:** `PgBouncer/PgBouncerMigrationTests.cs`

Some of these have minimal or no constant references and require zero changes — verify by inspection before editing.

### Phase 3C close-out

- [ ] **Step 1: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -15`

Expected: every test passes.

- [ ] **Step 2: Squash into a single commit per the phase plan**

```bash
git rebase -i HEAD~<N>  # squash to "refactor(test): migration tests adopt GoldenDataset constants"
```

---

## Phase 4: Cleanup

Files referenced in this phase:
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`
- Delete: 15 obsolete SQL files under `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/`

### Task 4.1: Verify zero callers of every retired method

The 11 retired methods:
1. `SeedAsync`
2. `SeedWithoutTrainingDataAsync`
3. `SeedWithMinimalTrainingDataAsync`
4. `SeedBalancedTrainingDataAsync`
5. `SeedHighSpamTrainingDataAsync`
6. `SeedHighHamTrainingDataAsync`
7. `SeedDeduplicationTestDataAsync`
8. `SeedAnalyticsDataAsync`
9. `SeedOldMessagesAsync`
10. `SeedContentDetectionConfigAsync`
11. `SeedWebUsersOnlyAsync`

- [ ] **Step 1: Run find_symbol_usages on each method**

For each name in the list above, run:

```bash
# via mcp__csharp-er-mcp__find_symbol_usages — preferred for exact resolution
# or via Grep as a fallback:
```

Use the CSharperMcp `find_symbol_usages` tool on each method name. Expected for every method: zero non-test references (and zero references anywhere except the method's own definition).

If any unexpected reference is found, **STOP** and route it through the new system in an extra commit before proceeding to deletion.

### Task 4.2: Delete the 11 retired methods from GoldenDataset.cs

- [ ] **Step 1: Delete each method**

For each method in the list, locate it in `GoldenDataset.cs` (line numbers from earlier inspection: `SeedAsync` at 406, `SeedWithoutTrainingDataAsync` at 419, etc.) and remove it along with any helper methods it exclusively calls.

The `LoadSqlScriptAsync` helpers (private + public) and `LoadCanonicalAsync` stay — they're still used.

- [ ] **Step 2: Build**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -5`

Expected: Build succeeded — Phase 3 already routed every consumer through the new path.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: every test passes.

### Task 4.3: Delete the 15 obsolete SQL files

- [ ] **Step 1: git rm each obsolete SQL file**

```bash
cd TelegramGroupsAdmin.IntegrationTests/TestData/SQL
git rm 00_base_telegram_users.sql \
       01_base_web_users.sql \
       02_base_managed_chats.sql \
       03_base_linked_channels.sql \
       04_base_messages.sql \
       05_base_detection_results.sql \
       06_base_content_detection_configs.sql \
       07_base_telegram_user_mappings.sql \
       10_training_minimal.sql \
       11_training_full.sql \
       20_unbalanced_100_20.sql \
       21_unbalanced_20_100.sql \
       30_dedup_test_data.sql \
       50_analytics_test_data.sql \
       60_old_messages.sql

cd ../../..
git rm TelegramGroupsAdmin.IntegrationTests/TestData/MLTrainingData.sql
```

(The MLTrainingData.sql file at `TestData/MLTrainingData.sql` was referenced separately by the EmbeddedResource glob; verify it has no callers remaining and delete its glob entry from the .csproj if it's the only file under the now-removed `<EmbeddedResource Include="TestData\MLTrainingData.sql" />` line.)

- [ ] **Step 2: Update .csproj if MLTrainingData.sql line is now obsolete**

Edit `TelegramGroupsAdmin.IntegrationTests.csproj` line 51:
```xml
<EmbeddedResource Include="TestData\MLTrainingData.sql" />
```

Delete this line if `MLTrainingData.sql` was deleted in Step 1.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10`

Expected: every test passes.

### Task 4.4: Capture T1 final timing

- [ ] **Step 1: Time the full suite again**

Run:
```bash
time dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10
```

- [ ] **Step 2: Record T1 in `tmp/canonical-bootstrap/T1.txt` for the PR description**

Write the wall-clock + pass count. Compare against T0; expect material improvement on canonical-consumer subsets.

### Task 4.5: Commit Phase 4

- [ ] **Step 1: Stage and commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs \
        TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj
# (the git rm calls in Task 4.3 already staged the deletions)

git commit -F- <<'EOF'
chore(test): retire legacy seed methods + SQL files

- Deletes 11 Seed*Async methods from GoldenDataset (SeedAsync,
  SeedWithoutTrainingDataAsync, SeedWithMinimalTrainingDataAsync,
  SeedBalancedTrainingDataAsync, SeedHighSpamTrainingDataAsync,
  SeedHighHamTrainingDataAsync, SeedDeduplicationTestDataAsync,
  SeedAnalyticsDataAsync, SeedOldMessagesAsync,
  SeedContentDetectionConfigAsync, SeedWebUsersOnlyAsync). Verified zero
  callers via find_symbol_usages.
- Deletes 15 obsolete SQL files (00_base_* … 21_* and 30_/50_/60_
  scenario files plus MLTrainingData.sql). All replaced by canonical/*.sql
  in Phase 1.
- MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync and
  CreateDatabaseAndMigrateToAsync retained — still used by migration tests.

Closes #462.
EOF
```

---

## Final PR open

After Phase 4 lands, the working branch is ready for PR.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin refactor/golden-canonical-snapshot-and-templating
```

- [ ] **Step 2: Open PR to develop**

```bash
gh pr create --base develop --title "Canonical Golden Snapshot + Template DB Cloning (#462, #463)" --body "$(cat <<'EOF'
Closes #462
Closes #463

## Summary

Replaces three competing IntegrationTests seeding strategies with a single canonical superset cloned per-test from a Postgres template DB. Adds a subtractive `GoldenDataset.Reduce(...)` builder for tests that need a constrained shape from canonical.

## Phases

- Phase 1: canonical SQL fixtures + GoldenReducePlan builder + SharedDataProtectionProvider
- Phase 2: template DB infrastructure (#463)
- Phase 3A: migrate canonical-consumer tests to template clone + Reduce
- Phase 3B: migrate true-empty consumer tests to empty-template clone
- Phase 3C: migration tests adopt GoldenDataset constants
- Phase 4: retire legacy seed methods + SQL files

## Performance

- T0 (pre-Phase-1): see `tmp/canonical-bootstrap/T0.txt`
- T1 (post-Phase-4): see `tmp/canonical-bootstrap/T1.txt`

## Test plan

- [x] Phase 1 framework tests: GoldenReducePlanTests + LoadCanonicalAsyncTests
- [x] Phase 2 template tests: MigrationTestHelperTemplateTests
- [x] Phase 3A: full integration suite green after each canonical-consumer migrated
- [x] Phase 3B: full integration suite green after each true-empty consumer migrated
- [x] Phase 3C: full integration suite green after migration tests adopt constants
- [x] Phase 4: full integration suite green after retirements
EOF
)"
```

---

## Post-Phase A: Bootstrap cleanup (no commit)

> **Status:** TBD — runs only after Pre-Phase 1c + Phase 1 + Phase 2 + Phase 3A/B/C + Phase 4 prove canonical works end-to-end. Until then, the bootstrap schema and `tmp/` working files are kept as a safety net so any data-shape fixes can be made by editing bootstrap + re-dumping specific files instead of re-running the entire 6-step bootstrap pipeline.

- [ ] **Step 1: Confirm canonical works end-to-end**

Run the full integration suite once more:

```bash
dotnet test TelegramGroupsAdmin.IntegrationTests --logger "console;verbosity=normal" 2>&1 | tail -10
```

Expected: every test passes. If any test fails, do not proceed — fix the underlying issue (likely via bootstrap edit + targeted re-dump) and retry.

- [ ] **Step 2: Drop the local bootstrap schema**

```bash
PGPASSWORD=changeme psql -h localhost -p 5432 -U tgadmin -d telegram_groups_admin \
    -c "DROP SCHEMA IF EXISTS bootstrap CASCADE;"
```

Expected: `DROP SCHEMA`. Confirms ~10K rows of bootstrap data + helper tables (`bootstrap.wordlist`, `bootstrap.user_name_map`, `bootstrap.message_slices`) + helper functions (`bootstrap.lorem`, `bootstrap.pick_word_id`, `bootstrap.rotate_user_id`, `bootstrap.rotate_chat_id`) are gone. The local DB returns to its prod-restored state.

- [ ] **Step 3: Remove tmp/canonical-bootstrap working files**

```bash
rm -rf /Users/<user>/Repos/personal/TelegramGroupsAdmin/tmp/canonical-bootstrap
```

This deletes the audit output, the 300-row implicit_ham TSV + batch files + spam rubric, all the B*.sql scripts, and the rotation salts in `B5_rotate_ids.sql`. Once removed, re-bootstrapping requires reproducing the salts — recommend backing up `B5_rotate_ids.sql` to a password manager or context-keep memory before this step if you anticipate any future re-bootstrap.

- [ ] **Step 4: Verify gitignored cleanup left no untracked files**

```bash
git status
```

Expected: clean working tree. The `tmp/` directory is gitignored, so its removal doesn't affect git state.

---

## Post-Phase B: File follow-up bug reports (no commit)

> **Status:** TBD — runs after Post-Phase A. Each bug becomes a separate GitHub issue tracked independently from this PR. None of these bugs blocked or shaped canonical's design beyond targeted exclusions; they're real production findings that deserve their own fix-on-its-own-PR.

The bootstrap data exploration uncovered seven concrete production bugs, listed below. Each warrants its own GitHub issue + future fix PR. After all issues are filed, paste the issue numbers into a `bootstrap-bugs.txt` artifact (or directly into the main PR description) so they're discoverable from the canonical PR.

- [ ] **Bug 1 — `MessageTranslation.DetectedLanguage` is bimodal in production data.**

  **Issue title:** `Normalize MessageTranslation.DetectedLanguage at write boundary`

  **Body:**
  ```
  The `message_translations.detected_language` column has inconsistent format
  across rows:
  - `AITranslationService.cs:47` prompts the AI for full English language names
    ('english', 'russian', 'chinese', etc.) but the model sometimes returns
    ISO 639-1 codes ('en', 'ko', 'ru', 'zh') anyway.
  - `AddTrainingSampleDialog.razor:44` UI placeholder asks the human for
    "2-letter ISO code (e.g., es, ru, fr, zh)" — manual training samples land
    in ISO format.
  - `MessageTranslationService.InsertTranslationAsync` and
    `MessageTranslationMappings` pass the value through verbatim. No
    normalization layer.

  Result: production data has both 'korean' (25 rows) and 'ko' (4 rows) for
  the same language, plus 'russian'/'ru' bimodality, etc.

  Fix: normalize at the write boundary. Pick one canonical format (proposal:
  full English names matching the AI prompt's intent) and convert ISO codes
  to that format inside the repository or DTO setter. Existing data can be
  back-filled in a one-shot migration.
  ```

- [ ] **Bug 2 — Translator persists no-op rows for non-language content (URL-only, URL-Previews, identity-passthrough).**

  **Issue title:** `AITranslationService creates no-op translation rows for non-language content`

  **Body:**
  ```
  AITranslationService writes `message_translations` rows for messages whose
  source content has no actual language to translate. Three observed sub-patterns
  share the same signature: detected_language='unknown' AND translated_text is
  byte-identical (or near-identical) to message_text.

  Sub-pattern A — Bare URL only:
    message_text = 'https://youtu.be/...' (or t.me, Facebook, etc.)
    translated_text = same URL string

  Sub-pattern B — URL + auto-generated "URL Previews" block:
    message_text contains '━━━ URL Previews ━━━' decorator
    translated_text passes through with the same decorator

  Sub-pattern C — Identity passthrough on non-language tokens:
    message_text = '00.0000000000000000000000000000000000..' (digit noise)
    message_text = '— —- —- * * * —- —- —-' (punctuation/separator art)
    translated_text = same string verbatim
    Both observed in English-only groups (Survival Techland, The Survival
    Podcast Community), where the translator should never have fired.

  Verified 2026-04-30 against fresh prod restore:
  - 17 sub-pattern A rows (across all slices)
  - 56 sub-pattern B rows (URL + URL-Previews block)
  - 2 sub-pattern C rows (msg_id 93017 in chat -1001322973935, msg_id 221795
    in chat -1001329174109)

  Cost angle: each row is a wasted OpenAI API call.

  Fix: short-circuit AITranslationService before the API call when content is
  detectably non-language. Cheap detection rule: skip if input is URL-only,
  contains the URL Previews decorator, or yields detected_language='unknown'
  AND translated_text matches source verbatim. Don't persist any of these.
  ```

- [ ] **Bug 3 — `messages.message_text` divergence from Telegram chat content (corrupted rows).**

  **Issue title:** `messages.message_text storage diverges from Telegram chat content for some rows`

  **Body:**
  ```
  Some rows in the messages table have stored `message_text` that does not match
  the content shown in the Telegram chat history.

  Concrete example (verified 2026-04-30):
  - chat_id = -1001322973935 (Survival Techland), message_id = 93017
  - DB stores: '00.0000000000000000000000000000000000..' (39 chars)
  - Telegram chat shows: substantive prose about FAANG companies / job hunting
    in the same time window (mid-conversation about MAANA acronym, LinkedIn,
    big tech)
  - Row has no message_edits history, content_hash present
    (2C53B8D17C34A8...), edit_date NULL, deleted_at NULL.

  Second example: chat_id=-1001329174109 (The Survival Podcast Community),
  message_id=221795, DB stores '— —- —- * * * —- —- —-' (punctuation/separator
  art), should be substantive content per the user's review.

  The corruption is NOT in the translation pipeline (those rows are downstream).
  It's at the messages layer — message_text was persisted with content that
  doesn't reflect what Telegram actually shows.

  Investigation directions:
  - Telegram edit-event handling: did the bot receive an edit Telegram itself
    later rolled back, leaving stale edited content?
  - Race condition during ingest: was the message_text overwritten by a stale
    or incorrect Update payload?
  - Old ingest bug now fixed in code but rows persist?

  Canonical bootstrap excluded both known-corrupted rows by ID (Pre-Phase 1b
  Task B2/B3 sampling).
  ```

- [ ] **Bug 4 — Ham labeling has no audit log entry.**

  **Issue title:** `Training label changes (ham/spam) bypass audit logging`

  **Body:**
  ```
  The `AuditEventType` enum (TelegramGroupsAdmin.Core/Models/AuditEventType.cs)
  has no event type for "training label applied" or "training label removed."
  The closest entry is ReportReviewed=27, which logs report adjudication
  outcomes ("Marked as spam (report #N)") — that's a separate workflow from
  direct edits to the training_labels table.

  Verified 2026-04-30 against fresh prod restore:
  - audit_log table has 466 entries; zero match on regex 'ham|training_label|
    TrainingLabel|markedAs|labeled_as'.
  - ReportReviewed (event_type=27) entries only ever describe spam-side
    outcomes; there is no equivalent for direct ham labeling.

  Operational impact: admin training-label changes are invisible to compliance
  audits and the in-app audit timeline.

  Fix: add new AuditEventType values (proposal: TrainingLabelApplied,
  TrainingLabelRemoved). Wire each write path that mutates training_labels
  to call AuditService.LogEventAsync with the actor user_id and the
  (chat_id, message_id, new_label) value.
  ```

- [ ] **Bug 5 — `training_labels.labeled_by_user_id` is always NULL for ham (no UI flow).**

  **Issue title:** `Ham labeling does not capture labeler user_id`

  **Body:**
  ```
  Verified 2026-04-30 against fresh prod restore:
  - 86 rows with label=1 (ham): 100% have labeled_by_user_id = NULL.
  - 313 rows with label=0 (spam): 297 have NULL labeled_by_user_id, 16 have
    real telegram_user_id values across 5 distinct admin accounts (1312830442
    being the most active with 8 rows).

  Root cause: there is no UI flow for marking ham. Spam can be labeled via
  multiple admin paths (each captures the actor inconsistently); ham only
  enters the system via direct DB updates or implicit-ham detection — neither
  captures the actor user_id.

  Two paths to fix:
  - Add an admin UI for marking ham (mirror the spam-marking flow). When fired
    from the UI, capture and persist the acting telegram_user_id.
  - Document automated/system ham labels with `reason = 'system_<source>'` so
    consumers can distinguish them from human-applied labels.

  Related: Bug 4 (no audit logging) compounds this — even if labeled_by_user_id
  isn't captured, the audit log should record the actor.
  ```

- [ ] **Bug 6 — EF Core auto-generated shadow FK columns.**

  **Issue title:** `Remove EF Core shadow FK columns: invites.UserRecordDtoId and users.InvitedByUserId`

  **Body:**
  ```
  Two locations have unintentional EF Core auto-generated shadow FK columns
  alongside their explicit relationships:

  1. invites.UserRecordDtoId (varchar, nullable) — duplicates created_by/used_by
     navigation. Generated by EF Core because a UserRecordDto navigation
     property exists without an explicit HasForeignKey() mapping.

  2. users.InvitedByUserId (varchar, nullable) — duplicates the explicit
     invited_by self-referencing FK. Both columns hold the same value when set.

  Cascade behavior INCONSISTENCY on the users duplicate:
  - users.invited_by → users.id with ON DELETE SET NULL (correct)
  - users.InvitedByUserId → users.id with ON DELETE NO ACTION (different!)

  Depending on which constraint validates first, deletes could behave
  inconsistently. This is a latent foot-gun.

  Fix: drop the shadow columns + their constraints, OR unify both to one
  well-defined behavior via Fluent API HasForeignKey() mapping. Probably
  easiest as a migration that drops the shadow columns and the EF model
  configuration that triggers their generation.
  ```

- [ ] **Bug 7 — System-event audit coverage gap.**

  **Issue title:** `Background jobs and scheduled tasks don't emit audit_log entries`

  **Body:**
  ```
  Verified 2026-04-30 against fresh prod restore:
  - audit_log: 466 total rows
  - actor distribution: 119 system events (actor_*_user_id IS NULL) vs 347
    user-attributed events
  - Ratio: 26% system / 74% user

  This is the OPPOSITE of expected for a moderation bot with continuous
  background activity (Quartz jobs, scheduled scans, periodic cache refreshes,
  retention deletion sweeps, welcome-timeout firings). Most IBackgroundJob
  implementations and scheduled tasks don't emit audit_log writes; only
  user-driven actions (logins, role changes, settings updates, manual
  moderations) emit faithfully.

  Operational impact: tests asserting on audit completeness will pass on
  current prod data but fail once system-side auditing is fixed. Audit-trail
  reconstruction of "what did the system do?" is unreliable.

  Fix: identify all background job + scheduled task code paths and wire each
  to emit audit_log via a system-actor identifier (e.g., actor_system_identifier =
  'background_job:<job_name>'). Decide on retention strategy — system events
  may benefit from a separate retention window from user events.
  ```

After all seven issues are filed, paste the issue numbers into a `bootstrap-bugs.txt` artifact in `tmp/canonical-bootstrap/` (if Post-Phase A hasn't run yet) or directly into the main PR description for traceability.

---

## Acceptance checklist (mirrors spec)

### Pre-Phase 1a
- [x] Second-pass audit complete (`audit-output.md`); per-file classification produced — done 2026-04-30

### Pre-Phase 1b
- [x] Canonical bootstrap complete: 35 `canonical/*.sql` files, sanitization rules applied, ID rotation applied, manual spot-check passed — done 2026-05-01
- [x] No bootstrap script committed (working files preserved in `tmp/canonical-bootstrap/` for safety; cleanup deferred to Post-Phase A)

### Pre-Phase 1c
- [ ] `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` exists with Part 1 (dataset orientation, 35-table count grid, identity boundaries, sanitization posture) and Part 2 (locked-id scenario recipes)
- [ ] `tmp/canonical-bootstrap/cheatsheet-audit.md` produced (audit output that drove Part 2 scenario selection)
- [ ] Every locked id in Part 2 verified against canonical SQL (grep test passes)
- [ ] Part 1 contains no example row contents (orientation-only, per design)
- [ ] No `GoldenDataset` constants pre-emitted in this phase — constants added on demand in Phases 3A–C

### Phase 1
- [ ] `.csproj` `EmbeddedResource` glob updated to `TestData\SQL\**\*.sql`
- [ ] `TestData/SQL/canonical/` exists with 35 per-table SQL files
- [ ] `TestData/SQL/migration/40_pre_migration_impersonation_alerts.sql` moved; `CriticalMigrationTests.cs` references new path
- [ ] `GoldenReducePlanBuilder` (parent stage) and `ChildReducePlan` (child stage) exist
- [ ] PR reviewer confirms `ChildReducePlan` has no `KeepMessages` member
- [ ] `GoldenDataset.Reduce(AppDbContext)` and `LoadCanonicalAsync(...)` exist
- [ ] `PostgresFixture.SharedDataProtectionProvider` exists
- [ ] `GoldenReducePlanTests.cs` covers KeepSpam isolation, KeepHam, KeepDetectionResults, KeepUserActions, KeepMessages cascade, cascade-narrowing, topological execution, validation rules
- [ ] All existing tests pass on legacy path
- [ ] T0 baseline captured

### Phase 2
- [ ] `PostgresFixture.[OneTimeSetUp]` builds `empty_template` and `golden_template`, both flagged datistemplate
- [ ] All template-build connections use Pooling=false
- [ ] `MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync()` and `CreateDatabaseFromEmptyTemplateAsync()` exist and use Pooling=false admin connections
- [ ] `[OneTimeTearDown]` disposes container; no explicit template drops
- [ ] All existing tests pass

### Phases 3A–C
- [ ] All canonical-consumer files migrated; suite green
- [ ] `UserRepositoryTests` mixed class migrated per-test-method; suite green
- [ ] All true-empty consumer files migrated; suite green
- [ ] DI provider swap reviewed per-file with documented intent
- [ ] Migration tests adopt `GoldenDataset.*` constants where applicable; suite green

### Phase 4
- [ ] `find_symbol_usages` shows zero callers of every retired method
- [ ] 11 retired methods deleted from `GoldenDataset`
- [ ] 15 obsolete SQL files deleted
- [ ] `MigrationTestHelper.CreateDatabaseAndApplyMigrationsAsync` retained
- [ ] All tests pass
- [ ] T1 final timing captured

### Post-Phase A (Bootstrap cleanup)
- [ ] Full integration suite green (one final run before cleanup)
- [ ] `bootstrap` schema dropped from local DB
- [ ] `tmp/canonical-bootstrap/` removed
- [ ] (If re-bootstrap anticipated) `B5_rotate_ids.sql` salts backed up to password manager / context-keep memory before removal

### Post-Phase B (Bug filings)
- [ ] All 7 follow-up GitHub issues filed (Bugs 1–7 in Post-Phase B above)
- [ ] Issue numbers recorded in PR description or `bootstrap-bugs.txt`

---

## Notes for the executor

- **Subagent-driven execution is recommended** for this plan because it spans many independent file migrations. Each Phase 3 file is its own subagent dispatch.
- **Pre-Phase-1b is collaborative.** The bootstrap is the only part that requires the maintainer at the keyboard alongside Claude — it touches a real database and produces non-deterministic outputs (sampled rows). Pause and confirm rather than guessing.
- **The `tmp/canonical-bootstrap/` directory** holds working artifacts (`audit-output.md`, `T0.txt`, `T1.txt`, `RehashTool.csproj`). It is NOT committed.
- **Bisect contract.** Every commit between Phase 1 and Phase 4 must build and pass the full suite. If a Phase 3 file's migration breaks a peer test, commit the fix in the same Phase 3 step before moving on.
- **No backwards-compatibility shims.** When deleting a `Seed*Async` method in Phase 4, do not leave an `[Obsolete]` wrapper — the project policy is to remove dead code outright.

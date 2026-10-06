# Integration Test Canonical Dataset

This file is auto-loaded by Claude Code when working under `TelegramGroupsAdmin.IntegrationTests/`. It is the discovery surface for the canonical test dataset. **Do not embed example row contents here**: read the SQL files directly when you need exemplars. Counts and structural description belong here; rows do not.

## Part 0 - Test data rules (MANDATORY — read before writing any test here)

The binding rule set lives in `.claude/rules/integration-test-data.md` and is injected automatically when you edit files in this project or a superpowers plan/spec. The short form:

- A test asserts its logic against canonical rows. **Never seed a precondition** — no SUT write method (`ObserveAsync`, `SetBanStatusAsync`, `TrustUserAsync`, …) as setup, no `ctx.<Table>.Add`, no raw `INSERT`. A SUT write appears only when that write **is** the assertion subject.
- Canonical lacks a shape? **Do not add rows.** Find a canonical row no test or doc references and flag-edit it in place; keep the story plausible; pin it in `TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs`; add a Part 2 recipe marked "(canonical edit <date>)"; guard the precondition in the test by reading the row back.
- Read expected counts/names at runtime, never hard-code them.
- Hit an infrastructure problem (template build, FK, sequence, missing shape)? Escalate — never change the assertion to make it pass.

## Part 1 - Dataset orientation

### What this is
The canonical dataset is a frozen superset of every entity type the integration suite needs to read from. Tests clone it per-method via Postgres template DBs (`MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync`) and either consume it as-is, reduce it down with `GoldenDataset.Reduce(ctx).KeepMessages(...).ApplyAsync()` (subtractive — FK CASCADE drops everything outside the allowlist), or mutate it in-place with `GoldenDataset.Mutate(ctx).ShiftDetectionResultTimestamps(...).ApplyAsync()` (NOW()-relative re-timing for windowed aggregations). Source: `TelegramGroupsAdmin.Testing.Golden/SQL/canonical/*.sql` (33 files, 2,797 INSERT statements).

True-empty tests use `MigrationTestHelper.CreateDatabaseFromEmptyTemplateAsync` instead (post-migrate, zero rows) — cheaper than a golden clone, and the right choice when the SUT writes its own state from scratch.

### How we built it
Origin: prod DB snapshot from 2026-04-30. Bootstrap pipeline (full detail in `docs/superpowers/plans/2026-04-30-canonical-golden-snapshot-and-template-cloning.md` Pre-Phase 1b):

1. Mirrored 35 prod tables into a `bootstrap` schema (UNLOGGED, FK constraints replicated via `pg_get_constraintdef`).
2. Sampled 100 messages from each of 4 slices: explicit_spam, implicit_spam, explicit_ham, implicit_ham (400 total).
3. Copied all parent rows up front (Option D), then pruned unreferenced parents at the end (Strict-Plus prune).
4. Rotated user IDs into `[9_000_000_000_000, 10_000_000_000_000)` and chat IDs into `[-100_099_999_999_999, -100_000_000_000_000]` via deterministic md5 + secret salt. The `-100` chat prefix preserves Telegram supergroup format compatibility for app-code checks; 15-digit length keeps them clearly synthetic. `chat_id = 0` preserved as sentinel.
5. Sanitized non-banned ("ham") users with wordlist names; banned users keep real names as spam evidence.
6. Pinned content/sim hashes computed via temporary console app referencing `TelegramGroupsAdmin.Core` (real `HashUtilities.ComputeContentHash` + `SimHashService.ComputeHash`). These hashes are baked into the SQL and do NOT recompute on regeneration.
7. Dumped 35 per-table SQL files via `pg_dump --column-inserts` (run inside `tga-db` container for postgres:18 compatibility). `bootstrap.users` is CLUSTERed by `created_at` first so the self-referential `invited_by` FK is satisfied in topological insert order.

### Tables and counts

| Order | Table | Rows | Notes |
|-------|-------|------|-------|
| 01 | users | 9 | Web users, all 9 pinned in `GoldenDatasetConstants.WebUsers` (5 active login anchors, 1 active GlobalAdmin with a stored TOTP secret, 2 soft-deleted, 1 disabled). Canonical edits 2026-10-01: `rerun@` flag-edited to Disabled; `perfume@`'s `totp_secret` is filled at load from the plaintext fixture `01_users.totp_secrets.json` (see Part 2). All share one PBKDF2 hash. |
| 02 | telegram_users | 335 | Anchor set after Strict-Plus prune (every row referenced by >=1 child). Two rows flag-edited 2026-09-13 for Users-tab tests (see Part 2 recipes). |
| 03 | managed_chats | 21 | Synthetic themed names; one disambiguated duplicate via `is_deleted`. |
| 04 | configs | 20 | `chat_id=0` global row + 19 per-chat. Encrypted JSONB columns NULL in the SQL; `api_keys` on the global row is filled at load from the plaintext fixture `04_configs.api_keys.json` (encrypted with the template-build key ring). `welcome_config` populated only on global + Main Chat. |
| 05 | content_detection_configs | 18 | One per non-deleted managed_chat. |
| 06 | ban_celebration_captions | 74 | Reference data, copied whole. |
| 07 | ban_celebration_gifs | 92 | Reference data, copied whole. |
| 08 | blocklist_subscriptions | 7 | Reference data; URLs not scrubbed (these are public blocklists). |
| 09 | prompt_versions | 1 | Single Main Chat synthetic row; tests build version history via SUT. |
| 10 | recovery_codes | 8 | Canonical addition 2026-10-02: one unused set for `perfume@` (see Part 2). Empty until then because prod had none. |
| 11 | stop_words | 17 | Reference data. |
| 12 | tag_definitions | 7 | Reference data (6 prod-derived) + 1 synthetic `power-user` (usage_count 20) for the concurrent-decrement race test. |
| 13 | username_blacklist | 2 | 1 enabled + 1 disabled, both Exact match. |
| 14 | domain_filters | 0 | Empty by design. |
| 17 | web_notifications | 0 | Empty by design. |
| 18 | notification_preferences | 5 | One per active web user. |
| 19 | messages | 410 | 100 per slice: explicit_spam, implicit_spam, explicit_ham, implicit_ham. Plus 7 SimHash test anchors appended in 3A.3 (4666, 14538, 212355, 220848, 221429, 221904, 222949) — all banned-user spam from dev DB, preserved verbatim, FK-resolved against existing canonical telegram_users and MainChat. Plus 2 messages with converted OpenAI vetoes (94, 22127) appended 2026-09-28 from prod, authored by existing non-banned canonical users (see Part 2 recipe 4h). Plus @bagging_armado's join message 110342 appended 2026-10-03 from prod (see Part 2 "User identity service anchors"). |
| 20 | chat_admins | 104 | Snapshot of admin membership across all 21 chats. |
| 21 | linked_channels | 3 | One per chat that has a linked channel. |
| 22 | telegram_user_mappings | 3 | Cross-chat user identity links. |
| 23 | profile_scan_results | 12 | Includes a mix of clean and flagged scans; row 534 carries an `explicit_display_text` value for the explicit-username masking tests; row 526 and @Adexfunnel's imported prod row carry `ai_promotional_display_text = true` (canonical edit 2026-10-05). Columns `ai_promotional_display_text` (default false) and `source` (0 = FullScan, 1 = NameOnly; only row 528 is 1, see "Rescan job anchors"). |
| 24 | username_history | 4 | Rename trail for spam-rename-then-spam users. |
| 25 | admin_notes | 3 | Free-text rebuilt from sanitized telegram_users; rows 5+6 cross-reference each other's sanitized usernames. |
| 26 | audit_log | 100 | Connected-as / disconnected-as narrative anchored to canonical fixture identities. |
| 27 | user_tags | 12 | |
| 28 | welcome_responses | 11 | Deliberately trimmed from 293 (Pre-3A.7 audit): no test exercised the prod-derived volume. Kept: 5 synthetics 999001..999005 (one per WelcomeResponseType, anchors `WelcomeTimeoutJobTests`), 4 prod-derived MainChat anchors (ids 73/75/94/128) for AnalyticsRepositoryTests' status-distributed shape (3 Accepted + 1 Timeout + the synthetic Denied/Left filling out the 6 analytics windows), 2 non-MainChat keepers (ids 55/121) for chat-grouping shape diversity. |
| 29 | invites | 19 | |
| 30 | reports | 14 | `reviewed_by` mapped via deterministic hashtext to canonical fixture emails. Ids 186-188 are synthetic pending fixtures (one user, three report types) added for join-gate cleanup tests. All six pre-existing exam (`type=2`) contexts carry `"outcome": 0`. Profile-scan (`type=3`) `context.aiSignals` restored to string arrays (canonical edit 2026-10-01; see Part 2 "Reports"). |
| 31 | message_edits | 23 | Edit history for messages whose canonical row carries `edit_count > 0`. |
| 32 | detection_results | 461 | Verdict events: `source`/`classification` set by `tools/convert-canonical-to-verdict-events.sql` (canonical edit 2026-09-27), which also folded the old explicit labels in as decision rows (84 added). Legacy verdict columns dropped by `DropLegacyVerdictColumns` (file regenerated without them, same rows). URL hostnames in `check_results_json` scrubbed to `canonical-spam.test`. dr 22 and 1639 appended 2026-09-28 (converted OpenAI vetoes, stored as `AddVerdictEvents` repairs them). `is_spam` is generated from `classification` (not in the file); the `message_verdicts` view is the verdict. |
| 34 | user_actions | 993 | Bootstrap missed adding 7 synthetic ban-celebration anchor rows; see Part 2 ban-celebration note. |
| 35 | message_translations | 14 | Non-noop translations only; URL hostnames scrubbed. |
| 36 | ban_celebration_subscribers | 5 | Approved canonical addition 2026-09-25 (new table — no row to flag-edit). See Part 2 "DM ban celebration subscribers". |

### What's NOT in the dataset
- **Encrypted JSONB credentials** in `configs` (sendgrid keys, web push keys) - left NULL. Populated at runtime by the app via `IDataProtectionProvider`. Exception: `api_keys` on the global row — `GoldenDataset.LoadCanonicalAsync` encrypts the plaintext fixture `canonical/04_configs.api_keys.json` (one dummy AI connection key, `GoldenDatasetConstants.SystemConfig`) with the session's provider, so the app under test can read it back.
- **Media files** referenced by `messages.media_features` / local media paths - no payloads on disk.
- **Email verification tokens, password reset tokens, locked_until timestamps** - all NULL.
- **TOTP secrets** - NULL except where canonical fixtures need TOTP-enabled state for tests (see Part 2 scenarios).
- **Operational / transient tables** (10 SKIP tables): `cached_blocked_domains`, `exam_sessions`, `file_scan_quota`, `file_scan_results`, `pending_notifications`, `push_subscriptions`, `report_callback_contexts`, `telegram_link_tokens`, `telegram_sessions`, `verification_tokens`. These are NOT exported. Tests that need them seed inline.

### Identity boundaries
- **Telegram user IDs:** `[9_000_000_000_000, 10_000_000_000_000)`. Synthesized via `abs(md5(real_id || 'canonical-user-rotation-2026')) % 10^12 + 9*10^12`.
- **Chat IDs:** `[-100_099_999_999_999, -100_000_000_000_000]`. Same pattern, `'canonical-chat-rotation-2026'` salt. The `-100` prefix matches the Telegram supergroup format that app code checks (e.g., `chatId.ToString().StartsWith("-100")`); the 15-digit length keeps them clearly synthetic vs real 13-digit supergroup IDs. `chat_id = 0` preserved as sentinel.
- **Web user UUIDs:** 4 fixed canonical fixtures (see Part 2) + 5 rotated prod UUIDs.
- **Web user password (all 9):** `Passw0rd!SaidNoSecurityAuditorEver`. Hash already in `01_users.sql`. Login flow tests can authenticate any web user with this password.

### Sanitization posture
- **Banned telegram_users (status 2):** real names preserved. They ARE the spam signature; tests rely on them.
- **Non-banned telegram_users:** first/last/username replaced with deterministic wordlist values; NULL fields stay NULL.
- **Cross-table free-text references** (`admin_notes`, `audit_log` narrative, `reports.reviewed_by`): rewritten to point at the canonical (sanitized or rotated) name, not the prod name. The `Connected as <name> (ID: <id>)` audit pattern is rebuilt from sanitized telegram_users data; admin identities map to canonical fixture emails (`owner/admin/globaladmin@example.com`) via deterministic hashtext.
- **URL hostnames in spam content** (`messages`, `message_translations`, `detection_results.check_results_json`): uniformly replaced with `canonical-spam.test`, paths/queries preserved verbatim. No domain exceptions (`t.me` included). This matches what the SUT actually does (hostname-only blocklist + tokenizer-based ML).
- **Free-text JSONB fields** (`reports.context` aiReason/aiSignals, exam answers): the length-preserving lorem sanitizer rewrites string values; it also rewrote three profile-scan `aiSignals` *arrays* as single strings (shape change, not a prod shape — repaired 2026-10-01, guarded by `CanonicalReportContextShapeTests`). Check `jsonb_typeof` before trusting a sanitized JSONB value's shape.
- **PII in spam messages:** phone numbers replaced with NANP-reserved `+15555550199` / `555-555-0199`; non-canonical emails replaced with `spam@canonical.test`. Not load-bearing for spam classifier features.
- **LLM prompt content** (`configs.welcome_config` + `prompt_versions`): minimized to 1 global synthetic baseline + 1 Main Chat customized variant + 1 Main Chat `prompt_versions` row. The other 18 per-chat configs have `welcome_config = NULL` (fall back to global) and `invite_link = NULL`.
- **`username_blacklist`:** trimmed to 2 rows (1 enabled-Exact + 1 disabled-Exact). Other match types (Contains/Regex/StartsWith) are not implemented in `BlacklistMatchType` / `UsernameBlacklistService.CheckDisplayNameAsync`; fixtures for those should be added when the feature ships.

### Schema reference
For column-level details, read the per-table SQL file directly (`head -1 <file>` shows the INSERT column list, then read a row or two). Do not transcribe column lists into this document.

### Canonical anchors in code
Test code references canonical IDs through `TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs` — the single discovery surface for every ID the test suite pins to. **Magic-string UUIDs and bare numeric chat/message ids in test code are a code smell**: extend `GoldenDatasetConstants` with a named constant instead.

Layout (`public static class GoldenDatasetConstants` with nested static classes):
- `WebUsers` — ids and emails for the Owner, Admin, GlobalAdmin, no-TOTP GlobalAdmin, no-TOTP Admin, Deleted Admin and Deleted GlobalAdmin anchors, plus the shared `SecurityStamp` and `Password` (canonical `users.id` UUIDs)
- `Chats` — `MainChatId`
- `Retention` — message anchors, `MessageShifts`, `AllMessageRefs`, `ExpectedDeletionsWith30DayRetention` (consumed by `MessageHistoryRepositoryTests.CleanupExpiredAsync_*`)
- `Analytics` — DR / WR anchors, `DetectionResultShifts`, `WelcomeResponseShifts`, `AllMessageRefs`, expected counts (consumed by `AnalyticsRepositoryTests`)
- `Reports` — `PendingExamFailureId`, `ResolvedExamFailureId`, `AutoApprovedExamPassId`, `AutoApprovedExamPassUserId` (consumed by `ExamResultRepositoryTests`)
- `Verdicts` — verdict-event anchors for the `message_verdicts` view, training levels, auto-trust and retention (see Part 2 "Verdict events")

Promote a constant to its top-level domain class (`WebUsers`, `Chats`) once a second consumer wants it; until then, keep it under a test-domain nested class next to the tests that use it. The `GoldenDataset.cs` loader file (also in `TelegramGroupsAdmin.Testing.Golden`) holds the canonical *behavior* (`LoadCanonicalAsync`, `Reduce`, `Mutate`); the constants file holds canonical *data*.

## Part 2 - Scenario recipes

Each recipe pins a canonical anchor row by id so a test author (or test-writing agent) can grab a fixture without re-querying. **If you change canonical and a recipe id no longer resolves, update the recipe in the same commit**: stale recipes are a code smell.

Recipe format: a heading, the anchor id(s), a one-line description, and "use when" guidance.

### Web users

#### Owner: full-access fixture
- `User.Id` = `b388ee38-0ed3-4c09-9def-5715f9f07f56`
- Email: `owner@example.com`, permission_level 2, status 1, TOTP enabled
- Use when: a test needs the highest-privilege web user (system administration, settings mutation).

#### GlobalAdmin: cross-chat elevated fixture
- `User.Id` = `8e3a7211-d0eb-40c6-af8e-7d15bb42d10a`
- Email: `ahead@canonical.test`, permission_level 1, status 1, TOTP enabled, invited by Owner
- Use when: a test needs an active elevated (cross-chat) admin who is NOT the Owner (permission boundary tests). The other active GlobalAdmins have their own recipes below: `machine@` (no TOTP) and `perfume@` (stored TOTP secret).

#### GlobalAdmin without TOTP: password-login elevated fixture
- `User.Id` = `c2674f3a-16e6-4537-9cbc-a80a0ea9c686`
- Email: `machine@canonical.test`, permission_level 1, status 1, TOTP disabled
- Use when: E2E or integration tests need an elevated user who logs in with password alone (no TOTP prompt). Constants: `GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminId` / `NoTotpGlobalAdminEmail`. Password: `GoldenDatasetConstants.WebUsers.Password`.

#### Admin without TOTP: password-login standard fixture
- `User.Id` = `28d7aa41-5be5-43a3-a48e-7b1a4bbe5891`
- Email: `reshoot@canonical.test`, permission_level 0, status 1, TOTP disabled
- Use when: E2E or integration tests need a standard-permission user who logs in with password alone (no TOTP prompt). Constants: `GoldenDatasetConstants.WebUsers.NoTotpAdminId` / `NoTotpAdminEmail`.

#### Admin: standard-permission fixture
- `User.Id` = `921637d5-0f65-4c66-b143-6f057dd06a1c`
- Email: `admin@example.com`, permission_level 0, status 1, TOTP enabled, invited by Owner
- Use when: a test needs an authenticated user with normal permissions (most authenticated-flow tests). The other Admin-level rows are the no-TOTP Admin (`reshoot@`, recipe below) and the disabled one (`rerun@`, status 2, recipe below).

#### Deleted Admin: soft-delete fixture
- `User.Id` = `a8dc8371-afc5-4b61-9d71-d177f2dd9ddd`
- Email: `deleted@example.com`, status 3 (deleted), is_active false
- Use when: a test asserts on soft-delete behavior or filters out deleted users (E2E: the Restore action on the Web Admin Accounts page). The other soft-deleted row is the GlobalAdmin below.

#### Deleted GlobalAdmin: soft-deleted elevated fixture
- `User.Id` = `ba9ba542-3df6-4473-a820-578562780c57`
- Email: `globaladmin@example.com`, permission_level 1, status 3
- Use when: a test asserts that elevated-but-deleted users are still excluded.

#### Disabled Admin: disabled-account fixture (canonical edit 2026-10-01)
- `User.Id` = `6a66f0f6-6e59-45ac-ac5f-51a2df0c9c58`
- Email: `rerun@canonical.test`, permission_level 0, status 2 (Disabled), is_active false, TOTP disabled, invited by Owner
- Edit: status 3 → 2. Story: the Owner disabled the account twelve minutes after creating it (modified_by Owner, modified_at 06:12) rather than deleting it. Canonical had no Disabled web user; the two remaining soft-deleted rows (`deleted@`, `globaladmin@`) keep the soft-delete recipes intact.
- Use when: a test needs a web user in the Disabled state — the Enable action, or the default status filter (Active + Pending + Disabled) showing a non-active row. Constants: `GoldenDatasetConstants.WebUsers.DisabledAdminId` / `DisabledAdminEmail` / `DisabledAdminStatus`. Guarded by `CanonicalWebUserAnchorTests.DisabledAnchor_IsADisabledInactiveAdmin`; E2E tests read the status back in `ArrangeDataAsync`.

#### GlobalAdmin with a stored TOTP secret (canonical edit 2026-10-01)
- `User.Id` = `f2f2f5c2-2cd2-45a1-a272-83f59076fb40`
- Email: `perfume@canonical.test`, permission_level 1, status 1, TOTP enabled, email verified
- Edit: the only web user whose `users.totp_secret` is non-NULL. The SQL keeps the column NULL (ciphertext is key-ring bound, like `configs.api_keys`); the base32 plaintext lives in `SQL/canonical/01_users.totp_secrets.json` (user id → secret) and `GoldenDataset.LoadCanonicalAsync` protects it at load with `DataProtectionPurposes.TotpSecrets` — the purpose the app's `DataProtectionService` uses — so `TotpService` can unprotect it. A dummy 20-byte secret; it protects nothing.
- Use when: a test needs a user with a completed TOTP setup — the Owner's Reset TOTP action (the menu item renders only with a stored secret), or a real authenticator login (`TotpHelper` + the plaintext). Constants: `GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminId` / `StoredTotpGlobalAdminEmail` / `StoredTotpGlobalAdminBase32`. Guarded by `CanonicalWebUserAnchorTests.StoredTotpAnchor_*`. Do not give owner@/admin@/ahead@ a secret: UI-login tests rely on them landing on /login/setup-2fa.

#### Recovery codes: `perfume@` holds one unused set (canonical addition 2026-10-02)
- `recovery_codes` rows 1-8, all for `StoredTotpGlobalAdminId` (`f2f2f5c2-2cd2-45a1-a272-83f59076fb40`), `used_at` NULL: the set completing TOTP setup issues. The SQL stores hashes only; row 1's plaintext is pinned.
- Use when: a test needs an existing recovery code set, e.g. that re-issuing replaces it or that a stored code is accepted once. Constants: `GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCodeCount` / `StoredTotpGlobalAdminRecoveryCode`. Guarded by `LoadCanonicalAsyncTests` (exact count).

#### Locked web user: `GoldenDataset.Mutate(ctx).LockWebUser(id, lockFor)` (no canonical edit)
- A lockout is only "locked" while `locked_until` is ahead of NOW(), so canonical's frozen snapshot cannot carry one. The mutate verb sets `locked_until = NOW() + lockFor` and `failed_login_attempts = 5` (`AccountLockoutConstants.MaxFailedAttempts`) on any canonical web user — the shape `AccountLockoutService` leaves after the fifth failed login.
- Use when: a test needs the Locked chip / Unlock Account action (E2E) or `UserRecord.IsLocked` / `CanLogin` false. Lock an active anchor such as `NoTotpAdminId`; apply before the app starts.

### Telegram users

#### Top MainChat author (ham)
- `telegram_user_id` = `9921676191756`
- `@unhelpfulgrab`, "Squeak Degree", `is_banned=false`
- 24 messages in canonical, mostly in MainChat. Cross-referenced by `admin_notes` row 6 (notes them as a suspected duplicate of `9452657005278`).
- Use when: a test needs a real, prolific MainChat author.

#### Second active MainChat author (ham)
- `telegram_user_id` = `9960171136314`
- `@sillywolf`, "Early Spirits", `is_banned=false`
- 23 messages. `admin_notes` row 4 references this user with a generic "Test Note".
- Use when: a test needs a paired second author for cross-author scenarios in MessageHistoryRepositoryTests.

#### Suspected duplicate account (ham, cross-referenced)
- `telegram_user_id` = `9452657005278`
- `@strainermaroon`, "Obtrusive Impure", `is_banned=false`
- `admin_notes` row 5 ties this account to `@unhelpfulgrab` ("Same account as @unhelpfulgrab").
- Use when: a test needs an account with a non-trivial `admin_notes` narrative (free-text cross-reference to another canonical user).

#### Kicked joiner (welcome timeout, never verified)
- `telegram_user_id` = `9171379870502`
- `@luminanceflagstick`, "Agnostic", `is_active=false`, `is_banned=false`, 0 messages, 2 `user_actions` (Mute "Pending welcome verification", Kick "Welcome timeout") in MainChat
- Use when: a test needs a user who never passed the join gate (hidden from the Active tab, shown on All with the Unverified chip). Constant: `GoldenDatasetConstants.UsersPage.KickedJoinerId`.

#### Mixed-issuer moderation subject (Audit Log filters)
- `telegram_user_id` = `9704804870465`
- Untouched canonical row with 9 `user_actions` (ids 196..245, Nov 2025): Auto-Detection (ban + deletes), three web users (Owner, the stored-TOTP GlobalAdmin, the no-TOTP GlobalAdmin: trust/untrust pairs) and a Telegram admin (`telegram_user_id` 9906913218017) — every actor kind the moderation log's Issued By column renders. Fewer rows than the moderation table's first page (25), so a filtered first page shows them all.
- Use when: a test filters the Audit Log's Telegram Moderation Log by user and needs to prove the user column alone drives the narrowing. Constant: `GoldenDatasetConstants.ModerationLog.MixedIssuerUserId`. Issued-by anchors for the same filter (`UserActionsRepositoryIssuedByFilterTests`, `AuditLogGoldenTests`): `SystemActorIds.ExamFlow` (8 `user_actions`, rendered "Exam Flow"), the no-TOTP GlobalAdmin (`WebUsers.NoTotpGlobalAdminId`, 8 rows, rendered as its email) and the Telegram admin `ModerationLog.TelegramAdminIssuerId` (20 rows, rendered as its full name — read the names from the clone, never paste them). Counts are read from the clone at runtime.

#### Trusted kicked joiner (canonical edit 2026-09-13)
- `telegram_user_id` = `9301917046112`
- `@tadpolesleek`, "Supply", `is_active=false`, `is_trusted=true` (flag edited in place; row is otherwise a welcome-timeout kick like the one above)
- Use when: a test needs trust independent of join-gate state. Constant: `UsersPage.TrustedKickedJoinerId`.

#### Expired temp-ban with the flag still set (canonical edit 2026-09-13)
- `telegram_user_id` = `9995544961449`
- `@curveabdominal`, "Crawling", `is_active=false`, `is_banned=true`, `ban_expires_at=2026-04-30 12:56:53+00` (past), `banned_at=2026-04-30 00:56:53+00`
- Use when: a test needs a user the Banned tab drops (expired) that every other status tab also excludes — the shape the All tab guarantees. Constant: `UsersPage.ExpiredBanUserId`.

#### Users page members: trust subject, badge holders, warned members (no canonical edit — pinned unreferenced rows)
- `UsersPage.UntrustedActiveMemberId` = `9704788798695`: `is_active=true`, `is_banned=false`, `is_trusted=false`, not a bot, no `chat_admins` row, 2 messages in one chat. The Users page trust toggle's subject (`UsersGoldenTests`): the app write IS the assertion (`is_trusted` + a `Trust` `user_actions` row by the Owner).
- `UsersPage.ChatAdminMemberId` = `9187417286258`: trusted active member with 4 `is_active=true` `chat_admins` rows. Carries both the Chat admin and the Trusted badge — every non-bot active canonical admin is also trusted, so no canonical row proves the admin badge alone.
- `UsersPage.WarnedTrustedMemberId` = `9685233957282` and `UsersPage.ExpiredWarningTrustedMemberId` = `9086323729821`: trusted active members, no active admin seat, one `warnings` JSONB entry each with `ExpiresAt` in April 2026 — like all three canonical warnings, expired at the snapshot. The first is the anchor for `ExtendTelegramUserWarnings` (below); the second stays expired to prove the active-warning predicate (Active renders "None"; Tagged does not list it — the Tagged tab counts notes, live tags and warnings in force only).
- Use when: a test needs a trust toggle target, a row with the Trusted / Chat admin badge, or a member with a warning. Read-backs in the tests guard every flag.

#### Warning in force: `GoldenDataset.Mutate(ctx).ExtendTelegramUserWarnings(id, expiresIn)` (no canonical edit)
- A warning counts only while its `ExpiresAt` is ahead of NOW() (90-day default expiry), so canonical's frozen snapshot carries only expired warnings. The mutate verb sets every element's `ExpiresAt` in `telegram_users.warnings` to `NOW() + expiresIn`, leaving IssuedAt, Reason, actor and context alone; it fails loudly for a user with no warnings (that is a missing anchor, not something to invent).
- Use when: a test needs the Warnings cell chip, `WarningCount > 0`, or `GetActiveWarningCountAsync` non-zero. Re-time `UsersPage.WarnedTrustedMemberId` and read the expected count back with the same predicate (`ExpiresAt == null || ExpiresAt > now`); apply before the app starts. Self-tests: `GoldenMutatePlanTests.ExtendTelegramUserWarnings_*`.

#### Email configured: `GoldenDataset.Mutate(ctx).EnableSendGridApiKey(provider, key)` (no canonical edit)
- Canonical's global `sendgrid_config` is enabled with a from-address, but no SendGrid key is stored in `api_keys` (encrypted columns are not in the SQL), so `IFeatureAvailabilityService.GetEmailConfigurationStateAsync` reads `Disabled`: the Login page hides the forgot-password / resend-verification links and the Register page shows the "email verification disabled" note. The mutate verb decrypts the canonical `api_keys` under `DataProtectionPurposes.ApiKeys`, adds `sendGrid`, and re-encrypts, leaving the AI connection keys and every other row alone; it fails loudly without a stored key set. Pass the key ring the app under test uses (E2E: the fixture's shared key directory, application name `TgSpamPreFilter`). The key is a dummy.
- Use when: a test needs email read as configured (Login page links, password-reset or verification flows) without a SUT write. Self-tests: `GoldenMutatePlanTests.EnableSendGridApiKey_*`.

#### Removed tag on an otherwise untagged member (canonical edit 2026-10-01)
- `user_tags.id` = `11` (`UserTags.RemovedTagId`), `telegram_user_id` = `9579510369392` (`UserTags.RemovedTagUserId`)
- `@parasailprojector`, "Recycler": active, trusted, not banned, no admin note, no `chat_admins` row; its only tag, "helpful-user", has `removed_at=2025-11-15 17:20:00+00` (after `added_at` 2025-10-28) and `removed_by_web_user_id` = the Owner
- Use when: a test needs a member whose only tag is removed — Tagged/`IsTagged`/`TaggedCount` must ignore it (`TelegramUserRepositoryTests` removed-tag tests). Guarded by a read-back of `removed_at`.

#### Heavily-banned spammer
- `telegram_user_id` = `9971261287520`
- `@lazinessunsheathe`, "Reappear Math"
- 4 `Ban` action_type rows in `user_actions` (the most of any canonical user). Tied for top is `9793662571780` (also 4 bans).
- Use when: a test needs a user with a thick `user_actions` audit trail.

#### Renamed spammer (rename-then-spam pattern)
- `telegram_user_id` = `9875141377477`
- Currently sanitized; `username_history` row 2 records the prior display name as "QQQ".
- `is_banned=true`, `is_active=true`.
- Use when: a test needs a user with `username_history` and a confirmed ban. Three more analogues exist with non-Latin prior names: `9032620986755`, `9095125964119`, `9726308613009`.

#### Synthetic welcome-flow target
- `telegram_user_id` = `9196379650113`
- `@squishierspectacle`, "Recall Zen"
- The fixed user_id behind synthetic `welcome_responses` 999001..999005 (one per `WelcomeResponseType`).
- Use when: a test exercises the welcome-response status branches (Pending / Accepted / Denied / Timeout / Left).

#### Profile-scan target
- `telegram_user_id` = `9408530993787`
- Has `profile_scan_results` row 532 (low score 0.2, outcome=0, AI text intact).
- Use when: a test needs a canonical row in `profile_scan_results` with substantive AI signals.

### Managed chats

#### Main Community: the canonical "MainChat"
- `chat_id` = `-100026957614982`
- Holds 198 of 400 messages (49.5%). Carries the only non-NULL `welcome_config` JSONB outside the global row, the only non-NULL `prompt_versions` row, and a `linked_channels` row.
- Use when: any test needs a primary chat anchor — exposed in code as `GoldenDatasetConstants.Chats.MainChatId`.

#### Workshop Alumni: secondary chat for cross-chat tests
- `chat_id` = `-100059667856554`
- Second-most messages (40). 5 `chat_admins` members.
- Use when: a test needs a second active chat to pair against MainChat (`chatA` vs `chatB` patterns in MessageHistoryRepositoryTests).

#### Crypto Group: most-administered chat
- `chat_id` = `-100094881429433`
- 13 `chat_admins` members (most of any canonical chat). 16 messages.
- Use when: a test needs a chat with a large admin set (chat-admin permission tests).

#### Test Group: soft-deleted edge case
- `chat_id` = `-100086877127767`
- `is_active=false`, `is_deleted=true`, `bot_status=2`.
- Use when: a test asserts on soft-deleted chats being filtered out of active queries.

#### Land Owners Group: chat with linked channel
- `chat_id` = `-100017312732389`
- Active chat. 25 messages. Carries no `linked_channels` row directly (see linked-channel anchors below for chats that do).
- Use when: a test needs a non-MainChat with substantive message volume and a global welcome flow.

### Linked channels

#### MainChat linked channel
- `linked_channels.id` = `1`, `chat_id` = `-100026957614982`, `channel_id` = `-100021999196951`
- Use when: a test asserts on MainChat's linked channel (only canonical chat that has one named after MainChat).

### Messages

#### Message with multiple detection_results
- `message_id` = `20465`, `chat_id` = `-100055570785509`, `user_id` = `9611864826059`
- Carries 4 `detection_results` rows (different detector edits / methods). `20416` is a tied alternate.
- Use when: a test needs a message with multi-detector history.

#### Message with edit history (in MainChat)
- `message_id` = `212340`, `chat_id` = `-100026957614982`
- Has a `message_edits` row (id 337). Other in-MainChat edited messages: `211396`, `218375`.
- Use when: a test needs an edited message anchored in MainChat.

#### Message with translation
- `message_id` = `8567`, `chat_id` = `-100017312732389`, `user_id` = `9461937425965`
- `message_translations` row 75 carries a Spanish-detected translation. (No translations exist in MainChat; pick a non-MainChat anchor.)
- Use when: a test exercises translation lookup or asserts on detected_language metadata.

#### Sample spam-labeled message (explicit spam decision)
- `message_id` = `4575`, `chat_id` = `-100048429560480`, `user_id` = `9331684387862`
- Scan dr2084 (`ImplicitHam`) then decision dr2087 (`LegacyManual`, `ExplicitSpam`) → `message_verdicts` = `ExplicitSpam`.
- Use when: a test needs a message whose verdict is an explicit spam label.

#### Sample ham-labeled message (explicit ham decision)
- `message_id` = `103`, `chat_id` = `-100082190806505`, `user_id` = `9320215215920`
- Scan dr27 (`ImplicitHam`) then decision dr41 (`LegacyManual`, `ExplicitHam`) → `message_verdicts` = `ExplicitHam`.
- Use when: a test needs a message whose verdict is an explicit ham label.

### Configs

#### Global config (chat_id = 0)
- `configs.id` = `1`, `chat_id` = `0`
- Carries the only non-NULL global `welcome_config` baseline. Encrypted JSONB columns NULL in the SQL (DataProtection injection target); `api_keys` is written post-load from `04_configs.api_keys.json` (`GoldenDatasetConstants.SystemConfig`).
- Use when: a test reads global fallback configuration or exercises the encrypted-column injection path.
- `backup_encryption_config` deliberately keeps the legacy `Algorithm` and `Iterations` keys, which the model no longer has. Real deployments carry them until the next passphrase rotation, so the row is the anchor for tolerant-deserialization tests (`BackupEncryptionConfigStoredJsonTests`, constant `GoldenDatasetConstants.SystemConfig.GlobalChatId`). Do not strip the keys.

#### Main Community per-chat config (overrides global)
- `configs.id` = `15`, `chat_id` = `-100026957614982`
- The only per-chat config with a non-NULL `welcome_config`. Mirrors the global baseline with one customized prompt variant.
- Use when: a test needs to verify per-chat override fallback behavior, or to exercise a chat that has a non-global welcome config.

#### Per-chat content_detection_configs (sample)
- `content_detection_configs.id` = `2`, `chat_id` = `0` (global baseline)
- 17 additional per-chat rows (one per active managed_chat).
- Use when: a test needs a representative content-detection config row.

### DM ban celebration subscribers (canonical addition 2026-09-25)

Anchors are in code as `GoldenDatasetConstants.DmCelebrations`. None of these users was referenced by any test or doc before this addition.

| User | Id | `bot_dm_enabled` | Rows | Use when |
|---|---|---|---|---|
| @magnetismvoucher | `9183753414221` | true | Workshop Alumni | a subscriber who is deliverable |
| @thudupper | `9011393194616` | false | Workshop Alumni, stale prompt `424242` / `canonical-stale-prompt-job` | a subscriber who is not deliverable; cleanup of a timed-out prompt |
| @deepnessunmapped | `9689750659830` | false | Workshop Alumni + Poultry Community | removing one chat must leave the other |
| @chummyrepair | `9306234060091` | true | none (MainChat member) | the subscribe path, where the SUT upsert is the assertion subject |
| @ToniBaronePaul | `9782251136844` | true (and `is_banned=true`) | Workshop Alumni | a subscription row that outlived its owner's ban; "deliverable" must exclude banned users |

Workshop Alumni (`-100059667856554`) has a `ban_celebration_config` that is enabled for auto and manual bans (`sendToBannedUser` true) and no `welcome_config` (the global row applies), so a celebration there both posts to the chat and fans out to its deliverable subscribers. `BanCelebrationNameMaskingTests` uses it with `ScannedTwiceExplicitUserId` for caption name masking.

### Verdict events (canonical edit 2026-09-27)

`detection_results` is an append-only verdict-event log; `message_verdicts` resolves each message to its latest non-FileScan row (`detected_at DESC, id DESC`, `Unscanned` when none). The conversion is `TelegramGroupsAdmin.Testing.Golden/SQL/tools/convert-canonical-to-verdict-events.sql` (provenance, not embedded; its header records how it was run). `TestData/CanonicalSlices.cs` freezes every non-chat-0 message's slice under the old model (the since-dropped explicit label table + the legacy spam/training-membership flags); `CanonicalVerdictOracleTests` checks the view against it (msg 104948 is a documented spec-mandated override to ExplicitSpam) and checks chat-0 samples separately. Anchors are in code as `GoldenDatasetConstants.Verdicts`.

| Constant | Anchor | Verdict | Use when |
|---|---|---|---|
| `CorrectedToHamMsgId` | msg 213409, @dinnersnazzy (9257421184750), MainChat | scan dr2026, then manual ham correction dr2031 (`LegacyManual`) → `ExplicitHam` | a later decision supersedes a scan |
| `AutoBanMsgId` | msg 220384, @AndrewLong6 (9127536472473), MainChat | `AutoBan` decision (folded label, no user) → `ExplicitSpam` | auto-ban decisions; Remove from training |
| `MarkAsHamSubjectMsgId` | msg 8646, Land Owners (sender 9550752264926) | `AutoBan` → `ExplicitSpam` | the Mark as Ham write subject |
| `EditFlipMsgId` / `EditFlipChatId` | msg 82837, chat -100065252085265, @financerope (9468093502025), 5 edits | **edited:** ham-labeled, then edited into spam; the v1 rescan dr1334 wins → `ContentScan` / `ImplicitSpam` | an edit rescan supersedes an earlier admin decision; edits count once |
| `SpamInTrustWindowMsgId` / `SpamInTrustWindowUserId` | msg 7796, @mouthsafeguard (9917295586642), Land Owners | **edited:** dr1339 → `UntrainedSpam`; ham decision dr1349 and its label removed | spam among a user's last three messages (auto-trust denied) |
| `FileScanBesideScanMsgId` / `FileScanRowId` | msg 216684 (sender 9778846455554), MainChat; FileScan dr2535 | **edited:** dr2535 → clean `FileScan` (newest row, ignored by the view); verdict from dr2534 → `UntrainedSpam`; spam label removed | the view must skip FileScan rows |
| `UntrainedHamMsgId` / `UntrainedHamChatId` / `UntrainedHamUserId` | msg 22160, Crypto Group (-100094881429433), @wrongedjersey (9621984255379, not banned) | **edited:** dr1933 → AI review 2.0 below threshold → `UntrainedHam` | allowed-but-untrained ham (auto-trust counts it, training does not) |
| `UnscannedMsgId` (canonical edit 2026-09-28: distinctive `message_text`, `similarity_hash` recomputed via `SimHashService`) | msg 219219, @unhelpfulgrab, MainChat | no rows → `Unscanned` | unscanned messages; trains as implicit ham (text must survive SimHash dedup, so not lorem ipsum) |
| `AllHamUserId` | user 9184102838760, msgs 71028/71030/71041 | all `ExplicitHam` | auto-trust with N ham messages |
| `PhotoFeaturesMsgId` | msg 222818 (sender 9777802619662), MainChat, photo | **edited:** `media_features` = `{"type":"photo","hash":"8J8PDw8PH/8="}`; AutoBan dr3322 → `ExplicitSpam` | Layer 1 photo similarity reads a spam sample (messages ⋈ `message_verdicts`) |
| `VideoFeaturesMsgId` (canonical edit 2026-09-28) | msg 214424 (sender 9607332364262), MainChat, video (`photo_file_id` NULL) | **edited:** `media_features` = video with 3 keyframes (0.1/0.5/0.9, hashes in `VideoFeaturesKeyframeHashes`); LegacyManual dr2168 → `ExplicitSpam` | Layer 1 video similarity reads a spam sample (`MediaSampleRepository.GetRecentVideoSamplesAsync`) |
| `OpenAIVetoScanRowId` / `OpenAIVetoMsgId` (canonical edit 2026-09-28) | ContentScan dr1934 on msg 212950 (sender 9011155048805), MainChat | **edited:** OpenAI check → non-abstained clean (Score 0) over StopWords 2 + Bayes 5 → `ImplicitHam`, score 0; the message's verdict stays the later `/spam` decision dr1935 → `ExplicitSpam` | the current-encoding OpenAI veto (veto analytics / recently vetoed messages). dr1492 (FN pair, OpenAI *abstained*) is not a veto — see the veto tests. All canonical vetoes: `AllVetoScanRowIds` |
| `LegacyVetoEarlyScanRowId` / `LegacyVetoEarlyMsgId` / `LegacyVetoEarlyChatId` (approved addition 2026-09-28) | ContentScan dr22 on msg 94, chat -100082190806505, sender 9320215215920 (existing non-banned user) | **added from prod:** OpenAI clean answer that `RemoveV1ContentDetectionBridge` (2026-03-06) converted to `Abstained=true`, Score 4.5 (Confidence/20), repaired to the veto encoding over Bayes 4.9 → `ImplicitHam`, `properties` `{"backfilled": true, "repaired_legacy_veto": true}` | a converted veto as `AddVerdictEvents` leaves it (veto analytics count it) |
| `LegacyVetoLateScanRowId` / `LegacyVetoLateMsgId` / `LegacyVetoLateChatId` (approved addition 2026-09-28) | ContentScan dr1639 on msg 22127, chat -100094881429433, sender 9887521719353 (existing non-banned user) | **added from prod:** same repair from the later engine (score 0) over Bayes 0.5 → `ImplicitHam` | as above, second engine era |
| `LabeledOnlyRetentionMsgId` | msg 7974, @arisepacifism (9702019239117) | manual ham dr2009 (`LegacyManual`) → `ExplicitHam` | retention keeps decision-only messages |
| `Retention.MsgId_ExpiredWithEdits` (no canonical edit — pinned an unreferenced row) | msg 221932, MainChat, 1 edit (message_edits row 3014) | no `detection_results` rows → `Unscanned` (non-curated) | retention deletes an expired non-curated message together with its `message_edits` rows (edit-cascade coverage; task #548 review finding, 2026-09-27) |

Flag-edits (all rows were unreferenced by tests and docs beforehand):
- **4a** msg 82837: dr1343 (admin ham) and its explicit label row re-timed to `2025-10-29 21:59:00+00` (after scan dr1333, before the first edit at 22:00); dr1334 → score 4.5, `ImplicitSpam`, reason `[Edit #1] AI confirmed spam: …`.
- **4b** msg 7796: dr1339 → score 3.0, `UntrainedSpam`; dr1349 deleted; its explicit label removed (label file since dropped).
- **4c** msg 216684: dr2535 → `file_scan` / `FileScanningCheck`, `FileScan` / `UntrainedHam`, score 0, re-timed 1 minute after the newest row; its explicit label removed (label file since dropped).
- **4d** msg 22160: dr1933 → score 2.0, `UntrainedHam`, reason `AI below review threshold: AI: Review …`, `check_results_json` with a Similarity 3.5 check and an OpenAI 2.0 review.
- **4e** msg 222818: `media_features` set by an appended `UPDATE` in `19_messages.sql` (a scrubbed photo hash; no file on disk).
- **4f** (2026-09-28) msg 214424: `media_features` set by an appended `UPDATE` in `19_messages.sql` (synthetic keyframe hashes; no file on disk).
- **4g** (2026-09-28) dr1934 (msg 212950): OpenAI check `Score` 4.8 spam → 0 with an `AI: Clean - …` detail, `reason` = that detail, `score` 7 → 0, `classification` `UntrainedSpam` → `ImplicitHam` (what `ContentDetectionEngineV2.CreateVetoedResult` + `VerdictClassifier` would have written). Not the message's verdict, so `CanonicalSlices` is unchanged.
- **4h** (2026-09-28, approved addition — rows appended, not flag-edited) msgs 94 and 22127 with their scans dr22 and dr1639, copied from prod and sanitized like every other non-banned author's message: `message_text` is the canonical lorem at the original length (111 / 34 chars — lengths no other lorem row uses, so no byte-identical duplicate: implicit ham is not deduplicated against explicit ham, and a same-length lorem would collide with `CorrectedToHamMsgId`), `content_hash` NULL, `similarity_hash` recomputed via `SimHashService`; timestamps verbatim; the OpenAI reason/detail replaced with synthetic prose (check scores and Bayes key words as in prod); user and chat ids mapped to the canonical rotations of the same prod users/chats (both authors already in canonical, non-banned and sanitized). The scans are stored post-`AddVerdictEvents`: only the OpenAI check is rewritten (other checks' converted V1 "clean" results keep `Abstained=true` + a leftover Score, which no reader counts). Not in `CanonicalSlices` (added after the freeze).

### AI veto history: message id shared by two chats (canonical edit 2026-09-30)

Telegram message ids are only unique per chat, but no two canonical chats shared one. Anchors are in code as `GoldenDatasetConstants.AIVetoHistory`.

| Constant | Anchor | Use when |
|---|---|---|
| `SharedMessageId` / `SharedIdChatId` | msg 14538 exists in Poultry Community (-100017608907459, its newest message, sender 9718812162815, ExplicitHam via synthetic promotion dr3325) **and** MainChat (the WORMGPT SimHash anchor) | a chat-scoped query or `(MessageId, ChatId)` join must not confuse the two rows; AI veto history exclusion (#521) |

Flag-edit: Poultry Community's msg 14498 renumbered to 14538 (still after the chat's 14352; timestamp unchanged) in `19_messages.sql` (row + its `content_hash` `UPDATE`), dr3325's `message_id` in `32_detection_results.sql`, and its `CanonicalSlices` entry. Lookups of these ids by `message_id` alone are now ambiguous: always add the chat (`SimHashIntegrationTests` scopes its WORMGPT lookup to MainChat for this reason).

### Reports

#### Profile-scan alert `aiSignals` shape (canonical edit 2026-10-01)
`reports` 177, 178, 180 (`type=3`, resolved, real rows) had `context.aiSignals` stored as one lorem *string*. Production writes only arrays (`ProfileScanAlertContext.AiSignals` is `string[]`, verified against prod 2026-10-01: 9 rows, all arrays); the string shape was an artifact of the bootstrap's length-preserving lorem sanitizer, and it made `ReportsRepository.GetProfileScanAlertsAsync(pendingOnly: false)` — and so the whole Reports page — throw a `JsonException` on canonical data. The three values were split on `,`/`.` into short lorem items (`["Lorem ipsum dolor sit amet", "consectetur adipiscing elit", …]`); no real-looking signals were invented. Guard: `TestData/Tests/CanonicalReportContextShapeTests` (`jsonb_typeof(context->'aiSignals')`). Pending fixture 188 already carried an array.

### User identity service anchors (canonical edit 2026-10-03)
Anchors are in code as `GoldenDatasetConstants.IdentityService` (#552 part 1); all read-only.

| Constant | Anchor | Shape |
|---|---|---|
| `ScannedTwiceExplicitUserId` | 9220500615182 @bagging_armado | scans 530 (older) and 534 (newer); 534 explicit. Also the banned user in `BanCelebrationNameMaskingTests` (global `maskFlaggedNames` absent → true, so the caption shows the explicit label) |
| `ExplicitAuthorMessageId` | msg 110342, Workshop Alumni | @bagging_armado's real join service message (added from prod; deleted by `ban_cleanup`). The only message whose author's latest scan is explicit, so `enriched_messages` carries `latest_scan_explicit = true` on it |
| `ScannedCleanUserId` | 9025828368896 @swivelhumvee | not trusted, not banned, scanned once (score 0.0, Feb 2026) with a plain profile (no bio, personal channel 0, no photo or stories); `JoinRenameRescanTests` (the join observation that records the rename is the scenario under test) |
| `UnscannedUserId` | 9063342700386 @Juvenileii | not trusted, not a bot, no scan rows |
| `BotUserId` | 9742468412405 @doilyemcee | the canonical bot |
| `UntrustedNoHistoryUserId` | 9263051408340 @pastramiherbs | not trusted, active, no username_history |
| `TrustedUserId` | 9006671634371 @starlightskinless | trusted, not an admin |
| `RaceUserId` | 9680301255238 @violingentleman | not trusted, active, no history; row-lock race test |
| `PhotoUserId` | 9264989724828 @raceoutnumber | not trusted; user_photo_path and photo_hash set |
| `InactiveUserId` | 9332352149450 @calixrowen | is_active = false (banned spammer); MarkActiveAsync test |

### Flagged name anchors (canonical edit 2026-10-05)
Anchors are in code as `GoldenDatasetConstants.FlaggedNames` (#552 part 2). Tests read each anchor's flags back first.

| Constant | Anchor | Shape |
|---|---|---|
| `NameOnlyScanUserId` | 9333810782137 @loucurtsinger | not trusted, not a bot, no scan rows, `profile_scanned_at` NULL (banned before scanning existed). `NameOnlyScanTests`: the name-only scan writes the user's first row (the write is the assertion subject). Read-only otherwise |
| `BannedPromotionalUserId` | 9635655270997 @Adexfunnel | banned by an admin after profile-scan alert #178 (score 2.8, held for review); **added:** its real prod scan row (approved import, AI text verbatim), with `ai_promotional_display_text = true`. `PromotionalNameMaskingTests`: spam label in group posts and the ban celebration caption + subscriber copy, real name in the admin DM. Report 178's `aiReason` is lorem (sanitized earlier); the scan row keeps prod's text |
| `UnbannedPromotionalUserId` / `UnbannedPromotionalScanId` | 9213195802818 @splendorfraying, row 526 | not banned, not trusted; its only scan row 526 (score 0.0) **edited:** `ai_promotional_display_text = true`. A flagged name of a user who is not banned is shown by real name |

Use when: a test needs a never-scanned, untrusted user whose first scan row is the subject (`NameOnlyScanUserId`), or a promotional name whose user is banned / not banned (`BannedPromotionalUserId` / `UnbannedPromotionalUserId`).

### Rescan job anchors (canonical edit 2026-10-05)
Anchors are in code as `GoldenDatasetConstants.ProfileRescan` (#552 part 2). `IncompleteScanSelectionTests` reads each back first and passes the user count as the batch size.

| Constant | Anchor | Shape |
|---|---|---|
| `NeverScannedUserId` | 9963580010331 "Ferocity Opponent" | never scanned; **edited:** `profile_scan_excluded` true → false (every never-scanned eligible user in canonical had been auto-excluded by the old unresolvable rule) |
| `ExcludedNeverScannedUserId` | 9434053902837 "Preflight Silk" | never scanned, excluded. Read-only |
| `NameOnlyLatestUserId` / `NameOnlyLatestScanId` | 9758118926756 @unreadbackspin, row 528 | one scan row, **edited:** `source` 0 → 1 (NameOnly). Retry-limit boundary: limit 2 selects, limit 1 does not. Also the read anchor for `ProfileScanResultsRepositoryTests.GetLatestSourceAsync_CanonicalNameOnlyUser_ReturnsNameOnly` (read-only) |
| `FullScanLatestUserId` | 9922735795237 @parkingsturdily, row 533 | one FullScan row from 2026-04-30; never selected however old. Read-only |
| `MultiChatUserId` / `MultiChatLatestChatId` / `MultiChatOldestChatId` | 9739143127436 @elvesunable | undeleted messages in Hobby Forum (latest), Main Community, Garage Chat (oldest); pins `GetChatsForUserAsync` order. Read-only |

Use when: a test needs the job's incomplete-scan selection. Every other eligible canonical user has at most one scan row, so a mixed NameOnly / FullScan history is not available without an approved import.

### Past-name search anchors (canonical edit 2026-10-03)
Anchors are in code as `GoldenDatasetConstants.UsernameHistory`. Both owners are banned spammers (All and Banned tabs, not Active).

| Constant | Anchor | Shape |
|---|---|---|
| `PastUsernameUserId` / `PastUsername` | 9032620986755 @BryanNguyen54, history row 3 | prior username `rsza_tilla` (flag-edited from NULL; prior names "Rsza Тилляев" unchanged) |
| `PastFirstNameUserId` / `PastFirstName` | 9875141377477 "Jeanette", history row 2 | prior first name `QQQ` |
| `NoPastUsernameUserId` | 9095125964119, history row 4 | prior names "Tin Tun" / "Min", no prior username; `UsernameHistoryRepositoryTests` field mapping and isolation (read-only) |
| `CascadeDeleteUserId` | 9726308613009, history row 1 | `UsernameHistoryRepositoryTests` deletes the user in its clone to test the cascade, and reads it for isolation |

Use when: a search must match a user by a past name only (`TelegramUserRepositoryTests` search region), or a rename must be recorded at a known time (`UsernameHistoryRepositoryTests` `HasChangeSinceAsync`, which reads row 2's `recorded_at` back). Tests read the history row and the current names back first.

### Synthetic / reserved rows (do not regenerate)
- `welcome_responses` IDs `999001..999005`: 5 status branches anchored on `(MainChat_Id=-100026957614982, user_id=9196379650113, username='canonical_user1')`. Mapping: `999001`=Pending, `999002`=Accepted, `999003`=Denied, `999004`=Timeout, `999005`=Left.
- `username_blacklist` IDs `999001` (`pattern='spambot_admin'`, enabled, Exact match) + `999005` (`pattern='archived_pattern'`, disabled, Exact match). No Contains/Regex/StartsWith fixtures (feature not yet implemented). `999005` is also `GoldenDatasetConstants.Backup.BlacklistEntryId`: `BackupServiceTests.RestoreAsync_ShouldWipeAllTablesFirst` moves it to `999905` at runtime after taking the backup.
- `detection_results` rows with `reason='canonical_synthetic_promotion'`: 15 synthetic explicit-ham decisions (`LegacyManual` / `ExplicitHam`, folded from the old explicit label table).
- `reports` IDs `186..188`: 3 pending (`status=0`) fixtures, all for `9465377455871`, added for join-gate cleanup tests (the golden dataset's real reports are all already resolved). `186`=ContentReport pointing at real message `(70989, -100054416618415)` so the `enriched_reports.content_user_id` join resolves; `187`=ExamResult (failure) in chat `-100054416618415`; `188`=ProfileScanAlert in chat `-100048429560480`. `188` is also the one pre-existing pending profile-scan alert `ProfileScanAlertMappingTests` must account for.
- `reports` ID `189`: synthetic auto-approved ExamResult pass (status=1, `reviewed_by='Exam Flow'`, `action_taken='auto-approved'`, context `outcome=1`) for user `9960171136314` in MainChat, anchoring the auto-approval override tests. All six pre-existing exam contexts (`179, 181, 182, 183, 185, 187`) now carry `"outcome": 0`.

### Cross-references
- **Auth password (all 9 web users):** `Passw0rd!SaidNoSecurityAuditorEver`. Hash baked into `01_users.sql`.
- **`chat_id = 0`:** preserved sentinel (rotation skipped via CASE WHEN guard). Global config row, global content_detection_config row, and global stop_words/etc. anchor here.


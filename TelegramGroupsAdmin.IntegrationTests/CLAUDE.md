# Integration Test Canonical Dataset

This file is auto-loaded by Claude Code when working under `TelegramGroupsAdmin.IntegrationTests/`. It is the discovery surface for the canonical test dataset. **Do not embed example row contents here**: read the SQL files directly when you need exemplars. Counts and structural description belong here; rows do not.

## Part 1 - Dataset orientation

### What this is
The canonical dataset is a frozen superset of every entity type the integration suite needs to read from. Tests clone it per-method via Postgres template DBs (Phase 2+) and reduce it down with `GoldenReducePlan` (Phase 1+). Source: `TestData/SQL/canonical/*.sql` (35 files, 3,174 INSERT statements).

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
| 01 | users | 9 | Web users: 4 canonical fixtures + 5 prod-derived. All share one bcrypt hash. |
| 02 | telegram_users | 335 | Anchor set after Strict-Plus prune (every row referenced by >=1 child). |
| 03 | managed_chats | 21 | Synthetic themed names; one disambiguated duplicate via `is_deleted`. |
| 04 | configs | 20 | `chat_id=0` global row + 19 per-chat. Encrypted JSONB columns NULL; `welcome_config` populated only on global + Main Chat. |
| 05 | content_detection_configs | 18 | One per non-deleted managed_chat. |
| 06 | ban_celebration_captions | 74 | Reference data, copied whole. |
| 07 | ban_celebration_gifs | 92 | Reference data, copied whole. |
| 08 | blocklist_subscriptions | 7 | Reference data; URLs not scrubbed (these are public blocklists). |
| 09 | prompt_versions | 1 | Single Main Chat synthetic row; tests build version history via SUT. |
| 10 | recovery_codes | 0 | Empty by design. |
| 11 | stop_words | 17 | Reference data. |
| 12 | tag_definitions | 6 | Reference data. |
| 13 | username_blacklist | 2 | 1 enabled + 1 disabled, both Exact match. |
| 14 | domain_filters | 0 | Empty by design. |
| 15 | image_training_samples | 0 | Empty by design (no payloads carried). |
| 16 | video_training_samples | 0 | Empty by design (no payloads carried). |
| 17 | web_notifications | 0 | Empty by design. |
| 18 | notification_preferences | 5 | One per active web user. |
| 19 | messages | 407 | 100 per slice: explicit_spam, implicit_spam, explicit_ham, implicit_ham. Plus 7 SimHash test anchors appended in 3A.3 (4666, 14538, 212355, 220848, 221429, 221904, 222949) — all banned-user spam from dev DB, preserved verbatim, FK-resolved against existing canonical telegram_users and MainChat. |
| 20 | chat_admins | 104 | Snapshot of admin membership across all 21 chats. |
| 21 | linked_channels | 3 | One per chat that has a linked channel. |
| 22 | telegram_user_mappings | 3 | Cross-chat user identity links. |
| 23 | profile_scan_results | 10 | Includes a mix of clean and flagged scans. |
| 24 | username_history | 4 | Rename trail for spam-rename-then-spam users. |
| 25 | admin_notes | 3 | Free-text rebuilt from sanitized telegram_users; rows 5+6 cross-reference each other's sanitized usernames. |
| 26 | audit_log | 100 | Connected-as / disconnected-as narrative anchored to canonical fixture identities. |
| 27 | user_tags | 12 | |
| 28 | welcome_responses | 11 | Deliberately trimmed from 293 (Pre-3A.7 audit): no test exercised the prod-derived volume. Kept: 5 synthetics 999001..999005 (one per WelcomeResponseType, anchors `WelcomeTimeoutJobTests`), 4 prod-derived MainChat anchors (ids 73/75/94/128) for AnalyticsRepositoryTests' status-distributed shape (3 Accepted + 1 Timeout + the synthetic Denied/Left filling out the 6 analytics windows), 2 non-MainChat keepers (ids 55/121) for chat-grouping shape diversity. |
| 29 | invites | 19 | |
| 30 | reports | 10 | `reviewed_by` mapped via deterministic hashtext to canonical fixture emails. |
| 31 | message_edits | 23 | Edit history for messages whose canonical row carries `edit_count > 0`. |
| 32 | detection_results | 376 | URL hostnames in `check_results_json` scrubbed to `canonical-spam.test`. `is_spam` is a generated column. |
| 33 | training_labels | 200 | 185 prod-derived + 15 synthetic explicit_ham promotions (`reason='canonical_synthetic_promotion'`). |
| 34 | user_actions | 993 | Bootstrap missed adding 7 synthetic ban-celebration anchor rows; see Part 2 ban-celebration note. |
| 35 | message_translations | 14 | Non-noop translations only; URL hostnames scrubbed. |

### What's NOT in the dataset
- **Encrypted JSONB credentials** in `configs` (sendgrid keys, web push keys, AI provider keys) - left NULL. Populated at runtime by the app via `IDataProtectionProvider`.
- **File payloads** referenced by `image_training_samples` / `video_training_samples` - even the metadata rows are empty (0 rows in canonical).
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
- **PII in spam messages:** phone numbers replaced with NANP-reserved `+15555550199` / `555-555-0199`; non-canonical emails replaced with `spam@canonical.test`. Not load-bearing for spam classifier features.
- **LLM prompt content** (`configs.welcome_config` + `prompt_versions`): minimized to 1 global synthetic baseline + 1 Main Chat customized variant + 1 Main Chat `prompt_versions` row. The other 18 per-chat configs have `welcome_config = NULL` (fall back to global) and `invite_link = NULL`.
- **`username_blacklist`:** trimmed to 2 rows (1 enabled-Exact + 1 disabled-Exact). Other match types (Contains/Regex/StartsWith) are not implemented in `BlacklistMatchType` / `UsernameBlacklistService.CheckDisplayNameAsync`; fixtures for those should be added when the feature ships.

### Schema reference
For column-level details, read the per-table SQL file directly (`head -1 <file>` shows the INSERT column list, then read a row or two). Do not transcribe column lists into this document.

### Legacy `GoldenDataset.cs` warning
The existing `TestData/GoldenDataset.cs` predates canonical. Its `Users.User{1..4}_Id` UUIDs match canonical (preserved as fixtures), but `TelegramUsers.*`, `ManagedChats.*`, `Messages.*`, `LinkedChannels.*`, `DetectionResults.*`, and `TrainingLabels.*` constants pin to **pre-rotation** IDs that DO NOT exist in canonical. Phase 4 retires that class. Until then, do not introduce new references to those constants; use direct canonical IDs (or new constants minted on demand against canonical) instead.

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
- Use when: a test needs an active elevated (cross-chat) admin who is NOT the Owner (permission boundary tests). Two additional active GlobalAdmins exist in canonical (`perfume@`, `machine@`) but only this one is recipe-exposed.

#### Admin: standard-permission fixture
- `User.Id` = `921637d5-0f65-4c66-b143-6f057dd06a1c`
- Email: `admin@example.com`, permission_level 0, status 1, TOTP enabled, invited by Owner
- Use when: a test needs an authenticated user with normal permissions (most authenticated-flow tests). One additional active Admin exists in canonical (`rerun@`) but only this one is recipe-exposed.

#### Deleted Admin: soft-delete fixture
- `User.Id` = `a8dc8371-afc5-4b61-9d71-d177f2dd9ddd`
- Email: `deleted@example.com`, status 3 (deleted), is_active false
- Use when: a test asserts on soft-delete behavior or filters out deleted users. One additional deleted Admin exists in canonical (`reshoot@`) but only this one is recipe-exposed.

#### Deleted GlobalAdmin: soft-deleted elevated fixture
- `User.Id` = `ba9ba542-3df6-4473-a820-578562780c57`
- Email: `globaladmin@example.com`, permission_level 1, status 3
- Use when: a test asserts that elevated-but-deleted users are still excluded.

### Telegram users

#### Top MainChat author (ham)
- `telegram_user_id` = `9921676191756`
- `@unhelpfulgrab`, "Squeak Degree", `is_banned=false`
- 24 messages in canonical, mostly in MainChat. Cross-referenced by `admin_notes` row 6 (notes them as a suspected duplicate of `9452657005278`).
- Use when: a test needs a real, prolific MainChat author. Closest canonical analog of legacy `GoldenDataset.TelegramUsers.User1_TelegramUserId`.

#### Second active MainChat author (ham)
- `telegram_user_id` = `9960171136314`
- `@sillywolf`, "Early Spirits", `is_banned=false`
- 23 messages. `admin_notes` row 4 references this user with a generic "Test Note".
- Use when: a test needs a paired second author for cross-author scenarios in MessageHistoryRepositoryTests. Closest canonical analog of legacy `User2_TelegramUserId`.

#### Suspected duplicate account (ham, cross-referenced)
- `telegram_user_id` = `9452657005278`
- `@strainermaroon`, "Obtrusive Impure", `is_banned=false`
- `admin_notes` row 5 ties this account to `@unhelpfulgrab` ("Same account as @unhelpfulgrab").
- Use when: a test needs an account with a non-trivial `admin_notes` narrative (free-text cross-reference to another canonical user).

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
- Use when: any test that historically pinned to `GoldenDataset.ManagedChats.MainChat_Id`. This is the de facto MainChat anchor.

#### Workshop Alumni: secondary chat for cross-chat tests
- `chat_id` = `-100059667856554`
- Second-most messages (39). 5 `chat_admins` members.
- Use when: a test needs a second active chat to pair against MainChat (`chatA` vs `chatB` patterns in MessageHistoryRepositoryTests). Closest canonical analog of legacy `Chat1_Id`.

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

#### Sample spam-labeled message (training_labels label=0)
- `message_id` = `4575`, `chat_id` = `-100048429560480`, `user_id` = `9331684387862`
- `training_labels.label = 0` (spam). The synthetic `id < 0` rows (e.g., `-3`, `-6`) are training-only placeholders without real `messages` rows; prefer real message ids when the test needs the underlying message body.
- Use when: a test needs a message that downstream training labels classify as spam.

#### Sample ham-labeled message (training_labels label=1)
- `message_id` = `103`, `chat_id` = `-100082190806505`, `user_id` = `9320215215920`
- `training_labels.label = 1` (ham).
- Use when: a test needs a message that downstream training labels classify as ham.

### Configs

#### Global config (chat_id = 0)
- `configs.id` = `1`, `chat_id` = `0`
- Carries the only non-NULL global `welcome_config` baseline. All encrypted JSONB columns NULL (DataProtection injection target).
- Use when: a test reads global fallback configuration or exercises the encrypted-column injection path.

#### Main Community per-chat config (overrides global)
- `configs.id` = `15`, `chat_id` = `-100026957614982`
- The only per-chat config with a non-NULL `welcome_config`. Mirrors the global baseline with one customized prompt variant.
- Use when: a test needs to verify per-chat override fallback behavior, or to exercise a chat that has a non-global welcome config.

#### Per-chat content_detection_configs (sample)
- `content_detection_configs.id` = `2`, `chat_id` = `0` (global baseline)
- 17 additional per-chat rows (one per active managed_chat).
- Use when: a test needs a representative content-detection config row.

### Synthetic / reserved rows (do not regenerate)
- `welcome_responses` IDs `999001..999005`: 5 status branches anchored on `(MainChat_Id=-100026957614982, user_id=9196379650113, username='canonical_user1')`. Mapping: `999001`=Pending, `999002`=Accepted, `999003`=Denied, `999004`=Timeout, `999005`=Left.
- `username_blacklist` IDs `999001` (`pattern='spambot_admin'`, enabled, Exact match) + `999005` (`pattern='archived_pattern'`, disabled, Exact match). No Contains/Regex/StartsWith fixtures (feature not yet implemented).
- `training_labels` rows with `reason='canonical_synthetic_promotion'`: 15 explicit_ham promotions. `labeled_by_user_id` is the rotated id of prod user `1312830442` (a stable canonical synthetic-promotion attribution anchor).
- `user_actions` ban-celebration anchors: **NOT yet added** as synthetic rows. `BanCelebrationServiceTests` `{bancount}` placeholder tests (1..7) currently have no dedicated synthetic anchors; the test rewrite in Phase 3A should either pick 7 canonical telegram_users with `>=1` Ban action_type=0 row (top candidates: `9971261287520`, `9793662571780`, `9319426004230`), or extend canonical with synthetic rows in a follow-up bootstrap pass.

### Cross-references
- **Auth password (all 9 web users):** `Passw0rd!SaidNoSecurityAuditorEver`. Hash baked into `01_users.sql`.
- **`chat_id = 0`:** preserved sentinel (rotation skipped via CASE WHEN guard). Global config row, global content_detection_config row, and global stop_words/etc. anchor here.

### Known canonical gaps (Pre-Phase 1b extensions that were NOT carried into the final dump)

If a Phase 3A test rewrite needs one of these, the option is (a) extend canonical with a follow-up bootstrap pass, or (b) seed inline in the test setup.

- **SimHash deduplication messages (IDs 95001..95022):** ✅ RESOLVED in 3A.3 by a different approach — canonical was extended with real prod near-duplicate clusters (banned-user spam campaigns) instead of the original 95001..95022 synthetic groups. The test now uses cluster `220848/221429/221904/222949` (recruitment-spam variants, all pairwise SimHash ≤ 10) for `SimHash_DetectsNearDuplicates_InRecruitmentSpamCluster`, anchor `212355` for the bit_count query test, and `20849/4666/221139/14538` for `SimHash_DistinguishesDifferentGroups`. Legacy `SQL.30_dedup_test_data.sql` may be retired in Phase 4 cleanup.
- **Analytics time-spread data:** `AnalyticsRepositoryTests.cs:78` aggregates over daily/weekly/monthly/7-day/30-day/365-day windows. Canonical does not pre-shift timestamps for this; tests still rely on legacy `SQL.50_analytics_test_data.sql` via `GoldenDataset.SeedAnalyticsDataAsync`. Phase 3A migration here will need either a UPDATE-on-load pass or a `Reduce` plan that injects time-shifted rows.
- **Ban-celebration `{bancount}` anchors:** see Synthetic / reserved rows note above.
- **Welcome-response slice anchor pinned to a specific welcome_message_id constant:** ✅ RESOLVED in 3A.4 — `WelcomeTimeoutJobTests` was retargeted at canonical user `9196379650113` and welcome_message_ids `99001..99005` (one per `WelcomeResponseType`). Per-test template clones give each test a fresh canonical 999001 Pending anchor, so mutation-as-assertion tests (kick path → Timeout) don't pollute one another. The "near-miss" tests (different chat / different message id) use the existing Pending row in MainChat as the deliberate near-miss instead of seeding a synthetic row. welcome_message_ids 99001..99005 confirmed not to collide with any `messages.message_id` in canonical.

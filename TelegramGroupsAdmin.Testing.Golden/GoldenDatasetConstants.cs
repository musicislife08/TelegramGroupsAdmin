namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>
/// Canonical constants — anchor IDs, expected counts, mutation offsets — for tests
/// that exercise canonical-loaded substrate. Single source of truth: when a test
/// pins to a canonical row by id, the literal goes here. Magic-string ids in tests
/// are a code smell — file a follow-up to migrate them, don't write new ones.
///
/// Organized by domain:
///   • <see cref="WebUsers"/>   — web_users.id fixtures (UUIDs)
///   • <see cref="Chats"/>      — managed_chats.id anchors
///   • <see cref="Retention"/>  — anchors for retention-cleanup tests
///   • <see cref="Analytics"/>  — anchors for analytics-aggregation tests
///   • <see cref="Reports"/>    — anchors for exam-result repository tests
///   • <see cref="Verdicts"/>   — verdict-event anchors (message_verdicts view, training levels)
///   • <see cref="ModerationLog"/> — user_actions anchors for the Audit Log page's moderation filters
///   • <see cref="SystemConfig"/> — global configs row: encrypted-column plaintexts written post-load
///
/// Promote a constant up to a top-level domain class (e.g. <see cref="WebUsers"/>,
/// <see cref="Chats"/>) once a second consumer wants it; until then, keep it next
/// to the tests that use it under a domain nested class.
/// </summary>
public static class GoldenDatasetConstants
{
    /// <summary>
    /// Web user fixtures from <c>canonical/01_users.sql</c>. UUIDs are stable
    /// fixtures preserved verbatim across canonical regenerations — they're the
    /// 9 canonical web users, all pinned (5 active login anchors, 1 active GlobalAdmin carrying
    /// a stored TOTP secret, 2 soft-deleted, 1 disabled). Password for all 9 canonical web users:
    /// <see cref="Password"/> (every row carries the same PBKDF2 hash, equal to the E2E
    /// <c>PrehashedTestCredentials.StandardPasswordHash</c>).
    /// </summary>
    public static class WebUsers
    {
        /// <summary>
        /// Owner fixture (owner@example.com, permission_level=2, status=1, TOTP enabled).
        /// Highest-privilege web user — use when a test needs system-administration
        /// scope or wants to satisfy a created_by/owner FK without raw INSERT.
        /// </summary>
        public const string OwnerId = "b388ee38-0ed3-4c09-9def-5715f9f07f56";

        /// <summary>Owner fixture email — paired with <see cref="OwnerId"/>.</summary>
        public const string OwnerEmail = "owner@example.com";

        /// <summary>
        /// Admin fixture (admin@example.com, permission_level=0, status=1, TOTP enabled,
        /// invited_by=Owner). Standard-permission authenticated user — use for most
        /// authenticated-flow tests that don't need elevated permissions.
        /// </summary>
        public const string AdminId = "921637d5-0f65-4c66-b143-6f057dd06a1c";

        /// <summary>Admin fixture email — paired with <see cref="AdminId"/>.</summary>
        public const string AdminEmail = "admin@example.com";

        /// <summary>
        /// GlobalAdmin fixture (ahead@canonical.test, permission_level=1, status=1, TOTP enabled
        /// with no stored secret). Use for UI password login as an elevated non-Owner user.
        /// </summary>
        public const string GlobalAdminId = "8e3a7211-d0eb-40c6-af8e-7d15bb42d10a";

        /// <summary>GlobalAdmin fixture email — paired with <see cref="GlobalAdminId"/>.</summary>
        public const string GlobalAdminEmail = "ahead@canonical.test";

        /// <summary>
        /// GlobalAdmin without TOTP (machine@canonical.test, permission_level=1, status=1,
        /// TOTP disabled). Use for UI password login with no TOTP prompt as an elevated user.
        /// </summary>
        public const string NoTotpGlobalAdminId = "c2674f3a-16e6-4537-9cbc-a80a0ea9c686";

        /// <summary>No-TOTP GlobalAdmin email — paired with <see cref="NoTotpGlobalAdminId"/>.</summary>
        public const string NoTotpGlobalAdminEmail = "machine@canonical.test";

        /// <summary>
        /// Admin without TOTP (reshoot@canonical.test, permission_level=0, status=1, TOTP
        /// disabled). Use for UI password login with no TOTP prompt as a standard-permission user.
        /// </summary>
        public const string NoTotpAdminId = "28d7aa41-5be5-43a3-a48e-7b1a4bbe5891";

        /// <summary>No-TOTP Admin email — paired with <see cref="NoTotpAdminId"/>.</summary>
        public const string NoTotpAdminEmail = "reshoot@canonical.test";

        /// <summary>Security stamp carried by every canonical web user.</summary>
        public const string SecurityStamp = "TEST_SECURITY_STAMP";

        /// <summary>Plaintext password for every canonical web user.</summary>
        public const string Password = "Passw0rd!SaidNoSecurityAuditorEver";

        /// <summary>
        /// Deleted Admin fixture (deleted@example.com, status=3, is_active=false,
        /// invited_by=Owner). Use when a test needs a soft-deleted user — e.g. asserting
        /// that deleted users are filtered out of active queries, or that backup/restore
        /// preserves <see cref="DeletedAdminStatus"/>.
        /// </summary>
        public const string DeletedAdminId = "a8dc8371-afc5-4b61-9d71-d177f2dd9ddd";

        /// <summary>Deleted Admin fixture email — paired with <see cref="DeletedAdminId"/>.</summary>
        public const string DeletedAdminEmail = "deleted@example.com";

        /// <summary>
        /// Status value carried by the Deleted Admin fixture (UserStatus.Deleted = 3).
        /// Pair with <see cref="DeletedAdminId"/> when verifying canonical persists the
        /// soft-deleted state through round-trips.
        /// </summary>
        public const int DeletedAdminStatus = 3;

        /// <summary>
        /// Disabled Admin fixture (rerun@canonical.test, permission_level=0, status=2, is_active=false,
        /// TOTP disabled, invited by Owner; canonical edit 2026-10-01: the Owner disabled this account
        /// twelve minutes after creating it, instead of deleting it — previously status 3). Use when a
        /// test needs a web user in the Disabled state (the Enable action, the default status filter).
        /// Tests that rely on it read the status back first.
        /// </summary>
        public const string DisabledAdminId = "6a66f0f6-6e59-45ac-ac5f-51a2df0c9c58";

        /// <summary>Disabled Admin fixture email — paired with <see cref="DisabledAdminId"/>.</summary>
        public const string DisabledAdminEmail = "rerun@canonical.test";

        /// <summary>Status value carried by the Disabled Admin fixture (UserStatus.Disabled = 2).</summary>
        public const int DisabledAdminStatus = 2;

        /// <summary>
        /// GlobalAdmin with a stored TOTP secret (perfume@canonical.test, permission_level=1, status=1,
        /// TOTP enabled; canonical edit 2026-10-01). The only canonical web user whose
        /// <c>users.totp_secret</c> is non-NULL: the plaintext lives in
        /// <c>canonical/01_users.totp_secrets.json</c> and <c>GoldenDataset.LoadCanonicalAsync</c> protects it
        /// at load with <c>DataProtectionPurposes.TotpSecrets</c>. Use for the Owner's Reset TOTP action
        /// (the menu item renders only with a stored secret) or a real authenticator login via the secret.
        /// Not a UI-login anchor — owner@/admin@/ahead@ keep no secret and land on /login/setup-2fa.
        /// </summary>
        public const string StoredTotpGlobalAdminId = "f2f2f5c2-2cd2-45a1-a272-83f59076fb40";

        /// <summary>TOTP-secret GlobalAdmin email — paired with <see cref="StoredTotpGlobalAdminId"/>.</summary>
        public const string StoredTotpGlobalAdminEmail = "perfume@canonical.test";

        /// <summary>
        /// Base32 plaintext of <see cref="StoredTotpGlobalAdminId"/>'s stored TOTP secret, mirroring
        /// <c>canonical/01_users.totp_secrets.json</c> (a dummy 20-byte test secret; it protects nothing).
        /// </summary>
        public const string StoredTotpGlobalAdminBase32 = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

        /// <summary>
        /// Number of recovery codes <see cref="StoredTotpGlobalAdminId"/> holds in
        /// <c>canonical/10_recovery_codes.sql</c> (canonical addition 2026-10-02): one unused set of
        /// <c>AuthenticationConstants.RecoveryCodeCount</c>, as completing TOTP setup issues. No other
        /// canonical web user has recovery codes.
        /// </summary>
        public const int StoredTotpGlobalAdminRecoveryCodeCount = 8;

        /// <summary>
        /// Plaintext of one of <see cref="StoredTotpGlobalAdminId"/>'s unused recovery codes (row id 1). The
        /// SQL stores only its hash: SHA-256 of the lowercase code, Base64-encoded, as <c>TotpService</c>
        /// hashes it. A dummy code; it protects nothing.
        /// </summary>
        public const string StoredTotpGlobalAdminRecoveryCode = "7aa70a3ce3b5f515";
    }

    /// <summary>
    /// Managed chat anchors from <c>canonical/03_managed_chats.sql</c>.
    /// </summary>
    public static class Chats
    {
        /// <summary>
        /// MainChat — the de facto canonical primary group. Holds 198/400 canonical
        /// messages, the only non-NULL <c>welcome_config</c> outside the global row,
        /// the only non-NULL <c>prompt_versions</c> row, and a <c>linked_channels</c> row.
        /// </summary>
        public const long MainChatId = -100026957614982L;

        /// <summary>
        /// Land Owners Group — non-MainChat with substantive message volume and a global
        /// welcome flow (per CLAUDE.md Part 2 recipe). Hosts most of the canonical
        /// unlabeled-message anchors used by training-label tests.
        /// </summary>
        public const long LandOwnersChatId = -100017312732389L;

        /// <summary>
        /// Unnamed canonical chat that hosts the existing spam/ham training-label fixtures
        /// (messages 4575, 4602, 4655, 4620). Co-located so a single chat anchor lets
        /// tests pin spam+ham labels without crossing chat boundaries.
        /// </summary>
        public const long TrainingFixturesChatId = -100048429560480L;
    }

    /// <summary>
    /// Content detection config anchors from <c>canonical/05_content_detection_configs.sql</c>.
    /// </summary>
    public static class ContentDetectionConfigs
    {
        /// <summary>
        /// The global config row (<c>chat_id = 0</c>). Its stored JSON predates the
        /// <c>HamSkipThreshold</c> image/video settings (the key is absent) and carries a non-default
        /// <c>ImageSpam.OcrConfidenceThreshold</c> of 75, which proves a read came from the row.
        /// </summary>
        public const long GlobalRowId = 2;

        /// <summary>The stored (non-default) <c>ImageSpam.OcrConfidenceThreshold</c> of <see cref="GlobalRowId"/>.</summary>
        public const double GlobalImageOcrConfidenceThreshold = 75;
    }

    /// <summary>
    /// Telegram user anchors from <c>canonical/02_telegram_users.sql</c>. Each constant
    /// pins a specific role the test suite relies on (top author, second author,
    /// labeling actor). Identity boundary: all IDs land in
    /// <c>[9_000_000_000_000, 10_000_000_000_000)</c> per the canonical rotation salt.
    /// </summary>
    public static class TelegramUsers
    {
        /// <summary>
        /// Top MainChat ham author (@unhelpfulgrab, "Squeak Degree"). 24 canonical messages,
        /// mostly in MainChat. Per CLAUDE.md Part 2 recipe.
        /// </summary>
        public const long TopMainChatHamAuthorId = 9921676191756L;

        /// <summary>
        /// Second active MainChat ham author (@sillywolf, "Early Spirits"). 23 canonical
        /// messages. Paired with <see cref="TopMainChatHamAuthorId"/> for cross-author
        /// scenarios. Per CLAUDE.md Part 2 recipe.
        /// </summary>
        public const long SecondMainChatHamAuthorId = 9960171136314L;
    }

    /// <summary>
    /// Anchors for the Users page tab tests (<c>TelegramUserRepositoryTests</c>, All / Trusted
    /// filters). All three are welcome-timeout kicked joiners in <see cref="Chats.MainChatId"/>
    /// with zero messages.
    /// Two rows are edited in canonical (2026-09-13) to carry shapes real data never keeps
    /// long enough to snapshot; see the per-constant notes.
    /// </summary>
    public static class UsersPage
    {
        /// <summary>@luminanceflagstick — is_active=false, is_banned=false, is_trusted=false. Untouched canonical row.</summary>
        public const long KickedJoinerId = 9171379870502L;

        /// <summary>Username of <see cref="KickedJoinerId"/>; unique across canonical usernames, first names, and username_history.</summary>
        public const string KickedJoinerUsername = "luminanceflagstick";

        /// <summary>@tadpolesleek — is_active=false, is_trusted=true (canonical edit: trusted after a timeout kick).</summary>
        public const long TrustedKickedJoinerId = 9301917046112L;

        /// <summary>@curveabdominal — is_active=false, is_banned=true with ban_expires_at in the past (canonical edit: expired temp-ban whose flag was never cleared).</summary>
        public const long ExpiredBanUserId = 9995544961449L;

        /// <summary>
        /// Untouched canonical member: is_active=true, is_banned=false, is_trusted=false, not a bot,
        /// no chat_admins row, 2 messages in one chat. The subject of the Users page trust toggle
        /// (<c>UsersGoldenTests</c>): trusting it is the assertion, not setup.
        /// </summary>
        public const long UntrustedActiveMemberId = 9704788798695L;

        /// <summary>
        /// Untouched canonical member: is_active=true, is_trusted=true, not a bot, no active chat_admins
        /// row, one <c>warnings</c> entry whose ExpiresAt (2026-04-26) is in the past. Carries the Trusted
        /// badge without the Chat admin badge; re-timed by <c>ExtendTelegramUserWarnings</c> when a test
        /// needs the warning in force.
        /// </summary>
        public const long WarnedTrustedMemberId = 9685233957282L;

        /// <summary>
        /// Untouched canonical member: is_active=true, is_trusted=true, no active chat_admins row, one
        /// <c>warnings</c> entry whose ExpiresAt (2026-04-28) is in the past. Left expired so the Users
        /// page's active-warning predicate is proven (Active renders "None"; Tagged does not list it).
        /// </summary>
        public const long ExpiredWarningTrustedMemberId = 9086323729821L;

        /// <summary>
        /// Untouched canonical member: is_active=true, is_trusted=true, not a bot, is_active=true on
        /// 4 chat_admins rows (admin in four managed chats). Carries both the Chat admin and the
        /// Trusted badge; every non-bot active canonical admin is also trusted.
        /// </summary>
        public const long ChatAdminMemberId = 9187417286258L;
    }

    /// <summary>
    /// Anchors from <c>canonical/27_user_tags.sql</c> (canonical edit 2026-10-01).
    /// </summary>
    public static class UserTags
    {
        /// <summary>
        /// @parasailprojector — active, trusted, not banned, no admin note, no chat_admins row. Its only
        /// tag (<see cref="RemovedTagId"/>, "helpful-user") is removed, so it is Tagged by nothing live:
        /// stats and counts that ignore <c>removed_at</c> would wrongly flag it.
        /// </summary>
        public const long RemovedTagUserId = 9579510369392L;

        /// <summary>
        /// <c>user_tags.id</c> 11 — canonical edit: <c>removed_at</c> set (2025-11-15 17:20 UTC, after its
        /// 2025-10-28 added_at) and <c>removed_by_web_user_id</c> the Owner; the tag was later withdrawn.
        /// </summary>
        public const long RemovedTagId = 11L;
    }

    /// <summary>
    /// DM ban celebration subscriber anchors from <c>canonical/36_ban_celebration_subscribers.sql</c>
    /// (canonical addition 2026-09-25 — a new table has no row to flag-edit; approved by owner).
    /// Every user is active, not banned, not a bot, and has real messages in the chats they are
    /// subscribed to.
    /// </summary>
    public static class DmCelebrations
    {
        /// <summary>Workshop Alumni — hosts three of the four subscriber rows.</summary>
        public const long WorkshopAlumniChatId = -100059667856554L;

        /// <summary>Poultry Community — second chat for the two-chat subscriber.</summary>
        public const long PoultryCommunityChatId = -100017608907459L;

        /// <summary>@magnetismvoucher — bot_dm_enabled=true; subscribed to Workshop Alumni. The deliverable subscriber.</summary>
        public const long DeliverableSubscriberId = 9183753414221L;

        /// <summary>@thudupper — bot_dm_enabled=false; subscribed to Workshop Alumni with a stale open prompt (never started the bot, prompt timed out).</summary>
        public const long UndeliverableSubscriberId = 9011393194616L;

        /// <summary>@deepnessunmapped — bot_dm_enabled=false; subscribed to Workshop Alumni and Poultry Community.</summary>
        public const long TwoChatSubscriberId = 9689750659830L;

        /// <summary>@chummyrepair — bot_dm_enabled=true; MainChat member with no subscription row.</summary>
        public const long UnsubscribedMemberId = 9306234060091L;

        /// <summary>
        /// @ToniBaronePaul — is_banned=true, bot_dm_enabled=true; subscribed to Workshop Alumni (canonical
        /// addition 2026-09-25). A subscription row that outlived its owner's ban, i.e. the ban-time removal
        /// did not run. Pins that "deliverable" excludes banned users regardless of the row surviving.
        /// </summary>
        public const long BannedSubscriberId = 9782251136844L;

        /// <summary>Stale prompt message id on <see cref="UndeliverableSubscriberId"/>'s Workshop Alumni row.</summary>
        public const int StalePromptMessageId = 424242;

        /// <summary>Stale prompt delete-job id on <see cref="UndeliverableSubscriberId"/>'s Workshop Alumni row.</summary>
        public const string StalePromptJobId = "canonical-stale-prompt-job";
    }

    /// <summary>
    /// Canonical IDs and target NOW()-relative offsets used by
    /// <c>MessageHistoryRepositoryTests.CleanupExpiredAsync_WithOldMessages_*</c>
    /// to shape the substrate for retention-cleanup testing. All anchors are in
    /// <see cref="Chats.MainChatId"/>.
    ///
    /// Test setup applies: <c>Reduce.KeepMessages(AllMessageRefs)</c> to isolate the
    /// 6 anchor messages, then <c>Mutate.ShiftMessageTimestamps(...)</c> to re-time
    /// the surviving rows into the retention windows the SUT cares about.
    /// </summary>
    public static class Retention
    {
        // ── Anchor 1: bare message with attached edit ──
        // 1 message_edits row (id 337). detection_results row 3267 (folded training label,
        // canonical edit 2026-09-27) → ExplicitHam, a curated classification. Shifted to
        // -45d → PRESERVED (with its edit) despite age, since its verdict is curated.
        public const int MsgId_BareWithEdit = 212340;
        public const long EditId_ForBareWithEdit = 337L;

        // ── Anchor 2: bare orphan ──
        // 0 detection_results, no edits. Shifted to -60d → DELETED.
        public const int MsgId_BareOrphan60d = 212694;

        // ── Anchor 3: training-flagged message (preserved despite age) ──
        // 2 detection_results: dr2842 (ContentScan, ImplicitSpam) and the later
        // dr3286 (AutoBan, ExplicitSpam). dr3286 is newer (detected_at DESC) so it
        // is the current verdict → ExplicitSpam, a curated classification. Shifted to
        // -90d → PRESERVED.
        public const int MsgId_TrainingPreserved = 218579;

        // ── Anchor 4: bare orphan just past retention threshold ──
        // 0 detection_results, no edits. Shifted to -35d → DELETED.
        public const int MsgId_BareOrphan35d = 212803;

        // ── Anchor 5: bare orphan just inside retention boundary ──
        // 0 detection_results, no edits. Shifted to -29d → PRESERVED.
        public const int MsgId_BareOrphan29d = 213117;

        // ── Anchor 6: non-training detection, curated classification ──
        // 1 detection_result (id 3033, LegacyManual) with classification=ExplicitSpam
        // (a curated value). Shifted to -50d → PRESERVED despite age, since its verdict is curated.
        public const int MsgId_NonTrainingDeleted = 220885;

        /// <summary>
        /// Standalone anchor (not part of <see cref="AllMessageRefs"/>/<see cref="MessageShifts"/> —
        /// used against the unreduced canonical dataset, like <c>Verdicts.LabeledOnlyRetentionMsgId</c>)
        /// covering the message_edits cascade-delete path (task #548 review finding): a message whose
        /// current verdict is non-curated (Unscanned — no detection_results rows at all) and which
        /// carries an edit, so deleting it must also delete the edit. In <see cref="Chats.MainChatId"/>;
        /// message_edits row id 3014. Timestamped 2026-04-09, already well past a 30-day window
        /// relative to "now" — no shift needed. DELETED along with its edit.
        /// </summary>
        public const int MsgId_ExpiredWithEdits = 221932;
        public const long EditId_ForExpiredWithEdits = 3014L;

        /// <summary>
        /// All 6 (chat_id, message_id) tuples passed to <c>Reduce.KeepMessages(...)</c>.
        /// FK CASCADE drops every other canonical message's detection_results,
        /// edits, and translations.
        /// </summary>
        public static readonly IReadOnlyList<(long ChatId, long MessageId)> AllMessageRefs =
        [
            (Chats.MainChatId, MsgId_BareWithEdit),
            (Chats.MainChatId, MsgId_BareOrphan60d),
            (Chats.MainChatId, MsgId_TrainingPreserved),
            (Chats.MainChatId, MsgId_BareOrphan35d),
            (Chats.MainChatId, MsgId_BareOrphan29d),
            (Chats.MainChatId, MsgId_NonTrainingDeleted),
        ];

        /// <summary>
        /// All 6 message timestamp shifts, midnight-anchored via
        /// <c>date_trunc('day', NOW()) + Offset</c>. Offsets are negative because we
        /// re-time canonical messages into the *past* relative to the test's NOW().
        /// </summary>
        public static readonly IReadOnlyList<TimestampShift> MessageShifts =
        [
            new(MsgId_BareWithEdit,        TimeSpan.FromDays(-45)),
            new(MsgId_BareOrphan60d,       TimeSpan.FromDays(-60)),
            new(MsgId_TrainingPreserved,   TimeSpan.FromDays(-90)),
            new(MsgId_BareOrphan35d,       TimeSpan.FromDays(-35)),
            new(MsgId_BareOrphan29d,       TimeSpan.FromDays(-29)),
            new(MsgId_NonTrainingDeleted,  TimeSpan.FromDays(-50)),
        ];

        /// <summary>
        /// Expected DeletedCount when CleanupExpiredAsync is called with 30-day retention:
        /// anchors 2 and 4 (60d and 35d bare orphans; Unscanned, not curated). Anchor 3
        /// (training, curated) and 5 (boundary) are preserved as before. Anchors 1 and 6
        /// were 4 before task #548's curated-verdict keep condition (folded/edited canonical
        /// rows on 212340 and 220885 both resolve to a curated classification via
        /// message_verdicts — ExplicitHam and ExplicitSpam respectively) — they are now
        /// preserved instead of deleted, so the count dropped from 4 to 2.
        /// </summary>
        public const int ExpectedDeletionsWith30DayRetention = 2;
    }

    /// <summary>
    /// Canonical IDs and target NOW()-relative offsets used by
    /// <c>AnalyticsRepositoryTests</c> to shape the substrate for time-window
    /// aggregation tests. All anchors are in <see cref="Chats.MainChatId"/>.
    ///
    /// Test setup applies: <c>Reduce.KeepMessages(AllMessageRefs)</c> to isolate the
    /// 9 anchor messages, then <c>Mutate.ShiftDetectionResultTimestamps(...)</c> +
    /// <c>ShiftWelcomeResponseTimestamps(...)</c> to re-time the surviving rows into
    /// today/yesterday/last-week buckets.
    /// </summary>
    public static class Analytics
    {
        // ── Spam-only detection_result anchors (single auto-S DR per message) ──
        // All 7 picks have real ProcessingTimeMs > 0 in check_results_json so the
        // algorithm-performance test sees honest timing data.

        public const long DrId_TodaySpam1 = 2952;        // msg 220017, score=5.0, ImplicitSpam
        public const long DrId_TodaySpam2 = 2955;        // msg 220093, score=4.6, UntrainedSpam
        public const long DrId_TodaySpam3 = 2959;        // msg 220224, score=4.8, ImplicitSpam
        public const long DrId_YesterdaySpam1 = 2998;    // msg 220364, score=4.9, ImplicitSpam
        public const long DrId_YesterdaySpam2 = 3055;    // msg 221125, score=4.6, UntrainedSpam
        public const long DrId_LastWeekSpam1 = 3119;     // msg 221604, score=4.3, UntrainedSpam
        public const long DrId_LastWeekSpam2 = 3221;     // msg 222793, score=4.8, ImplicitSpam

        public const long MsgId_TodaySpam1 = 220017;
        public const long MsgId_TodaySpam2 = 220093;
        public const long MsgId_TodaySpam3 = 220224;
        public const long MsgId_YesterdaySpam1 = 220364;
        public const long MsgId_YesterdaySpam2 = 221125;
        public const long MsgId_LastWeekSpam1 = 221604;
        public const long MsgId_LastWeekSpam2 = 222793;

        // ── FP pair on msg 213325 (organic auto-spam + manual-ham correction) ──
        public const long MsgId_FalsePositive = 213325;
        public const long DrId_FpAuto = 2012;            // score=5.5, auto-spam (ImplicitSpam)
        public const long DrId_FpManual = 2013;          // manual-ham ExplicitHam (later timestamp)

        // ── FN pair on msg 211184 (organic auto-ham + manual-spam correction) ──
        public const long MsgId_FalseNegative = 211184;
        public const long DrId_FnAuto = 1492;            // score=0, auto-ham (ImplicitHam)
        public const long DrId_FnManual = 1494;          // score=5, manual-spam ExplicitSpam (later timestamp)

        // ── Welcome response anchors in MainChat (3 Accepted + 1 Denied + 1 Timeout + 1 Left) ──
        public const long WrId_TodayAccepted1 = 73;       // Accepted, prod-derived
        public const long WrId_TodayAccepted2 = 75;       // Accepted, prod-derived
        public const long WrId_TodayDenied = 999003;      // Denied, synthetic (only Denied in MainChat)
        public const long WrId_YesterdayTimeout = 128;    // Timeout, prod-derived
        public const long WrId_YesterdayLeft = 999005;    // Left, synthetic (only Left in MainChat)
        public const long WrId_LastWeekAccepted = 94;     // Accepted, prod-derived

        /// <summary>
        /// All 9 (chat_id, message_id) tuples passed to <c>Reduce.KeepMessages(...)</c>.
        /// FK CASCADE drops every other canonical message's detection_results,
        /// edits, and translations.
        /// </summary>
        public static readonly IReadOnlyList<(long ChatId, long MessageId)> AllMessageRefs =
        [
            (Chats.MainChatId, MsgId_TodaySpam1),
            (Chats.MainChatId, MsgId_TodaySpam2),
            (Chats.MainChatId, MsgId_TodaySpam3),
            (Chats.MainChatId, MsgId_YesterdaySpam1),
            (Chats.MainChatId, MsgId_YesterdaySpam2),
            (Chats.MainChatId, MsgId_LastWeekSpam1),
            (Chats.MainChatId, MsgId_LastWeekSpam2),
            (Chats.MainChatId, MsgId_FalsePositive),
            (Chats.MainChatId, MsgId_FalseNegative),
        ];

        /// <summary>
        /// All 11 detection_result shifts (7 spam-only + 4 FP/FN pair rows). Offsets
        /// are midnight-anchored (see <see cref="TimestampShift"/>), so they land in
        /// the right calendar bucket regardless of what time of day the test runs.
        /// Manual correction rows are timed AFTER their corresponding auto row so the
        /// detection_accuracy view's "latest manual correction per message" CTE
        /// resolves correctly.
        /// </summary>
        public static readonly IReadOnlyList<TimestampShift> DetectionResultShifts =
        [
            // FP pair — auto at 00:01, manual at 00:02 (both in today's calendar day,
            // manual strictly after auto for the view's DISTINCT ON ordering).
            new(DrId_FpAuto,         TimeSpan.FromMinutes(1)),
            new(DrId_FpManual,       TimeSpan.FromMinutes(2)),
            // FN pair — auto at 00:03, manual at 00:04 (same ordering).
            new(DrId_FnAuto,         TimeSpan.FromMinutes(3)),
            new(DrId_FnManual,       TimeSpan.FromMinutes(4)),
            // Today spam — 5/6/7 minutes past midnight, all in today's calendar day.
            new(DrId_TodaySpam1,     TimeSpan.FromMinutes(5)),
            new(DrId_TodaySpam2,     TimeSpan.FromMinutes(6)),
            new(DrId_TodaySpam3,     TimeSpan.FromMinutes(7)),
            // Yesterday spam — yesterday at noon and 10am (always yesterday's calendar day).
            new(DrId_YesterdaySpam1, TimeSpan.FromHours(-12)),
            new(DrId_YesterdaySpam2, TimeSpan.FromHours(-14)),
            // Last week spam — 7 and 8 days ago at noon. SUT's "last week" is rolling
            // (today-7 to today-13), so both land safely in last-week bucket.
            new(DrId_LastWeekSpam1,  TimeSpan.FromDays(-7) + TimeSpan.FromHours(12)),
            new(DrId_LastWeekSpam2,  TimeSpan.FromDays(-8) + TimeSpan.FromHours(12)),
        ];

        /// <summary>
        /// All 6 welcome_response shifts. Today: 2 Accepted + 1 Denied; Yesterday:
        /// 1 Timeout + 1 Left; Last week: 1 Accepted. Same midnight-anchored offsets
        /// as <see cref="DetectionResultShifts"/>.
        /// </summary>
        public static readonly IReadOnlyList<TimestampShift> WelcomeResponseShifts =
        [
            new(WrId_TodayAccepted1,   TimeSpan.FromMinutes(5)),
            new(WrId_TodayAccepted2,   TimeSpan.FromMinutes(6)),
            new(WrId_TodayDenied,      TimeSpan.FromMinutes(7)),
            new(WrId_YesterdayTimeout, TimeSpan.FromHours(-12)),
            new(WrId_YesterdayLeft,    TimeSpan.FromHours(-14)),
            new(WrId_LastWeekAccepted, TimeSpan.FromDays(-7) + TimeSpan.FromHours(12)),
        ];

        // ── Expected count constants ──
        public const int TodaySpamCount = 3;
        public const int YesterdaySpamCount = 2;
        public const int LastWeekSpamCount = 2;

        /// <summary>
        /// Automated ham detections in the 7-day window — the FN pair's auto row
        /// (DrId 1492, ImplicitHam) which the manual correction later flags as a
        /// false negative. Counts toward DetectionAccuracyStats.TotalDetections.
        /// </summary>
        public const int InWindowHamAutoCount = 1;
        public const int TodayAcceptedCount = 2;
        public const int TodayDeniedCount = 1;
        public const int YesterdayTimeoutCount = 1;
        public const int YesterdayLeftCount = 1;
        public const int LastWeekAcceptedCount = 1;
        public const int TotalWelcomeResponses = 6;
        public const double ExpectedAcceptedPercentage = 50.0;          // 3/6 * 100
        public const double ExpectedDeniedPercentage = 100.0 / 6.0;     // ~16.67%
        public const double ExpectedTimeoutPercentage = 100.0 / 6.0;
        public const double ExpectedLeftPercentage = 100.0 / 6.0;
    }

    /// <summary>
    /// Report anchors from <c>canonical/30_reports.sql</c> used by exam-result tests.
    /// </summary>
    public static class Reports
    {
        /// <summary>
        /// Synthetic pending moderation (content) report: status=0, type=0, reported_by 'Auto-Detection',
        /// pointing at the real canonical message (70989, chat -100054416618415) authored by
        /// <see cref="PendingFixturesTelegramUserId"/>. The only pending moderation report in canonical —
        /// the Reports page's Warn/Spam/Ban/Dismiss actions resolve this row.
        /// </summary>
        public const long PendingModerationReportId = 186;

        /// <summary>
        /// Telegram user behind the three synthetic pending report fixtures (186..188): author of the
        /// message <see cref="PendingModerationReportId"/> points at and the subject of
        /// <see cref="PendingExamFailureId"/>. Has a first name (no username), so report cards render
        /// its "ID: …" caption.
        /// </summary>
        public const long PendingFixturesTelegramUserId = 9465377455871;

        /// <summary>Synthetic pending exam failure (status=0, user 9465377455871, chat -100054416618415).</summary>
        public const long PendingExamFailureId = 187;

        /// <summary>
        /// Synthetic pending profile-scan alert (status=0, type=3) for <see cref="PendingFixturesTelegramUserId"/>
        /// in chat -100048429560480 — the one pending profile-scan alert in canonical.
        /// </summary>
        public const long PendingProfileScanAlertId = 188;

        /// <summary>Real resolved exam failure (status=1, action_taken='approve', reviewed by globaladmin).</summary>
        public const long ResolvedExamFailureId = 185;

        /// <summary>
        /// Real resolved profile-scan alert (status=1, type=3, outcome Banned) whose context carries a
        /// string-array aiSignals (canonical edit 2026-10-01). The malformed-context repository tests
        /// corrupt this row's context as their subject.
        /// </summary>
        public const long ResolvedProfileScanAlertId = 177;

        /// <summary>Synthetic auto-approved exam pass (status=1, reviewed_by='Exam Flow', action_taken='auto-approved', outcome=1) in MainChat.</summary>
        public const long AutoApprovedExamPassId = 189;

        /// <summary>telegram_user_id behind <see cref="AutoApprovedExamPassId"/> (@sillywolf, ham).</summary>
        public const long AutoApprovedExamPassUserId = 9960171136314;
    }

    /// <summary>
    /// Verdict-event anchors (canonical edit 2026-09-27). See IntegrationTests/CLAUDE.md Part 2
    /// "Verdict events". Edited rows are guarded by read-back assertions in their tests.
    /// </summary>
    public static class Verdicts
    {
        /// <summary>Scan, then manual ham correction (LegacyManual) → ExplicitHam. @dinnersnazzy, MainChat.</summary>
        public const int CorrectedToHamMsgId = 213409;

        /// <summary>Auto-ban decision (migrated label, no user) → ExplicitSpam. @AndrewLong6, MainChat.</summary>
        public const int AutoBanMsgId = 220384;

        /// <summary>Auto-banned message on Land Owners used as the Mark as Ham subject (AutoBan → ExplicitSpam).</summary>
        public const int MarkAsHamSubjectMsgId = 8646;

        /// <summary>
        /// Edited: ham-labeled, then edited into spam (dr 1334 re-scan wins; label re-timed before the edit)
        /// → ImplicitSpam. @financerope (9468093502025), 5 edits.
        /// </summary>
        public const int EditFlipMsgId = 82837;

        /// <summary>Chat of <see cref="EditFlipMsgId"/>.</summary>
        public const long EditFlipChatId = -100065252085265L;

        /// <summary>Edited: UntrainedSpam scan (dr 1339), ham label removed. @mouthsafeguard's latest of three messages (Land Owners).</summary>
        public const int SpamInTrustWindowMsgId = 7796;

        /// <summary>@mouthsafeguard.</summary>
        public const long SpamInTrustWindowUserId = 9917295586642L;

        /// <summary>
        /// Edited: newest row is a clean FileScan (dr 2535) that the view must ignore; the verdict comes
        /// from ContentScan dr 2534 (UntrainedSpam). Spam label removed. MainChat.
        /// </summary>
        public const int FileScanBesideScanMsgId = 216684;

        /// <summary>The FileScan row of <see cref="FileScanBesideScanMsgId"/>.</summary>
        public const long FileScanRowId = 2535;

        /// <summary>Chat of <see cref="FileScanBesideScanMsgId"/> (MainChat).</summary>
        public const long FileScanBesideScanChatId = -100026957614982L;

        /// <summary>The ContentScan row (dr2534, UntrainedSpam) that is <see cref="FileScanBesideScanMsgId"/>'s current verdict; the view must resolve to this row, not the newer <see cref="FileScanRowId"/> FileScan.</summary>
        public const long FileScanBesideScanVerdictRowId = 2534;

        /// <summary>Edited: UntrainedHam (AI review 2.0 below threshold, dr 1933). Crypto Group; message kept, sender not banned.</summary>
        public const int UntrainedHamMsgId = 22160;

        /// <summary>Chat of <see cref="UntrainedHamMsgId"/> (Crypto Group).</summary>
        public const long UntrainedHamChatId = -100094881429433L;

        /// <summary>Sender of <see cref="UntrainedHamMsgId"/> (@wrongedjersey, not banned).</summary>
        public const long UntrainedHamUserId = 9621984255379L;

        /// <summary>
        /// Unscanned message (no verdict rows, trusted sender). @unhelpfulgrab, MainChat. Edited: distinctive
        /// text (not scrubbed lorem ipsum, with its similarity_hash recomputed) so training-sample dedup keeps it.
        /// </summary>
        public const int UnscannedMsgId = 219219;

        /// <summary>
        /// Edited: photo message (sender 9777802619662, MainChat) whose current verdict is an AutoBan
        /// decision (dr 3322, ExplicitSpam); media_features set to a photo hash (base64 <c>8J8PDw8PH/8=</c>).
        /// Layer 1 photo similarity reads it as a spam sample.
        /// </summary>
        public const int PhotoFeaturesMsgId = 222818;

        /// <summary>
        /// Edited 2026-09-28: video message (sender 9607332364262, MainChat, no photo_file_id) whose current
        /// verdict is a LegacyManual spam decision (dr 2168, ExplicitSpam); media_features set to three
        /// keyframe hashes at positions 0.1/0.5/0.9 (<see cref="VideoFeaturesKeyframeHashes"/>).
        /// Layer 1 video similarity reads it as a spam sample.
        /// </summary>
        public const int VideoFeaturesMsgId = 214424;

        /// <summary>The base64 keyframe hashes stored on <see cref="VideoFeaturesMsgId"/>, in position order.</summary>
        public static readonly string[] VideoFeaturesKeyframeHashes = ["PH7/58OBGDw=", "Dx8/f/78+PA=", "qlWqVQ/wD/A="];

        /// <summary>
        /// Edited 2026-09-28: the canonical OpenAI veto written in the current encoding. ContentScan dr 1934 on msg 212950 (MainChat,
        /// sender 9011155048805): StopWords 2 and Bayes 5 flagged spam, OpenAI returned a non-abstained clean
        /// (Score 0) → ImplicitHam, score 0. The message's verdict is the later /spam decision (dr 1935,
        /// ExplicitSpam), so the veto was a miss an admin corrected.
        /// </summary>
        public const long OpenAIVetoScanRowId = 1934;

        /// <summary>Message of <see cref="OpenAIVetoScanRowId"/>.</summary>
        public const int OpenAIVetoMsgId = 212950;

        /// <summary>
        /// Added 2026-09-28 (approved addition): prod scan whose OpenAI clean answer (the veto)
        /// RemoveV1ContentDetectionBridge (2026-03-06) converted to Abstained=true, Score = Confidence/20 (4.5). Stored as
        /// AddVerdictEvents leaves it: OpenAI check repaired to the veto encoding (Abstained=false, Score 0),
        /// properties <c>repaired_legacy_veto</c>, ContentScan/ImplicitHam. dr 22 on msg 94 in
        /// <see cref="LegacyVetoEarlyChatId"/>, sender 9320215215920; Bayes 4.9 was the overridden flag.
        /// Message text is lorem at the original length (111), like every non-banned author's message.
        /// </summary>
        public const long LegacyVetoEarlyScanRowId = 22;

        /// <summary>Message of <see cref="LegacyVetoEarlyScanRowId"/>.</summary>
        public const int LegacyVetoEarlyMsgId = 94;

        /// <summary>Chat of <see cref="LegacyVetoEarlyScanRowId"/> (not MainChat).</summary>
        public const long LegacyVetoEarlyChatId = -100082190806505;

        /// <summary>
        /// Added 2026-09-28 (approved addition): second converted OpenAI veto ("OpenAI vetoed spam" wording, Score 4.5), same repair as
        /// <see cref="LegacyVetoEarlyScanRowId"/>. dr 1639 on msg 22127 in <see cref="LegacyVetoLateChatId"/>,
        /// sender 9887521719353, from the later engine (score 0); Bayes 0.5 was the overridden flag. Lorem text (34).
        /// </summary>
        public const long LegacyVetoLateScanRowId = 1639;

        /// <summary>Message of <see cref="LegacyVetoLateScanRowId"/>.</summary>
        public const int LegacyVetoLateMsgId = 22127;

        /// <summary>Chat of <see cref="LegacyVetoLateScanRowId"/> (not MainChat).</summary>
        public const long LegacyVetoLateChatId = -100094881429433;

        /// <summary>Every canonical OpenAI veto scan: the current-encoding anchor and the two repaired converted ones.</summary>
        public static readonly long[] AllVetoScanRowIds = [OpenAIVetoScanRowId, LegacyVetoEarlyScanRowId, LegacyVetoLateScanRowId];

        /// <summary>User whose three latest messages are all training ham (msgs 71028/71030/71041).</summary>
        public const long AllHamUserId = 9184102838760L;

        /// <summary>Only its curated verdict keeps this old message (latest row dr2009, an explicit ham decision). @arisepacifism.</summary>
        public const int LabeledOnlyRetentionMsgId = 7974;
    }

    /// <summary>
    /// Anchors for the Audit Log page's Telegram Moderation Log tab (<c>canonical/34_user_actions.sql</c>,
    /// filtered by the page's Telegram User ID and Issued By fields). Untouched canonical rows; the
    /// counts are read from the clone at runtime, and tests guard the shapes below in ArrangeDataAsync.
    /// </summary>
    public static class ModerationLog
    {
        /// <summary>
        /// Telegram user with nine <c>user_actions</c> rows (ids 196..245, Nov 2025) issued by every actor
        /// kind the Issued By column renders: Auto-Detection, three web users (Owner, the stored-TOTP
        /// GlobalAdmin and the no-TOTP GlobalAdmin) and a Telegram admin. Has its own <c>telegram_users</c>
        /// row, so the Telegram User cell renders its display name with the "ID: …" caption. Nine rows fit
        /// on the moderation table's first page, so the filtered row count equals the DB count.
        /// </summary>
        public const long MixedIssuerUserId = 9704804870465L;

        /// <summary>
        /// Telegram admin who issued 20 <c>user_actions</c> rows (among them <see cref="MixedIssuerUserId"/>'s
        /// trust, row 206). Has a <c>telegram_users</c> row with username, first and last name, so the
        /// Issued By column renders its full name; tests read the names from the clone at runtime.
        /// </summary>
        public const long TelegramAdminIssuerId = 9906913218017L;
    }

    /// <summary>
    /// AI veto history anchors (canonical edit 2026-09-30). Telegram message ids are only unique per
    /// chat, so one id is shared by two chats to pin the chat-scoped history query and verdict join.
    /// </summary>
    public static class AIVetoHistory
    {
        /// <summary>
        /// Message id present in two chats: Poultry Community's newest message (renumbered from 14498,
        /// ExplicitHam via synthetic promotion dr3325) and MainChat's WORMGPT SimHash anchor.
        /// </summary>
        public const int SharedMessageId = 14538;

        /// <summary>Poultry Community — the chat whose newest message carries <see cref="SharedMessageId"/>.</summary>
        public const long SharedIdChatId = DmCelebrations.PoultryCommunityChatId;
    }

    /// <summary>
    /// Global system config (<c>configs</c>, chat_id = 0) anchors. The encrypted columns are NULL in
    /// <c>canonical/04_configs.sql</c>; their plaintexts are separate canonical fixtures
    /// (<c>canonical/04_configs.api_keys.json</c>) that <c>GoldenDataset.LoadCanonicalAsync</c>
    /// protects with the session's data-protection provider after the SQL load. The values below
    /// mirror that fixture so tests can assert what the app reads back through <c>ApiKeysConfig</c>.
    /// </summary>
    public static class SystemConfig
    {
        /// <summary>
        /// <c>chat_id</c> of the global config row (<c>configs.id = 1</c>). Its
        /// <c>backup_encryption_config</c> JSON deliberately keeps the legacy <c>Algorithm</c> and
        /// <c>Iterations</c> keys the model no longer has, as real deployments do until their next
        /// passphrase rotation. Anchor for stored-JSON tolerance tests.
        /// </summary>
        public const long GlobalChatId = 0;

        /// <summary>AI connection id carrying the canonical API key — the key under <c>aiConnectionKeys</c> in <c>04_configs.api_keys.json</c>.</summary>
        public const string OpenAiConnectionId = "openai";

        /// <summary>Dummy API key stored for <see cref="OpenAiConnectionId"/> in <c>04_configs.api_keys.json</c>. Only the format matters; it is never sent anywhere.</summary>
        public const string OpenAiConnectionKey = "sk-canonical-test-key";
    }

    /// <summary>Anchors for the user identity service tests (#552 part 1). No canonical rows were edited.</summary>
    public static class IdentityService
    {
        /// <summary>@bagging_armado: two scans (530 older, 534 newer); 534 has ai_explicit_display_text = true. Read-only: ProfileScanResultsRepositoryTests pins it.</summary>
        public const long ScannedTwiceExplicitUserId = 9220500615182;
        /// <summary>Message 110342 in Workshop Alumni: @bagging_armado's join service message (canonical edit 2026-10-03), the one message whose author's latest scan is explicit.</summary>
        public const long ExplicitAuthorMessageId = 110342;
        /// <summary>@swivelhumvee: not trusted, not banned, scanned once (score 0.0, Feb 2026) with a plain profile (no bio, personal channel 0, no photo or stories). Used by the join rename rescan test.</summary>
        public const long ScannedCleanUserId = 9025828368896;
        /// <summary>@Juvenileii: not trusted, not a bot, no scan rows, profile_scanned_at NULL.</summary>
        public const long UnscannedUserId = 9063342700386;
        /// <summary>@doilyemcee: the canonical bot. Read-only.</summary>
        public const long BotUserId = 9742468412405;
        /// <summary>@pastramiherbs: not trusted, active, no username_history rows.</summary>
        public const long UntrustedNoHistoryUserId = 9263051408340;
        /// <summary>@starlightskinless: trusted, not an admin.</summary>
        public const long TrustedUserId = 9006671634371;
        /// <summary>@violingentleman: not trusted, active, no history rows. Used by the row-lock race test.</summary>
        public const long RaceUserId = 9680301255238;
        /// <summary>@raceoutnumber: not trusted; user_photo_path and photo_hash both set.</summary>
        public const long PhotoUserId = 9264989724828;
        /// <summary>@calixrowen: is_active = false (a banned spammer, not trusted, not a bot). Used by the MarkActiveAsync test.</summary>
        public const long InactiveUserId = 9332352149450;
    }

    /// <summary>Anchors for flagged name masking and the name-only scan (#552 part 2).</summary>
    public static class FlaggedNames
    {
        /// <summary>@loucurtsinger "Lou Curtsinger": not trusted, not a bot, no profile_scan_results rows, profile_scanned_at and profile_scan_score NULL (banned in Nov 2025, before scanning existed). NameOnlyScanTests' name-only scan writes the user's first row. Read-only otherwise.</summary>
        public const long NameOnlyScanUserId = 9333810782137;
        /// <summary>@Adexfunnel "Adexfunnel": a name that advertises ad / marketing funnels. Held for review by its profile scan (score 2.8, alert #178), then banned by an admin. Its only scan row is the real prod scan, imported with ai_promotional_display_text = true (canonical edit 2026-10-05; the column did not exist in prod). Read-only.</summary>
        public const long BannedPromotionalUserId = 9635655270997;
        /// <summary>@splendorfraying "Stargazer Snippet": not banned, not trusted; only scan row 526 (score 0.0) has ai_promotional_display_text = true (canonical edit 2026-10-05). Read-only.</summary>
        public const long UnbannedPromotionalUserId = 9213195802818;
        /// <summary>@splendorfraying's only scan row, flag-edited to ai_promotional_display_text = true (canonical edit 2026-10-05).</summary>
        public const long UnbannedPromotionalScanId = 526;
    }

    /// <summary>Anchors for the rescan job's incomplete-scan selection (#552 part 2).</summary>
    public static class ProfileRescan
    {
        /// <summary>"Ferocity Opponent" (no username): untrusted, unbanned, non-bot, never scanned; profile_scan_excluded cleared (canonical edit 2026-10-05; it had been set by the old unresolvable auto-exclusion, read as an admin re-including the user).</summary>
        public const long NeverScannedUserId = 9963580010331;
        /// <summary>"Preflight Silk" (no username): untrusted, unbanned, non-bot, never scanned, profile_scan_excluded = true. Read-only.</summary>
        public const long ExcludedNeverScannedUserId = 9434053902837;
        /// <summary>@unreadbackspin: untrusted, unbanned; its only scan row 528 (score 1.2, AI fields NULL) has source = NameOnly (canonical edit 2026-10-05).</summary>
        public const long NameOnlyLatestUserId = 9758118926756;
        /// <summary>@unreadbackspin's only scan row, flag-edited to source = 1 (NameOnly) (canonical edit 2026-10-05).</summary>
        public const long NameOnlyLatestScanId = 528;
        /// <summary>@parkingsturdily: untrusted, unbanned; its only scan row 533 is a FullScan from 2026-04-30. Read-only.</summary>
        public const long FullScanLatestUserId = 9922735795237;
        /// <summary>@elvesunable: trusted; messages in three chats, latest first: Hobby Forum (2026-04-11), Main Community (2026-03-03), Garage Chat (2025-12-16). Read-only: pins the rescan job's chat list (GetChatsForUserAsync).</summary>
        public const long MultiChatUserId = 9739143127436;
        /// <summary>Hobby Forum: <see cref="MultiChatUserId"/>'s most recently active chat.</summary>
        public const long MultiChatLatestChatId = -100003785594462L;
        /// <summary>Garage Chat: <see cref="MultiChatUserId"/>'s least recently active chat (Main Community, <see cref="Chats.MainChatId"/>, is between).</summary>
        public const long MultiChatOldestChatId = -100063904363399L;
        /// <summary>@unbeatenmutiny: untrusted, unbanned; all its messages are in chat 0, which is not a managed chat. Read-only: the rescan job's chat list leaves out chats that are not active managed chats, and the job skips a user with message history but no active managed chat.</summary>
        public const long UnmanagedChatOnlyUserId = 9862700513599;
        /// <summary>@geologistfence: untrusted, unbanned; its only message is in Location Group (active, managed) and is soft-deleted (deleted_at set). Read-only: soft-deleted messages still count as the user having posted in that chat.</summary>
        public const long SoftDeletedOnlyUserId = 9154293302720;
        /// <summary>Location Group: the active managed chat of <see cref="SoftDeletedOnlyUserId"/>'s only (soft-deleted) message.</summary>
        public const long SoftDeletedOnlyChatId = -100055570785509L;
    }

    /// <summary>Anchors for BackupService restore tests.</summary>
    public static class Backup
    {
        /// <summary>
        /// Synthetic username_blacklist row 999005 ('archived_pattern', disabled). RestoreAsync_ShouldWipeAllTablesFirst
        /// moves it to <see cref="MovedBlacklistEntryId"/> after taking the backup (runtime UPDATE, no canonical edit).
        /// </summary>
        public const long BlacklistEntryId = 999005;
        /// <summary>The key the restore-wipe test moves <see cref="BlacklistEntryId"/> to; never present in canonical.</summary>
        public const long MovedBlacklistEntryId = 999905;
    }

    /// <summary>
    /// username_history anchors for past-name search (canonical edit 2026-10-03). Both owners are
    /// banned spammers, so they appear under the All and Banned tabs, not Active.
    /// </summary>
    public static class UsernameHistory
    {
        /// <summary>@BryanNguyen54 "Bryan Nguyen": history row 3 records the prior names "Rsza Тилляев" and, since the 2026-10-03 edit, the prior username <see cref="PastUsername"/>.</summary>
        public const long PastUsernameUserId = 9032620986755;
        /// <summary>Prior username on history row 3 (flag-edited from NULL). No current name or other history row contains it.</summary>
        public const string PastUsername = "rsza_tilla";
        /// <summary>"Jeanette" (no username): history row 2 records the prior first name <see cref="PastFirstName"/>.</summary>
        public const long PastFirstNameUserId = 9875141377477;
        /// <summary>Prior first name on history row 2.</summary>
        public const string PastFirstName = "QQQ";
        /// <summary>History row 4 records the prior names "Tin Tun" / "Min" with no prior username. Read-only.</summary>
        public const long NoPastUsernameUserId = 9095125964119;
        /// <summary>History row 1's owner. <c>UsernameHistoryRepositoryTests</c> deletes this user in its clone to test the cascade; otherwise read-only.</summary>
        public const long CascadeDeleteUserId = 9726308613009;
    }
}

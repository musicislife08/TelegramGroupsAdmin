namespace TelegramGroupsAdmin.IntegrationTests.TestData;

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
///
/// Promote a constant up to a top-level domain class (e.g. <see cref="WebUsers"/>,
/// <see cref="Chats"/>) once a second consumer wants it; until then, keep it next
/// to the tests that use it under a domain nested class.
/// </summary>
internal static class GoldenDatasetConstants
{
    /// <summary>
    /// Web user fixtures from <c>canonical/01_users.sql</c>. UUIDs are stable
    /// fixtures preserved verbatim across canonical regenerations — they're the
    /// 4 hand-picked canonical anchors that tests can pin to. Password for all
    /// 9 canonical web users: <c>Passw0rd!SaidNoSecurityAuditorEver</c>.
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

        /// <summary>
        /// Deleted Admin fixture (deleted@example.com, status=3, is_active=false,
        /// invited_by=Owner). Use when a test needs a soft-deleted user — e.g. asserting
        /// that deleted users are filtered out of active queries, or that backup/restore
        /// preserves <see cref="DeletedAdminStatus"/>.
        /// </summary>
        public const string DeletedAdminId = "a8dc8371-afc5-4b61-9d71-d177f2dd9ddd";

        /// <summary>
        /// Status value carried by the Deleted Admin fixture (UserStatus.Deleted = 3).
        /// Pair with <see cref="DeletedAdminId"/> when verifying canonical persists the
        /// soft-deleted state through round-trips.
        /// </summary>
        public const int DeletedAdminStatus = 3;
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

        /// <summary>
        /// Canonical user that appears as <c>labeled_by_user_id</c> on training_labels rows.
        /// Stable anchor for tests that need an Actor recognized as a prior labeler in the
        /// canonical training set.
        /// </summary>
        public const long TrainingLabelActorId = 9084745993769L;
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
    /// Canonical anchors used by <c>TrainingLabelsRepositoryTests</c> to pin existing
    /// spam/ham label rows and FK-valid-but-unlabeled message rows. The chat side of
    /// each anchor is in <see cref="Chats.TrainingFixturesChatId"/> for the labeled set
    /// and <see cref="Chats.LandOwnersChatId"/> for most of the unlabeled set
    /// (see per-constant notes).
    /// </summary>
    public static class TrainingLabels
    {
        /// <summary>Canonical spam label (label=0) — message_id in <see cref="Chats.TrainingFixturesChatId"/>.</summary>
        public const int ExistingSpamMsgId = 4575;

        /// <summary>Canonical ham label (label=1) — message_id in <see cref="Chats.TrainingFixturesChatId"/>.</summary>
        public const int ExistingHamMsgId = 4602;

        /// <summary>Second canonical spam label — used for PK-uniqueness enforcement tests. Chat: <see cref="Chats.TrainingFixturesChatId"/>.</summary>
        public const int ExistingSpam2MsgId = 4655;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.TrainingFixturesChatId"/>.</summary>
        public const int UnlabeledMsg1Id = 4620;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.LandOwnersChatId"/>.</summary>
        public const int UnlabeledMsg2Id = 7789;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.LandOwnersChatId"/>.</summary>
        public const int UnlabeledMsg3Id = 7834;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.LandOwnersChatId"/>.</summary>
        public const int UnlabeledMsg4Id = 7836;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.LandOwnersChatId"/>.</summary>
        public const int UnlabeledMsg5Id = 7853;

        /// <summary>Unlabeled FK-valid message in <see cref="Chats.LandOwnersChatId"/>.</summary>
        public const int UnlabeledMsg6Id = 8095;
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
        // 1 detection_result with used_for_training=true. Shifted to -90d → PRESERVED.
        public const int MsgId_TrainingPreserved = 218579;

        // ── Anchor 4: bare orphan just past retention threshold ──
        // 0 detection_results, no edits. Shifted to -35d → DELETED.
        public const int MsgId_BareOrphan35d = 212803;

        // ── Anchor 5: bare orphan just inside retention boundary ──
        // 0 detection_results, no edits. Shifted to -29d → PRESERVED.
        public const int MsgId_BareOrphan29d = 213117;

        // ── Anchor 6: non-training detection, curated classification ──
        // 1 detection_result (id 3033) with used_for_training=false, classification=ExplicitSpam
        // (a curated value). Shifted to -50d → PRESERVED despite age and despite
        // used_for_training=false, since its verdict is curated.
        public const int MsgId_NonTrainingDeleted = 220885;

        /// <summary>
        /// All 6 (chat_id, message_id) tuples passed to <c>Reduce.KeepMessages(...)</c>.
        /// FK CASCADE drops every other canonical message's detection_results,
        /// training_labels, edits, and translations.
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

        public const long DrId_TodaySpam1 = 2952;        // msg 220017, net_score=5.0
        public const long DrId_TodaySpam2 = 2955;        // msg 220093, net_score=4.6
        public const long DrId_TodaySpam3 = 2959;        // msg 220224, net_score=4.8
        public const long DrId_YesterdaySpam1 = 2998;    // msg 220364, net_score=4.9
        public const long DrId_YesterdaySpam2 = 3055;    // msg 221125, net_score=4.6
        public const long DrId_LastWeekSpam1 = 3119;     // msg 221604, net_score=4.3
        public const long DrId_LastWeekSpam2 = 3221;     // msg 222793, net_score=4.8

        public const long MsgId_TodaySpam1 = 220017;
        public const long MsgId_TodaySpam2 = 220093;
        public const long MsgId_TodaySpam3 = 220224;
        public const long MsgId_YesterdaySpam1 = 220364;
        public const long MsgId_YesterdaySpam2 = 221125;
        public const long MsgId_LastWeekSpam1 = 221604;
        public const long MsgId_LastWeekSpam2 = 222793;

        // ── FP pair on msg 213325 (organic auto-spam + manual-ham correction) ──
        public const long MsgId_FalsePositive = 213325;
        public const long DrId_FpAuto = 2012;            // net_score=10.15, auto-spam
        public const long DrId_FpManual = 2013;          // net_score=-5, manual-ham (later timestamp)

        // ── FN pair on msg 211184 (organic auto-ham + manual-spam correction) ──
        public const long MsgId_FalseNegative = 211184;
        public const long DrId_FnAuto = 1492;            // net_score=-1.45, auto-ham
        public const long DrId_FnManual = 1494;          // net_score=5, manual-spam (later timestamp)

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
        /// training_labels, edits, and translations.
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
        /// (DrId 1492, net_score=-1.45) which the manual correction later flags as a
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
        /// <summary>Synthetic pending exam failure (status=0, user 9465377455871, chat -100054416618415).</summary>
        public const long PendingExamFailureId = 187;

        /// <summary>Real resolved exam failure (status=1, action_taken='approve', reviewed by globaladmin).</summary>
        public const long ResolvedExamFailureId = 185;

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

        /// <summary>Unscanned message (no verdict rows). @unhelpfulgrab, MainChat.</summary>
        public const int UnscannedMsgId = 219219;

        /// <summary>User whose three latest messages are all training ham (msgs 71028/71030/71041).</summary>
        public const long AllHamUserId = 9184102838760L;

        /// <summary>Only a training label keeps this old message (all its rows used_for_training = false). @arisepacifism.</summary>
        public const int LabeledOnlyRetentionMsgId = 7974;
    }
}

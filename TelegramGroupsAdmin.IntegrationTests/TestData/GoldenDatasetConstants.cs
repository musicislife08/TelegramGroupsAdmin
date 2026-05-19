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
        // ── Anchor 1: bare message with attached edit (cascade-tests edit deletion) ──
        // 0 detection_results, 1 message_edits row (id 337). Shifted to -45d → DELETED;
        // SUT's explicit MessageEdits.RemoveRange exercises the edit-cascade path.
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

        // ── Anchor 6: non-training detection (DR cascades with message) ──
        // 1 detection_result with used_for_training=false. Shifted to -50d → DELETED.
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
        /// anchors 1, 2, 4, 6 (45d, 60d, 35d, 50d past midnight without training preservation).
        /// Anchors 3 (training) and 5 (boundary) are preserved.
        /// </summary>
        public const int ExpectedDeletionsWith30DayRetention = 4;
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
}

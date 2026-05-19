namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Canonical IDs and target NOW()-relative offsets used by
/// <c>AnalyticsRepositoryTests</c> to shape the substrate for time-window
/// aggregation tests. All anchors are in MainChat (-100026957614982).
///
/// Test setup applies: <c>Reduce.KeepMessages(AllMessageRefs)</c> to isolate the
/// 9 anchor messages, then <c>Mutate.ShiftDetectionResultTimestamps(...)</c> +
/// <c>ShiftWelcomeResponseTimestamps(...)</c> to re-time the surviving rows into
/// today/yesterday/last-week buckets.
/// </summary>
internal static class AnalyticsAnchors
{
    public const long MainChatId = -100026957614982L;

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
        (MainChatId, MsgId_TodaySpam1),
        (MainChatId, MsgId_TodaySpam2),
        (MainChatId, MsgId_TodaySpam3),
        (MainChatId, MsgId_YesterdaySpam1),
        (MainChatId, MsgId_YesterdaySpam2),
        (MainChatId, MsgId_LastWeekSpam1),
        (MainChatId, MsgId_LastWeekSpam2),
        (MainChatId, MsgId_FalsePositive),
        (MainChatId, MsgId_FalseNegative),
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

    // ── Expected count constants (migrated from GoldenDataset.AnalyticsData) ──
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

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Canonical IDs and target NOW()-relative offsets used by
/// <c>MessageHistoryRepositoryTests.CleanupExpiredAsync_WithOldMessages_*</c>
/// to shape the substrate for retention-cleanup testing. All anchors are in
/// MainChat (-100026957614982).
///
/// Test setup applies: <c>Reduce.KeepMessages(AllMessageRefs)</c> to isolate the
/// 6 anchor messages, then <c>Mutate.ShiftMessageTimestamps(...)</c> to re-time
/// the surviving rows into the retention windows the SUT cares about.
/// </summary>
internal static class RetentionAnchors
{
    public const long MainChatId = -100026957614982L;

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
        (MainChatId, MsgId_BareWithEdit),
        (MainChatId, MsgId_BareOrphan60d),
        (MainChatId, MsgId_TrainingPreserved),
        (MainChatId, MsgId_BareOrphan35d),
        (MainChatId, MsgId_BareOrphan29d),
        (MainChatId, MsgId_NonTrainingDeleted),
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

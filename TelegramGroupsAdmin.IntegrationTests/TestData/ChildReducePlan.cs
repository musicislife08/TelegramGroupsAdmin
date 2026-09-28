using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
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

    /// <summary>
    /// Keeps N messages currently classified ExplicitSpam — selected from
    /// <c>detection_results</c> explicit decision rows (source NOT IN ContentScan/FileScan/
    /// TrainingExclude, classification ExplicitSpam), the label store the SUT's
    /// <c>message_verdicts</c> view reads, ordered <c>chat_id ASC, message_id ASC</c> the
    /// same way <c>training_labels</c> selection always has been. Explicit decision rows
    /// for every other message are deleted; <c>training_labels</c> is then pruned to match
    /// (a label survives only if its message kept a same-polarity decision) until Task 16
    /// drops that table.
    /// </summary>
    public ChildReducePlan KeepSpam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.SpamCount = count;
        return this;
    }

    /// <summary>Symmetric to <see cref="KeepSpam"/> for ExplicitHam.</summary>
    public ChildReducePlan KeepHam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.HamCount = count;
        return this;
    }

    /// <summary>
    /// Keeps the lowest N <c>detection_results</c> <b>scan</b> rows by id — <c>source</c>
    /// ContentScan(0) or FileScan(1) only. Since explicit labels now live as decision rows
    /// in <c>detection_results</c> (source NOT
    /// IN ContentScan/FileScan/TrainingExclude, classification ExplicitSpam/ExplicitHam),
    /// decision rows are the label store and are pruned by <see cref="KeepSpam"/>/
    /// <see cref="KeepHam"/> instead — KeepDetectionResults no longer touches them (a call
    /// with count 0 used to wipe every verdict event, explicit decisions included; it now
    /// only drains the scan/implicit pool). Leaves the decision-row count unchanged.
    /// </summary>
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

    /// <summary>
    /// Drops every <c>messages</c> row that has no surviving explicit spam/ham decision
    /// row in <c>detection_results</c> (source NOT IN ContentScan/FileScan/TrainingExclude,
    /// classification ExplicitSpam/ExplicitHam) after KeepSpam / KeepHam have been applied
    /// — decision rows are the label store now and what the SUT's <c>message_verdicts</c>
    /// view reads; KeepSpam/KeepHam already pruned <c>training_labels</c> to match the
    /// surviving decision rows, so checking either table gives the same surviving-message
    /// set. FK cascades clean up the message's <c>detection_results</c>, <c>training_labels</c>,
    /// <c>message_edits</c>, and <c>message_translations</c> children. Use this when a test
    /// needs the "labels-only" substrate (no implicit ham pool from unlabeled messages, no
    /// implicit spam pool unless KeepDetectionResults is also constrained).
    /// </summary>
    public ChildReducePlan KeepLabeledMessagesOnly()
    {
        _state.DropUnlabeledMessages = true;
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
    public IReadOnlyList<(long ChatId, long MessageId)>? MessageIdAllowlist { get; set; }
    public int? SpamCount { get; set; }
    public int? HamCount { get; set; }
    public int? DetectionResultsCount { get; set; }
    public int? UserActionsCount { get; set; }
    public bool DropUnlabeledMessages { get; set; }

    public GoldenReducePlanState(AppDbContext context) => _context = context;

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. KeepMessages — runs first; FK cascade fires (CASCADE on
            //    message_edits/training_labels/detection_results/message_translations,
            //    SetNull on user_actions.MessageId/ChatId). Allowlist overload takes
            //    precedence over count when both are set.
            if (MessageIdAllowlist is { Count: > 0 } allowlist)
            {
                var values = string.Join(",", allowlist.Select((_, i) => $"({{{2 * i}}}::bigint, {{{2 * i + 1}}}::bigint)"));
                var parameters = new object[allowlist.Count * 2];
                for (int i = 0; i < allowlist.Count; i++)
                {
                    parameters[2 * i] = allowlist[i].ChatId;
                    parameters[2 * i + 1] = allowlist[i].MessageId;
                }
                await ExecRawAsync(_context, ct,
                    $"DELETE FROM messages WHERE (chat_id, message_id) NOT IN ({values})",
                    parameters, "KeepMessages(allowlist)");
            }
            else if (MessagesCount is int msgN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM messages " +
                    "WHERE (chat_id, message_id) NOT IN (" +
                    "  SELECT chat_id, message_id FROM messages " +
                    "  ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    msgN, "KeepMessages");
            }

            // 2/3. KeepSpam / KeepHam — Task 8 fix round (R20): the KEEP SET is now driven
            //    by detection_results *decision* rows, the label store the SUT's
            //    message_verdicts view actually reads — source NOT IN (ContentScan=0,
            //    FileScan=1, TrainingExclude=17), classification ExplicitSpam=0/
            //    ExplicitHam=1 (R17 established these predicates; R20 makes them the
            //    selection driver instead of a downstream filter on training_labels' pick).
            //    The unit is the message: pick the N lowest (chat_id, message_id) pairs —
            //    same "chat_id ASC, message_id ASC" order training_labels selection always
            //    used — among rows matching the predicate, delete every other matching
            //    decision row, then prune training_labels to match (a label survives only
            //    if its message kept a same-polarity decision) until Task 16 drops that
            //    table. A decision row with no training_labels row at all (e.g. an AutoBan
            //    fold, or a chat_id=0 Training-Data-page/Import sample) is included in the
            //    N-message selection pool on equal footing with labeled ones — it is a
            //    genuine explicit decision, just one training_labels never mirrored.
            const short LabelSpam = (short)TrainingLabel.Spam; // 0
            const short LabelHam = (short)TrainingLabel.Ham;   // 1
            const int ClassificationExplicitSpam = (int)VerdictClassification.ExplicitSpam; // 0
            const int ClassificationExplicitHam = (int)VerdictClassification.ExplicitHam;   // 1
            const string DecisionSourceExclusion =
                "(0 /* ContentScan */, 1 /* FileScan */, 17 /* TrainingExclude */)";
            const string ScanSourceInclusion = "(0 /* ContentScan */, 1 /* FileScan */)";

            if (SpamCount is int spamN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    $"WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "  AND (chat_id, message_id) NOT IN (" +
                    "    SELECT DISTINCT chat_id, message_id FROM detection_results " +
                    $"    WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "    ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    spamN, "KeepSpam(decision rows)", ClassificationExplicitSpam);

                await ExecBareAsync(_context, ct,
                    "DELETE FROM training_labels " +
                    $"WHERE label = {LabelSpam} " +
                    "  AND NOT EXISTS (" +
                    "    SELECT 1 FROM detection_results dr " +
                    "    WHERE dr.chat_id = training_labels.chat_id AND dr.message_id = training_labels.message_id " +
                    $"      AND dr.source NOT IN {DecisionSourceExclusion} AND dr.classification = {ClassificationExplicitSpam})",
                    "KeepSpam(training_labels)");
            }

            if (HamCount is int hamN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    $"WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "  AND (chat_id, message_id) NOT IN (" +
                    "    SELECT DISTINCT chat_id, message_id FROM detection_results " +
                    $"    WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "    ORDER BY chat_id ASC, message_id ASC LIMIT {0})",
                    hamN, "KeepHam(decision rows)", ClassificationExplicitHam);

                await ExecBareAsync(_context, ct,
                    "DELETE FROM training_labels " +
                    $"WHERE label = {LabelHam} " +
                    "  AND NOT EXISTS (" +
                    "    SELECT 1 FROM detection_results dr " +
                    "    WHERE dr.chat_id = training_labels.chat_id AND dr.message_id = training_labels.message_id " +
                    $"      AND dr.source NOT IN {DecisionSourceExclusion} AND dr.classification = {ClassificationExplicitHam})",
                    "KeepHam(training_labels)");
            }

            // 4. KeepLabeledMessagesOnly — must run after KeepSpam/KeepHam so it sees
            //    the post-filter label state. Checks for a surviving explicit decision row
            //    (detection_results is what the SUT's message_verdicts view reads; steps
            //    2/3 already pruned training_labels to match, so checking either table
            //    gives the same surviving-message set). FK CASCADE fires on the dropped
            //    messages (detection_results, training_labels, message_edits,
            //    message_translations). user_actions.MessageId/ChatId become NULL via SetNull.
            if (DropUnlabeledMessages)
            {
                await ExecBareAsync(_context, ct,
                    "DELETE FROM messages " +
                    "WHERE NOT EXISTS (" +
                    "  SELECT 1 FROM detection_results dr " +
                    $"  WHERE dr.message_id = messages.message_id AND dr.chat_id = messages.chat_id " +
                    $"    AND dr.source NOT IN {DecisionSourceExclusion} AND dr.classification IN ({ClassificationExplicitSpam}, {ClassificationExplicitHam}))",
                    "KeepLabeledMessagesOnly");
            }

            // 5. KeepDetectionResults — surrogate id PK. Task 8 fix round (R17): scoped to
            //    scan rows only (source IN (ContentScan=0, FileScan=1)) — decision rows are
            //    the label store now (see step 2/3) and are pruned by KeepSpam/KeepHam, not
            //    here. A call with count 0 used to wipe every verdict event including
            //    explicit decisions; it now only drains the scan/implicit pool.
            if (DetectionResultsCount is int drN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    $"WHERE source IN {ScanSourceInclusion} " +
                    "  AND id NOT IN (" +
                    "    SELECT id FROM detection_results " +
                    $"    WHERE source IN {ScanSourceInclusion} " +
                    "    ORDER BY id ASC LIMIT {0})",
                    drN, "KeepDetectionResults");
            }

            // 6. KeepUserActions — surrogate id PK; runs last so SetNull orphans
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

    private static async Task ExecBareAsync(AppDbContext ctx, CancellationToken ct,
        string sql, string stepName)
    {
        try
        {
            await ctx.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex)
        {
            throw new GoldenReducePlanException($"Step '{stepName}' failed", stepName, ex);
        }
    }

    private static async Task ExecRawAsync(AppDbContext ctx, CancellationToken ct,
        string sql, object[] parameters, string stepName)
    {
        try
        {
            await ctx.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
        catch (Exception ex)
        {
            throw new GoldenReducePlanException($"Step '{stepName}' failed", stepName, ex);
        }
    }
}

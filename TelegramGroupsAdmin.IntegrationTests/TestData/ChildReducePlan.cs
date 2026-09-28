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

    /// <summary>
    /// Keeps the lowest N <c>detection_results</c> <b>scan</b> rows by id — <c>source</c>
    /// ContentScan(0) or FileScan(1) only. Since explicit labels now live as decision rows
    /// in <c>detection_results</c> (source NOT IN ContentScan/FileScan/TrainingExclude,
    /// classification ExplicitSpam/ExplicitHam), decision rows are the label store and are
    /// pruned by <see cref="KeepSpam"/>/<see cref="KeepHam"/> instead — KeepDetectionResults
    /// no longer touches them (a call with count 0 used to wipe every verdict event,
    /// explicit decisions included; it now only drains the scan/implicit pool).
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
    /// — decision rows are the label store now; by construction (KeepSpam/KeepHam prune
    /// <c>training_labels</c> and decision rows to the same message set) this agrees with
    /// checking <c>training_labels</c> too, but decision rows are what the SUT reads via
    /// <c>message_verdicts</c>. FK cascades clean up the message's <c>detection_results</c>,
    /// <c>training_labels</c>, <c>message_edits</c>, and <c>message_translations</c> children.
    /// Use this when a test needs the "labels-only" substrate (no implicit ham pool from
    /// unlabeled messages, no implicit spam pool unless KeepDetectionResults is also
    /// constrained).
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

            // 2. KeepSpam — slice predicate appears on BOTH sides so KeepSpam(5)
            //    doesn't delete ham rows. training_labels.label is a smallint:
            //    0=Spam, 1=Ham (per TelegramGroupsAdmin.Core/Models/TrainingLabel.cs).
            //    Task 8 fix round (R17): explicit labels now also live as *decision*
            //    rows in detection_results — source NOT IN (ContentScan=0, FileScan=1,
            //    TrainingExclude=17), classification ExplicitSpam=0/ExplicitHam=1 (the
            //    values happen to equal TrainingLabel's). The message_verdicts view the
            //    SUT reads is built from detection_results only, so KeepSpam/KeepHam must
            //    prune decision rows too, or the training_labels-only prune is invisible
            //    to the SUT. The KEEP SET is still driven by training_labels (unchanged
            //    ordering/count, so exact-N assertions on training_labels keep holding);
            //    decision rows are then pruned to whatever training_labels now says
            //    survived, "so both stores agree" — a decision row with no surviving
            //    training_labels row (e.g. an AutoBan fold with no admin label) is culled
            //    too, which is intended: it was never part of the KeepSpam/KeepHam count.
            const short LabelSpam = (short)TrainingLabel.Spam; // 0
            const short LabelHam = (short)TrainingLabel.Ham;   // 1
            const int ClassificationExplicitSpam = (int)VerdictClassification.ExplicitSpam; // 0
            const int ClassificationExplicitHam = (int)VerdictClassification.ExplicitHam;   // 1
            const string DecisionSourceExclusion =
                "(0 /* ContentScan */, 1 /* FileScan */, 17 /* TrainingExclude */)";

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

                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    $"WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "  AND NOT EXISTS (" +
                    "    SELECT 1 FROM training_labels tl " +
                    "    WHERE tl.chat_id = detection_results.chat_id AND tl.message_id = detection_results.message_id " +
                    "      AND tl.label = {1})",
                    spamN, "KeepSpam(decision rows)", ClassificationExplicitSpam);
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

                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    $"WHERE source NOT IN {DecisionSourceExclusion} AND classification = {{1}} " +
                    "  AND NOT EXISTS (" +
                    "    SELECT 1 FROM training_labels tl " +
                    "    WHERE tl.chat_id = detection_results.chat_id AND tl.message_id = detection_results.message_id " +
                    "      AND tl.label = {1})",
                    hamN, "KeepHam(decision rows)", ClassificationExplicitHam);
            }

            // 4. KeepLabeledMessagesOnly — must run after KeepSpam/KeepHam so it sees
            //    the post-filter label state. Checks for a surviving explicit decision row
            //    (detection_results is what the SUT's message_verdicts view reads; by
            //    construction of steps 2/3 this agrees with training_labels membership).
            //    FK CASCADE fires on the dropped messages (detection_results,
            //    training_labels, message_edits, message_translations). user_actions.
            //    MessageId/ChatId become NULL via SetNull.
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
                    "WHERE source IN (0, 1) " +
                    "  AND id NOT IN (" +
                    "    SELECT id FROM detection_results " +
                    "    WHERE source IN (0, 1) " +
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

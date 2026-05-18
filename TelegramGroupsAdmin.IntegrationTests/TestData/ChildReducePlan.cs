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
    /// Drops every <c>messages</c> row that has no surviving <c>training_labels</c> row
    /// after KeepSpam / KeepHam have been applied. FK cascades clean up the message's
    /// <c>detection_results</c>, <c>message_edits</c>, and <c>message_translations</c>
    /// children. Use this when a test needs the "labels-only" substrate (no implicit ham
    /// pool from unlabeled messages, no implicit spam pool unless KeepDetectionResults
    /// is also constrained).
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

            // 4. KeepLabeledMessagesOnly — must run after KeepSpam/KeepHam so it sees
            //    the post-filter label state. FK CASCADE fires on the dropped messages
            //    (detection_results, training_labels (none left for these), message_edits,
            //    message_translations). user_actions.MessageId/ChatId become NULL via SetNull.
            if (DropUnlabeledMessages)
            {
                await ExecBareAsync(_context, ct,
                    "DELETE FROM messages " +
                    "WHERE NOT EXISTS (" +
                    "  SELECT 1 FROM training_labels t " +
                    "  WHERE t.message_id = messages.message_id AND t.chat_id = messages.chat_id)",
                    "KeepLabeledMessagesOnly");
            }

            // 5. KeepDetectionResults — surrogate id PK
            if (DetectionResultsCount is int drN)
            {
                await ExecAsync(_context, ct,
                    "DELETE FROM detection_results " +
                    "WHERE id NOT IN (" +
                    "  SELECT id FROM detection_results " +
                    "  ORDER BY id ASC LIMIT {0})",
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
}

using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Stage-1 reducer plan returned by GoldenDataset.Reduce(ctx). All five Keep* methods
/// are reachable; calling any child reducer (KeepSpam/KeepHam/KeepDetectionResults/
/// KeepUserActions) transitions to ChildReducePlan, where KeepMessages is no longer
/// reachable in fluent chains.
///
/// The underlying GoldenReducePlanState is shared between stages — registration via
/// intermediate variables can register parent ops after children, but ApplyAsync
/// runs in fixed parent-first topological order regardless.
/// </summary>
public sealed class GoldenReducePlanBuilder
{
    private readonly GoldenReducePlanState _state;

    internal GoldenReducePlanBuilder(GoldenReducePlanState state) => _state = state;

    public GoldenReducePlanBuilder KeepMessages(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.MessagesCount = count;
        return this;
    }

    /// <summary>
    /// Allowlist overload: keeps only the named (chat_id, message_id) tuples and drops
    /// every other <c>messages</c> row. Use when a test needs deterministic isolation by
    /// identity (e.g., AnalyticsRepositoryTests pinning specific FP/FN message anchors).
    /// FK CASCADE drops associated detection_results / training_labels / message_edits /
    /// message_translations; user_actions.MessageId/ChatId become NULL via SetNull.
    /// </summary>
    public GoldenReducePlanBuilder KeepMessages(IEnumerable<(long ChatId, long MessageId)> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        if (list.Count == 0) throw new ArgumentException("Allowlist cannot be empty.", nameof(ids));
        _state.MessageIdAllowlist = list;
        return this;
    }

    public ChildReducePlan KeepSpam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.SpamCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepHam(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.HamCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepDetectionResults(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.DetectionResultsCount = count;
        return new ChildReducePlan(_state);
    }

    public ChildReducePlan KeepUserActions(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _state.UserActionsCount = count;
        return new ChildReducePlan(_state);
    }

    public Task ApplyAsync(CancellationToken ct = default) => _state.ApplyAsync(ct);
}

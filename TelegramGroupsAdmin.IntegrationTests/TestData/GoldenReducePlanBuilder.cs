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

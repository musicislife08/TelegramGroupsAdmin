namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>One subscription that could not be synced, with a message safe to show an admin.</summary>
public sealed record BlocklistSyncFailure(long SubscriptionId, string SubscriptionName, string Message);

/// <summary>
/// One or more blocklist subscriptions could not be synced. The other subscriptions in the same
/// run were still synced; <see cref="Failures"/> names the ones that were not and why, and
/// <see cref="Exception.Message"/> summarises them for the job's last-error field and the UI.
/// </summary>
public sealed class BlocklistSyncException : Exception
{
    public BlocklistSyncException(BlocklistSyncFailure failure, Exception? inner = null)
        : base($"Blocklist '{failure.SubscriptionName}' could not be synced: {failure.Message}", inner)
    {
        Failures = [failure];
    }

    public BlocklistSyncException(IReadOnlyList<BlocklistSyncFailure> failures, int attempted)
        : base(Summarise(failures, attempted))
    {
        Failures = failures;
    }

    /// <summary>The subscriptions that failed, in sync order.</summary>
    public IReadOnlyList<BlocklistSyncFailure> Failures { get; }

    private static string Summarise(IReadOnlyList<BlocklistSyncFailure> failures, int attempted)
    {
        var details = string.Join("; ", failures.Select(f => $"'{f.SubscriptionName}' — {f.Message}"));
        return $"{failures.Count} of {attempted} blocklist subscriptions could not be synced: {details}";
    }
}

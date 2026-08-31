namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// Wraps any exception raised inside GoldenReducePlanState.ApplyAsync. The transaction
/// is rolled back before this is thrown. StepName carries the failing reducer's name
/// (e.g., "KeepSpam", "KeepMessages"); null for non-step failures (transaction
/// begin/commit).
/// </summary>
public sealed class GoldenReducePlanException : Exception
{
    public string? StepName { get; }

    public GoldenReducePlanException(string message, string? stepName, Exception inner)
        : base(message, inner)
    {
        StepName = stepName;
    }
}

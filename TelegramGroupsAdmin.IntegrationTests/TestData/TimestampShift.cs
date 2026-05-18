namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// A single in-place timestamp mutation: replace the target row's timestamp column
/// with <c>NOW() + OffsetFromNow</c>. Use a negative offset to shift into the past
/// (e.g., <c>TimeSpan.FromSeconds(-3)</c> for "3 seconds ago").
/// </summary>
public sealed record TimestampShift(long Id, TimeSpan OffsetFromNow);

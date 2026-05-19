namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// A single in-place timestamp mutation: replace the target row's timestamp column
/// with <c>date_trunc('day', NOW()) + Offset</c>. The midnight anchor makes calendar
/// buckets (today / yesterday / last week) robust regardless of what time of day
/// the test happens to run — mirrors the legacy seed's
/// <c>date_trunc('day', NOW()) + INTERVAL '...'</c> pattern.
///
/// Examples:
/// <list type="bullet">
///   <item><c>TimeSpan.FromHours(1)</c> → today at 01:00 local-midnight time</item>
///   <item><c>TimeSpan.FromDays(-1) + TimeSpan.FromHours(12)</c> → yesterday at noon</item>
///   <item><c>TimeSpan.FromDays(-8) + TimeSpan.FromHours(12)</c> → 8 days ago at noon</item>
/// </list>
/// </summary>
public sealed record TimestampShift(long Id, TimeSpan Offset);

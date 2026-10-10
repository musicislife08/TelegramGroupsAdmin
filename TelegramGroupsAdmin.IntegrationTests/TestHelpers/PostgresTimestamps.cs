namespace TelegramGroupsAdmin.IntegrationTests.TestHelpers;

/// <summary>
/// PostgreSQL stores timestamps to the microsecond, .NET to 100 ns, so a value written with
/// <c>DateTimeOffset.UtcNow</c> reads back up to 0.9 µs earlier. Bracket an app-side write with
/// <c>FloorToMicrosecond(before)</c> .. <c>CeilingToMicrosecond(after)</c> to assert it exactly.
/// </summary>
public static class PostgresTimestamps
{
    public static DateTimeOffset FloorToMicrosecond(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    public static DateTimeOffset CeilingToMicrosecond(DateTimeOffset value)
    {
        var floor = FloorToMicrosecond(value);
        return floor == value ? value : floor.AddTicks(TimeSpan.TicksPerMicrosecond);
    }
}

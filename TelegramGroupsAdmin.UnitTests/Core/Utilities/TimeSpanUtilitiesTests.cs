using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

/// <summary>
/// Unit tests for TimeSpanUtilities.
/// Tests duration parsing (e.g., "5m", "1h", "7d") and formatting.
/// Used for ban durations, cleanup intervals, and other time-based configurations.
/// </summary>
[TestFixture]
public class TimeSpanUtilitiesTests
{
    #region TryParseDuration - Minutes

    [Test]
    public void TryParseDuration_Minutes_ParsesCorrectly()
    {
        var result = TimeSpanUtilities.TryParseDuration("5m", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [TestCase("1m", 1)]
    [TestCase("5m", 5)]
    [TestCase("15m", 15)]
    [TestCase("60m", 60)]
    [TestCase("120m", 120)]
    public void TryParseDuration_Minutes_VariousValues_ParsesCorrectly(string input, int expectedMinutes)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalMinutes, Is.EqualTo(expectedMinutes));
    }

    [Test]
    public void TryParseDuration_Minutes_LowercaseOnly()
    {
        // Uppercase 'M' is months, not minutes
        var resultLower = TimeSpanUtilities.TryParseDuration("5m", out var durationLower);
        var resultUpper = TimeSpanUtilities.TryParseDuration("5M", out var durationUpper);

        Assert.That(resultLower, Is.True);
        Assert.That(durationLower, Is.EqualTo(TimeSpan.FromMinutes(5)));

        Assert.That(resultUpper, Is.True);
        Assert.That(durationUpper, Is.EqualTo(TimeSpan.FromDays(150))); // 5 months = 150 days
    }

    #endregion

    #region TryParseDuration - Hours

    [Test]
    public void TryParseDuration_Hours_ParsesCorrectly()
    {
        var result = TimeSpanUtilities.TryParseDuration("1h", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromHours(1)));
    }

    [TestCase("1h", 1)]
    [TestCase("6h", 6)]
    [TestCase("12h", 12)]
    [TestCase("24h", 24)]
    [TestCase("48h", 48)]
    [TestCase("720h", 720)]
    public void TryParseDuration_Hours_VariousValues_ParsesCorrectly(string input, int expectedHours)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalHours, Is.EqualTo(expectedHours));
    }

    [Test]
    public void TryParseDuration_Hours_CaseInsensitive()
    {
        var resultLower = TimeSpanUtilities.TryParseDuration("5h", out var durationLower);
        var resultUpper = TimeSpanUtilities.TryParseDuration("5H", out var durationUpper);

        Assert.That(resultLower, Is.True);
        Assert.That(resultUpper, Is.True);
        Assert.That(durationLower, Is.EqualTo(durationUpper));
    }

    #endregion

    #region TryParseDuration - Days

    [Test]
    public void TryParseDuration_Days_ParsesCorrectly()
    {
        var result = TimeSpanUtilities.TryParseDuration("7d", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromDays(7)));
    }

    [TestCase("1d", 1)]
    [TestCase("3d", 3)]
    [TestCase("7d", 7)]
    [TestCase("14d", 14)]
    [TestCase("30d", 30)]
    [TestCase("60d", 60)]
    public void TryParseDuration_Days_VariousValues_ParsesCorrectly(string input, int expectedDays)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalDays, Is.EqualTo(expectedDays));
    }

    [Test]
    public void TryParseDuration_Days_CaseInsensitive()
    {
        var resultLower = TimeSpanUtilities.TryParseDuration("7d", out var durationLower);
        var resultUpper = TimeSpanUtilities.TryParseDuration("7D", out var durationUpper);

        Assert.That(resultLower, Is.True);
        Assert.That(resultUpper, Is.True);
        Assert.That(durationLower, Is.EqualTo(durationUpper));
    }

    #endregion

    #region TryParseDuration - Weeks

    [Test]
    public void TryParseDuration_Weeks_ParsesCorrectly()
    {
        var result = TimeSpanUtilities.TryParseDuration("2w", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromDays(14)));
    }

    [TestCase("1w", 7)]
    [TestCase("2w", 14)]
    [TestCase("4w", 28)]
    [TestCase("52w", 364)]
    public void TryParseDuration_Weeks_VariousValues_ParsesCorrectly(string input, int expectedDays)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalDays, Is.EqualTo(expectedDays));
    }

    [Test]
    public void TryParseDuration_Weeks_CaseInsensitive()
    {
        var resultLower = TimeSpanUtilities.TryParseDuration("2w", out var durationLower);
        var resultUpper = TimeSpanUtilities.TryParseDuration("2W", out var durationUpper);

        Assert.That(resultLower, Is.True);
        Assert.That(resultUpper, Is.True);
        Assert.That(durationLower, Is.EqualTo(durationUpper));
    }

    #endregion

    #region TryParseDuration - Months (Case Sensitive!)

    [Test]
    public void TryParseDuration_Months_UppercaseM_ParsesAsMonths()
    {
        // Uppercase 'M' = months (case-sensitive!)
        var result = TimeSpanUtilities.TryParseDuration("1M", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromDays(30)));
    }

    [Test]
    public void TryParseDuration_Months_LowercaseM_ParsesAsMinutes()
    {
        // Lowercase 'm' = minutes (case-sensitive!)
        var result = TimeSpanUtilities.TryParseDuration("1m", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [TestCase("1M", 30)]
    [TestCase("2M", 60)]
    [TestCase("3M", 90)]
    [TestCase("6M", 180)]
    [TestCase("12M", 360)]
    public void TryParseDuration_Months_VariousValues_ParsesCorrectly(string input, int expectedDays)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalDays, Is.EqualTo(expectedDays));
    }

    #endregion

    #region TryParseDuration - Years

    [Test]
    public void TryParseDuration_Years_ParsesCorrectly()
    {
        var result = TimeSpanUtilities.TryParseDuration("1y", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromDays(365)));
    }

    [TestCase("1y", 365)]
    [TestCase("2y", 730)]
    [TestCase("5y", 1825)]
    [TestCase("10y", 3650)]
    public void TryParseDuration_Years_VariousValues_ParsesCorrectly(string input, int expectedDays)
    {
        var result = TimeSpanUtilities.TryParseDuration(input, out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration.TotalDays, Is.EqualTo(expectedDays));
    }

    [Test]
    public void TryParseDuration_Years_CaseInsensitive()
    {
        var resultLower = TimeSpanUtilities.TryParseDuration("2y", out var durationLower);
        var resultUpper = TimeSpanUtilities.TryParseDuration("2Y", out var durationUpper);

        Assert.That(resultLower, Is.True);
        Assert.That(resultUpper, Is.True);
        Assert.That(durationLower, Is.EqualTo(durationUpper));
    }

    #endregion

    #region TryParseDuration - Whitespace Handling

    [Test]
    public void TryParseDuration_LeadingWhitespace_TrimsAndParses()
    {
        var result = TimeSpanUtilities.TryParseDuration("  5m", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void TryParseDuration_TrailingWhitespace_TrimsAndParses()
    {
        var result = TimeSpanUtilities.TryParseDuration("5m  ", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void TryParseDuration_BothSidesWhitespace_TrimsAndParses()
    {
        var result = TimeSpanUtilities.TryParseDuration("  5m  ", out var duration);

        Assert.That(result, Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    #endregion

    #region TryParseDuration - Invalid Input

    [Test]
    public void TryParseDuration_NoUnit_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("5", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_NoNumber_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("m", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_InvalidUnit_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("5x", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_EmptyString_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_OnlyWhitespace_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("   ", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_DecimalNumber_ReturnsFalse()
    {
        // int.TryParse won't parse decimals
        var result = TimeSpanUtilities.TryParseDuration("5.5h", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_SpaceBetweenNumberAndUnit_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("5 m", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_WordFormat_ReturnsFalse()
    {
        // Word formats are not supported
        var result = TimeSpanUtilities.TryParseDuration("1day", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TryParseDuration_NegativeNumber_ReturnsFalse()
    {
        var result = TimeSpanUtilities.TryParseDuration("-5m", out var duration);

        Assert.That(result, Is.False);
        Assert.That(duration, Is.EqualTo(TimeSpan.Zero));
    }

    #endregion

    #region FormatDuration - Minutes

    [Test]
    public void FormatDuration_OneMinute_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromMinutes(1));

        Assert.That(result, Is.EqualTo("1 minute"));
    }

    [Test]
    public void FormatDuration_MultipleMinutes_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromMinutes(5));

        Assert.That(result, Is.EqualTo("5 minutes"));
    }

    [Test]
    public void FormatDuration_ZeroMinutes_ReturnsPluralZero()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.Zero);

        Assert.That(result, Is.EqualTo("0 minutes"));
    }

    [Test]
    public void FormatDuration_59Minutes_ReturnsMinutes()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromMinutes(59));

        Assert.That(result, Is.EqualTo("59 minutes"));
    }

    #endregion

    #region FormatDuration - Hours

    [Test]
    public void FormatDuration_OneHour_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromHours(1));

        Assert.That(result, Is.EqualTo("1 hour"));
    }

    [Test]
    public void FormatDuration_MultipleHours_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromHours(5));

        Assert.That(result, Is.EqualTo("5 hours"));
    }

    [Test]
    public void FormatDuration_60Minutes_ReturnsOneHour()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromMinutes(60));

        Assert.That(result, Is.EqualTo("1 hour"));
    }

    [Test]
    public void FormatDuration_23Hours_ReturnsHours()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromHours(23));

        Assert.That(result, Is.EqualTo("23 hours"));
    }

    #endregion

    #region FormatDuration - Days

    [Test]
    public void FormatDuration_OneDay_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(1));

        Assert.That(result, Is.EqualTo("1 day"));
    }

    [Test]
    public void FormatDuration_MultipleDays_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(5));

        Assert.That(result, Is.EqualTo("5 days"));
    }

    [Test]
    public void FormatDuration_24Hours_ReturnsOneDay()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromHours(24));

        Assert.That(result, Is.EqualTo("1 day"));
    }

    [Test]
    public void FormatDuration_6Days_ReturnsDays()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(6));

        Assert.That(result, Is.EqualTo("6 days"));
    }

    #endregion

    #region FormatDuration - Weeks

    [Test]
    public void FormatDuration_OneWeek_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(7));

        Assert.That(result, Is.EqualTo("1 week"));
    }

    [Test]
    public void FormatDuration_MultipleWeeks_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(14));

        Assert.That(result, Is.EqualTo("2 weeks"));
    }

    [Test]
    public void FormatDuration_29Days_ReturnsFourWeeks()
    {
        // 29 days = 4 weeks (integer division)
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(29));

        Assert.That(result, Is.EqualTo("4 weeks"));
    }

    #endregion

    #region FormatDuration - Months

    [Test]
    public void FormatDuration_OneMonth_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(30));

        Assert.That(result, Is.EqualTo("1 month"));
    }

    [Test]
    public void FormatDuration_MultipleMonths_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(90));

        Assert.That(result, Is.EqualTo("3 months"));
    }

    [Test]
    public void FormatDuration_364Days_Returns12Months()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(364));

        Assert.That(result, Is.EqualTo("12 months"));
    }

    #endregion

    #region FormatDuration - Years

    [Test]
    public void FormatDuration_OneYear_ReturnsSingular()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(365));

        Assert.That(result, Is.EqualTo("1 year"));
    }

    [Test]
    public void FormatDuration_MultipleYears_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(730));

        Assert.That(result, Is.EqualTo("2 years"));
    }

    [Test]
    public void FormatDuration_5Years_ReturnsPlural()
    {
        var result = TimeSpanUtilities.FormatDuration(TimeSpan.FromDays(1825));

        Assert.That(result, Is.EqualTo("5 years"));
    }

    #endregion

    #region Round Trip Tests

    [Test]
    public void RoundTrip_Minutes_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("30m", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("30 minutes"));
    }

    [Test]
    public void RoundTrip_Hours_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("6h", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("6 hours"));
    }

    [Test]
    public void RoundTrip_Days_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("3d", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("3 days"));
    }

    [Test]
    public void RoundTrip_Weeks_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("2w", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("2 weeks"));
    }

    [Test]
    public void RoundTrip_Months_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("3M", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("3 months"));
    }

    [Test]
    public void RoundTrip_Years_ParseAndFormatConsistent()
    {
        TimeSpanUtilities.TryParseDuration("2y", out var parsed);
        var formatted = TimeSpanUtilities.FormatDuration(parsed);

        Assert.That(formatted, Is.EqualTo("2 years"));
    }

    #endregion
}

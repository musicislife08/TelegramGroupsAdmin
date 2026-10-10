using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Tests for FileScanQuotaModel.PercentageUsed, which the File Scanning settings page shows as
/// the quota usage bar.
/// </summary>
[TestFixture]
public class FileScanQuotaModelTests
{
    private static FileScanQuotaModel Quota(int count, int limit) => new(
        Id: 1,
        Service: "VirusTotal",
        QuotaType: "daily",
        QuotaWindowStart: DateTimeOffset.UnixEpoch,
        QuotaWindowEnd: DateTimeOffset.UnixEpoch.AddDays(1),
        Count: count,
        LimitValue: limit,
        LastUpdated: DateTimeOffset.UnixEpoch);

    [TestCase(0, 500, 0.0)]
    [TestCase(125, 500, 25.0)]
    [TestCase(1, 3, 100.0 / 3)]
    [TestCase(500, 500, 100.0)]
    [TestCase(600, 500, 120.0)]
    public void PercentageUsed_IsCountOverLimit(int count, int limit, double expected)
    {
        Assert.That(Quota(count, limit).PercentageUsed, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PercentageUsed_NoPositiveLimit_IsZero(int limit)
    {
        Assert.That(Quota(5, limit).PercentageUsed, Is.Zero);
    }
}

using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// Guards the JSON shape of canonical <c>reports.context</c> against the bootstrap sanitizer.
/// The length-preserving lorem sanitizer once rewrote three profile-scan <c>aiSignals</c> arrays
/// as single strings (canonical edit 2026-10-01 restored them); production has only ever written
/// arrays, and a string there makes <c>GetProfileScanAlertsAsync</c> throw. See CLAUDE.md Part 2
/// "Reports".
/// </summary>
[TestFixture]
public class CanonicalReportContextShapeTests
{
    private MigrationTestHelper? _helper;

    [SetUp]
    public async Task Setup()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    [Test]
    public async Task NoReportContext_StoresAiSignalsAsAString()
    {
        var stringShaped = await _helper!.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reports WHERE jsonb_typeof(context->'aiSignals') = 'string'");

        Assert.That(stringShaped, Is.EqualTo(0),
            "canonical edit 2026-10-01: every reports.context aiSignals must be a JSON array (sanitizer artifact)");
    }

    [Test]
    public async Task EveryProfileScanAlertContext_StoresAiSignalsAsAnArrayOfStrings()
    {
        var profileScanAlerts = await _helper!.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM reports WHERE type = 3");
        var arrayShaped = await _helper.ExecuteScalarAsync<long>(
            """
            SELECT count(*) FROM reports
            WHERE type = 3
              AND jsonb_typeof(context->'aiSignals') = 'array'
              AND NOT EXISTS (
                  SELECT 1 FROM jsonb_array_elements(context->'aiSignals') AS e
                  WHERE jsonb_typeof(e) <> 'string')
            """);

        Assert.That(profileScanAlerts, Is.GreaterThan(0), "canonical must carry profile-scan alerts");
        Assert.That(arrayShaped, Is.EqualTo(profileScanAlerts),
            "canonical edit 2026-10-01: every profile-scan alert context must carry a string-array aiSignals");
    }
}

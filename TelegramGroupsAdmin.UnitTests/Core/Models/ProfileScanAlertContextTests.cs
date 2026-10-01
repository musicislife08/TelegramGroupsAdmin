using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

/// <summary>
/// reports.context for profile-scan alerts: rows written before the alert context carried an array
/// store "aiSignals" as one comma-separated string. One such row must not stop the whole Reports
/// queue from loading, so the context accepts both shapes and always writes the array.
/// </summary>
[TestFixture]
public class ProfileScanAlertContextTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Test]
    public void Deserialize_ArrayAiSignals_ReadsEveryEntry()
    {
        var context = JsonSerializer.Deserialize<ProfileScanAlertContext>(
            """{"userId": 1, "score": 0.9, "outcome": 1, "aiSignals": ["crypto promotion", "spam bio"]}""", Options);

        Assert.That(context!.AiSignals, Is.EqualTo(new[] { "crypto promotion", "spam bio" }));
    }

    [Test]
    public void Deserialize_LegacyCommaSeparatedAiSignals_SplitsIntoEntries()
    {
        var context = JsonSerializer.Deserialize<ProfileScanAlertContext>(
            """{"userId": 1, "score": 0.9, "outcome": 1, "aiSignals": "crypto promotion, spam bio,, "}""", Options);

        Assert.That(context!.AiSignals, Is.EqualTo(new[] { "crypto promotion", "spam bio" }));
    }

    [TestCase("""{"userId": 1, "aiSignals": null}""")]
    [TestCase("""{"userId": 1}""")]
    public void Deserialize_MissingOrNullAiSignals_IsNull(string json)
    {
        var context = JsonSerializer.Deserialize<ProfileScanAlertContext>(json, Options);

        Assert.That(context!.AiSignals, Is.Null);
    }

    [Test]
    public void Deserialize_EmptyLegacyString_IsEmptyArray()
    {
        var context = JsonSerializer.Deserialize<ProfileScanAlertContext>(
            """{"userId": 1, "aiSignals": ""}""", Options);

        Assert.That(context!.AiSignals, Is.Empty);
    }

    [Test]
    public void Deserialize_UnsupportedAiSignalsShape_Throws()
    {
        Assert.That(
            () => JsonSerializer.Deserialize<ProfileScanAlertContext>("""{"userId": 1, "aiSignals": 42}""", Options),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void Serialize_WritesAiSignalsAsArray()
    {
        var json = JsonSerializer.Serialize(new ProfileScanAlertContext { UserId = 1, AiSignals = ["a", "b"] });

        using var document = JsonDocument.Parse(json);
        var signals = document.RootElement.GetProperty("aiSignals");
        Assert.That(signals.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(signals.EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Serialize_NullAiSignals_WritesNull()
    {
        var json = JsonSerializer.Serialize(new ProfileScanAlertContext { UserId = 1, AiSignals = null });

        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("aiSignals").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
}

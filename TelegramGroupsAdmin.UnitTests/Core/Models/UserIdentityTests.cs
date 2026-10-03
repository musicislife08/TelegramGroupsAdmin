using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

[TestFixture]
public class UserIdentityTests
{
    [TestCase(NameVerdict.Unscanned, NameMasking.On, "Sofia R")]
    [TestCase(NameVerdict.Clean, NameMasking.On, "Sofia R")]
    [TestCase(NameVerdict.Explicit, NameMasking.On, "[name removed: explicit]")]
    [TestCase(NameVerdict.Promotional, NameMasking.On, "[name removed: spam]")]
    [TestCase(NameVerdict.Explicit, NameMasking.Off, "Sofia R")]
    [TestCase(NameVerdict.Promotional, NameMasking.Off, "Sofia R")]
    public void BotDisplayName_MapsVerdictAndMasking(NameVerdict verdict, NameMasking masking, string expected)
    {
        var identity = UserIdentity.ForTest(42, "Sofia", "R", verdict: verdict);

        Assert.That(identity.BotDisplayName(masking), Is.EqualTo(expected));
    }

    [Test]
    public void BotDisplayName_IdOnlyIdentity_FallsBackToUserId()
    {
        var identity = UserIdentity.ForTest(42);

        Assert.That(identity.BotDisplayName(NameMasking.On), Is.EqualTo("User 42"));
    }

    [Test]
    public void DisplayName_IsRealNameRegardlessOfVerdict()
    {
        var identity = UserIdentity.ForTest(42, "Sofia", "R", verdict: NameVerdict.Explicit);

        Assert.That(identity.DisplayName, Is.EqualTo("Sofia R"));
    }

    [Test]
    public void Deserialize_PayloadWithoutVerdict_DefaultsToUnscanned()
    {
        // Shape System.Text.Json wrote for queued Quartz payloads before Verdict existed.
        const string json = """{"Id":42,"FirstName":"Sofia","LastName":"R","Username":null,"DisplayName":"Sofia R"}""";

        var identity = JsonSerializer.Deserialize<UserIdentity>(json)!;

        Assert.That(identity.Id, Is.EqualTo(42));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public void Serialize_RoundTripsVerdict()
    {
        var identity = UserIdentity.ForTest(42, "Sofia", verdict: NameVerdict.Explicit);

        var copy = JsonSerializer.Deserialize<UserIdentity>(JsonSerializer.Serialize(identity))!;

        Assert.That(copy.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }
}

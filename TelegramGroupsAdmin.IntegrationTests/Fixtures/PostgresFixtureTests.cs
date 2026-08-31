using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;

namespace TelegramGroupsAdmin.IntegrationTests.Fixtures;

[TestFixture]
public class PostgresFixtureTests
{
    [Test]
    public void SharedDataProtectionProvider_IsEphemeral()
    {
        var provider = PostgresFixture.SharedDataProtectionProvider;
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider, Is.InstanceOf<EphemeralDataProtectionProvider>());
    }

    [Test]
    public void SharedDataProtectionProvider_ReturnsSameInstance()
    {
        var first = PostgresFixture.SharedDataProtectionProvider;
        var second = PostgresFixture.SharedDataProtectionProvider;
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void SharedDataProtectionProvider_RoundTripsCiphertext()
    {
        var protector = PostgresFixture.SharedDataProtectionProvider.CreateProtector("test");
        var protectedText = protector.Protect("hello");
        Assert.That(protector.Unprotect(protectedText), Is.EqualTo("hello"));
    }
}

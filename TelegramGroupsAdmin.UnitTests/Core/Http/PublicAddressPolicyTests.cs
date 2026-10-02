using System.Net;
using TelegramGroupsAdmin.Core.Http;

namespace TelegramGroupsAdmin.UnitTests.Core.Http;

/// <summary>
/// The address policy behind the public-url HttpClient: every non-public range is refused, in its
/// IPv4, IPv6 and IPv4-embedded-in-IPv6 forms, and ordinary public addresses pass.
/// </summary>
[TestFixture]
public class PublicAddressPolicyTests
{
    // IPv4
    [TestCase("127.0.0.1", "loopback")]
    [TestCase("127.255.255.254", "loopback")]
    [TestCase("10.0.0.1", "private")]
    [TestCase("10.255.255.255", "private")]
    [TestCase("172.16.0.1", "private")]
    [TestCase("172.31.255.255", "private")]
    [TestCase("192.168.1.1", "private")]
    [TestCase("169.254.169.254", "link-local")]
    [TestCase("169.254.0.1", "link-local")]
    [TestCase("0.0.0.0", "unspecified")]
    [TestCase("0.1.2.3", "unspecified")]
    [TestCase("100.64.0.1", "carrier-grade NAT")]
    [TestCase("100.127.255.255", "carrier-grade NAT")]
    [TestCase("224.0.0.1", "multicast")]
    [TestCase("239.255.255.255", "multicast")]
    [TestCase("255.255.255.255", "reserved")]
    [TestCase("240.0.0.1", "reserved")]
    [TestCase("192.0.0.1", "reserved")]
    [TestCase("192.0.2.1", "documentation")]
    [TestCase("198.51.100.1", "documentation")]
    [TestCase("203.0.113.1", "documentation")]
    [TestCase("198.18.0.1", "benchmarking")]
    [TestCase("192.88.99.1", "6to4 relay anycast")]
    // IPv6
    [TestCase("::1", "loopback")]
    [TestCase("::", "unspecified")]
    [TestCase("fc00::1", "unique-local")]
    [TestCase("fd12:3456:789a::1", "unique-local")]
    [TestCase("fe80::1", "link-local")]
    [TestCase("febf::1", "link-local")]
    [TestCase("fec0::1", "site-local")]
    [TestCase("ff02::1", "multicast")]
    [TestCase("2001:db8::1", "documentation")]
    [TestCase("100::1", "discard")]
    [TestCase("100::ffff:ffff:ffff:ffff", "discard")]
    [TestCase("64:ff9b:1::7f00:1", "local-use NAT64")]
    [TestCase("64:ff9b:1:abcd::808:808", "local-use NAT64")]
    // IPv4 embedded in IPv6
    [TestCase("::ffff:127.0.0.1", "loopback")]
    [TestCase("::ffff:10.0.0.1", "private")]
    [TestCase("::ffff:192.168.0.1", "private")]
    [TestCase("::ffff:169.254.169.254", "link-local")]
    [TestCase("::ffff:0.0.0.0", "unspecified")]
    [TestCase("::127.0.0.1", "loopback")]
    [TestCase("::10.0.0.1", "private")]
    [TestCase("64:ff9b::7f00:1", "loopback")]
    [TestCase("64:ff9b::a00:1", "private")]
    [TestCase("2002:7f00:1::", "loopback")]
    [TestCase("2002:c0a8:1::", "private")]
    [TestCase("::ffff:0:7f00:1", "loopback")]
    [TestCase("::ffff:0:a00:1", "private")]
    [TestCase("::ffff:0:a9fe:a9fe", "link-local")]
    public void IsPublic_NonPublicAddress_IsRefusedWithReason(string address, string expectedReason)
    {
        var isPublic = PublicAddressPolicy.IsPublic(IPAddress.Parse(address), out var reason);

        Assert.That(isPublic, Is.False, $"{address} must be refused");
        Assert.That(reason, Does.Contain(expectedReason));
    }

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("93.184.216.34")]
    [TestCase("172.32.0.1")]
    [TestCase("172.15.255.255")]
    [TestCase("100.128.0.1")]
    [TestCase("11.0.0.1")]
    [TestCase("2606:4700::1111")]
    [TestCase("2001:4860:4860::8888")]
    [TestCase("::ffff:8.8.8.8")]
    [TestCase("64:ff9b::808:808")]
    [TestCase("64:ff9c::1")]
    [TestCase("101::1")]
    [TestCase("2001::1")]
    [TestCase("2002:808:808::")]
    [TestCase("::ffff:0:808:808")]
    public void IsPublic_PublicAddress_IsAccepted(string address)
    {
        var isPublic = PublicAddressPolicy.IsPublic(IPAddress.Parse(address), out var reason);

        Assert.That(isPublic, Is.True, $"{address} must be accepted but was refused: {reason}");
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void NoFetchAllowance_AllowsNothing()
    {
        var allowance = new NoFetchAllowance();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowance.IsAllowed(IPAddress.Loopback, 80), Is.False);
            Assert.That(allowance.IsAllowed(IPAddress.IPv6Loopback, 443), Is.False);
            Assert.That(allowance.IsAllowed(IPAddress.Parse("8.8.8.8"), 80), Is.False,
                "the allowance only ever adds exceptions; public addresses are the policy's business");
        }
    }

    [Test]
    public void LoopbackPortAllowance_AllowsOnlyLoopbackOnTheListedPorts()
    {
        var allowance = new LoopbackPortAllowance(5001);
        allowance.AllowPort(5002);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowance.IsAllowed(IPAddress.Loopback, 5001), Is.True);
            Assert.That(allowance.IsAllowed(IPAddress.IPv6Loopback, 5002), Is.True);
            Assert.That(allowance.IsAllowed(IPAddress.Parse("::ffff:127.0.0.1"), 5001), Is.True);
            Assert.That(allowance.IsAllowed(IPAddress.Loopback, 5003), Is.False, "a port that was never listed");
            Assert.That(allowance.IsAllowed(IPAddress.Parse("10.0.0.1"), 5001), Is.False, "private, not loopback");
            Assert.That(allowance.IsAllowed(IPAddress.Parse("169.254.169.254"), 5001), Is.False, "link-local, not loopback");
        }
    }
}

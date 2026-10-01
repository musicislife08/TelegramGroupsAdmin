using TelegramGroupsAdmin.Core.Http;

namespace TelegramGroupsAdmin.UnitTests.Core.Http;

/// <summary>
/// A push subscription endpoint comes from the browser of a logged-in user and the server POSTs
/// to it later. The policy accepts only https URLs without userinfo whose host, when it is an IP
/// literal, is public; hostnames are judged at connect time by the guarded handler.
/// </summary>
[TestFixture]
public class PushEndpointPolicyTests
{
    [TestCase("https://fcm.googleapis.com/fcm/send/abc:APA91b")]
    [TestCase("https://updates.push.services.mozilla.com/wpush/v2/gAAAA")]
    [TestCase("https://web.push.apple.com/QGxyz")]
    [TestCase("https://wns2-bl2p.notify.windows.com/w/?token=AwYAAA")]
    [TestCase("https://8.8.8.8/push")]
    [TestCase("https://[2001:4860:4860::8888]/push")]
    public void IsAcceptable_PublicHttpsEndpoint_IsAccepted(string endpoint)
    {
        Assert.That(PushEndpointPolicy.IsAcceptable(endpoint, out var reason), Is.True, reason);
    }

    [TestCase("http://fcm.googleapis.com/fcm/send/abc", "https")]
    [TestCase("http://localhost:5000/push", "https")]
    [TestCase("ftp://fcm.googleapis.com/x", "https")]
    [TestCase("https://user:pw@fcm.googleapis.com/x", "userinfo")]
    [TestCase("https://127.0.0.1/push", "loopback")]
    [TestCase("https://127.1/push", "loopback")]
    [TestCase("https://2130706433/push", "loopback")]
    [TestCase("https://[::1]/push", "loopback")]
    [TestCase("https://[::ffff:127.0.0.1]/push", "loopback")]
    [TestCase("https://10.0.0.1/push", "private")]
    [TestCase("https://192.168.1.1/push", "private")]
    [TestCase("https://172.16.0.1/push", "private")]
    [TestCase("https://169.254.169.254/latest/meta-data/", "link-local")]
    [TestCase("https://0.0.0.0/push", "unspecified")]
    [TestCase("https://[fd00::1]/push", "unique-local")]
    public void IsAcceptable_NonHttpsOrNonPublicLiteral_IsRejected(string endpoint, string expectedReasonFragment)
    {
        Assert.That(PushEndpointPolicy.IsAcceptable(endpoint, out var reason), Is.False);
        Assert.That(reason, Does.Contain(expectedReasonFragment).IgnoreCase);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("fcm.googleapis.com/fcm/send/abc")]
    [TestCase("//fcm.googleapis.com/x")]
    [TestCase("https:///push")]
    [TestCase(null)]
    public void IsAcceptable_MalformedEndpoint_IsRejected(string? endpoint)
    {
        Assert.That(PushEndpointPolicy.IsAcceptable(endpoint, out var reason), Is.False);
        Assert.That(reason, Is.Not.Empty);
    }
}

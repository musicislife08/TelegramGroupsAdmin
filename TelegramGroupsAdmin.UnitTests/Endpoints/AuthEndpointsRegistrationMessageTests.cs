using TelegramGroupsAdmin.Endpoints;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.UnitTests.Endpoints;

/// <summary>
/// The register endpoint's success copy must match what actually happened: it may only say a verification
/// link was sent when the send succeeded.
/// </summary>
[TestFixture]
public class AuthEndpointsRegistrationMessageTests
{
    [Test]
    public void RegistrationSuccessMessage_VerificationEmailSent_SaysTheLinkWasSent()
    {
        var message = AuthEndpoints.RegistrationSuccessMessage(
            new RegisterResult(true, "id", null, EmailVerificationRequired: true));

        Assert.That(message, Does.Contain("We've sent a verification link"));
    }

    [Test]
    public void RegistrationSuccessMessage_VerificationEmailFailed_DoesNotClaimASendAndPointsToResend()
    {
        var message = AuthEndpoints.RegistrationSuccessMessage(
            new RegisterResult(true, "id", null, EmailVerificationRequired: true, VerificationEmailFailed: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Not.Contain("We've sent"));
            Assert.That(message, Does.Contain("couldn't send the verification email"));
            Assert.That(message, Does.Contain("Resend verification email"));
        }
    }

    [Test]
    public void RegistrationSuccessMessage_NoVerificationRequired_SaysPleaseLogIn()
    {
        var message = AuthEndpoints.RegistrationSuccessMessage(new RegisterResult(true, "id", null));

        Assert.That(message, Is.EqualTo("Account created successfully! Please log in."));
    }
}

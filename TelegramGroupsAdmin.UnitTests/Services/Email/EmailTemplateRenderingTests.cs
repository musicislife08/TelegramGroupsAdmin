using TelegramGroupsAdmin.Ui.Server.Services.Email;

namespace TelegramGroupsAdmin.UnitTests.Services.Email;

/// <summary>
/// Tests for EmailTemplateData rendering to verify each template type
/// produces correct subject lines and includes all required parameters in the body.
/// </summary>
[TestFixture]
public class EmailTemplateRenderingTests
{
    [Test]
    public void PasswordReset_RendersWithResetLinkAndExpiry()
    {
        // Arrange
        var resetLink = "https://example.com/reset?token=abc123";
        var expiryMinutes = 30;
        var template = new EmailTemplateData.PasswordReset(resetLink, expiryMinutes);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Password Reset"));
            Assert.That(body, Does.Contain(resetLink), "Body should contain the reset link");
            Assert.That(body, Does.Contain("30 minutes"), "Body should contain expiry time");
            Assert.That(body, Does.Contain("href="), "Body should contain HTML link");
        });
    }

    [Test]
    public void EmailVerification_RendersWithTokenAndBaseUrl()
    {
        // Arrange
        var token = "verify-token-xyz";
        var baseUrl = "https://example.com";
        var template = new EmailTemplateData.EmailVerification(token, baseUrl);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Verify"));
            Assert.That(body, Does.Contain(token), "Body should contain verification token");
            Assert.That(body, Does.Contain(baseUrl), "Body should contain base URL");
            Assert.That(body, Does.Contain("/verify-email?token="), "Body should contain verification path");
        });
    }

    [Test]
    public void WelcomeEmail_RendersWithEmailAndLoginUrl()
    {
        // Arrange
        var email = "user@example.com";
        var loginUrl = "https://example.com/login";
        var template = new EmailTemplateData.WelcomeEmail(email, loginUrl);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Welcome"));
            Assert.That(body, Does.Contain(email), "Body should contain user email");
            Assert.That(body, Does.Contain(loginUrl), "Body should contain login URL");
            Assert.That(body, Does.Contain("Two-Factor Authentication"), "Body should mention 2FA recommendation");
        });
    }

    [Test]
    public void InviteCreated_RendersWithLinkInviterAndExpiry()
    {
        // Arrange
        var inviteLink = "https://example.com/register?invite=abc";
        var invitedBy = "admin@example.com";
        var expiryDays = 7;
        var template = new EmailTemplateData.InviteCreated(inviteLink, invitedBy, expiryDays);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Invited"));
            Assert.That(body, Does.Contain(inviteLink), "Body should contain invite link");
            Assert.That(body, Does.Contain(invitedBy), "Body should contain inviter name");
            Assert.That(body, Does.Contain("7 days"), "Body should contain expiry period");
        });
    }

    [Test]
    public void AccountDisabled_RendersWithEmailAndReason()
    {
        // Arrange
        var email = "disabled@example.com";
        var reason = "Policy violation";
        var template = new EmailTemplateData.AccountDisabled(email, reason);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Disabled"));
            Assert.That(body, Does.Contain(email), "Body should contain user email");
            Assert.That(body, Does.Contain(reason), "Body should contain disable reason");
        });
    }

    [Test]
    public void AccountLocked_RendersWithEmailLockedUntilAndAttempts()
    {
        // Arrange
        var email = "locked@example.com";
        var lockedUntil = new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var attempts = 5;
        var template = new EmailTemplateData.AccountLocked(email, lockedUntil, attempts);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Locked"));
            Assert.That(subject, Does.Contain("Security"), "Subject should indicate security alert");
            Assert.That(body, Does.Contain(email), "Body should contain user email");
            Assert.That(body, Does.Contain("5 failed login attempts"), "Body should contain attempt count");
            Assert.That(body, Does.Contain("2025-01-15"), "Body should contain lock date");
        });
    }

    [Test]
    public void AccountUnlocked_RendersWithEmail()
    {
        // Arrange
        var email = "unlocked@example.com";
        var template = new EmailTemplateData.AccountUnlocked(email);

        // Act
        var (subject, body) = RenderTemplate(template);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(subject, Does.Contain("Unlocked"));
            Assert.That(body, Does.Contain(email), "Body should contain user email");
            Assert.That(body, Does.Contain("administrator"), "Body should mention admin action");
        });
    }

    [Test]
    public void AllTemplates_ProduceHtmlContent()
    {
        // Arrange - create one of each template type
        var templates = new EmailTemplateData[]
        {
            new EmailTemplateData.PasswordReset("https://test.com/reset", 30),
            new EmailTemplateData.EmailVerification("token", "https://test.com"),
            new EmailTemplateData.WelcomeEmail("test@test.com", "https://test.com/login"),
            new EmailTemplateData.InviteCreated("https://test.com/invite", "admin", 7),
            new EmailTemplateData.AccountDisabled("test@test.com", "reason"),
            new EmailTemplateData.AccountLocked("test@test.com", DateTimeOffset.UtcNow, 3),
            new EmailTemplateData.AccountUnlocked("test@test.com")
        };

        foreach (var template in templates)
        {
            // Act
            var (subject, body) = RenderTemplate(template);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(subject, Is.Not.Null.And.Not.Empty, $"{template.GetType().Name} should have subject");
                Assert.That(body, Does.Contain("<html>"), $"{template.GetType().Name} should produce HTML");
                Assert.That(body, Does.Contain("</body>"), $"{template.GetType().Name} should have closing body tag");
            });
        }
    }

    [Test]
    public void AllTemplates_ContainSecurityFooter()
    {
        // Arrange - templates that should have security footer
        var securityTemplates = new EmailTemplateData[]
        {
            new EmailTemplateData.PasswordReset("https://test.com/reset", 30),
            new EmailTemplateData.AccountDisabled("test@test.com", "reason"),
            new EmailTemplateData.AccountLocked("test@test.com", DateTimeOffset.UtcNow, 3),
            new EmailTemplateData.AccountUnlocked("test@test.com")
        };

        foreach (var template in securityTemplates)
        {
            // Act
            var (_, body) = RenderTemplate(template);

            // Assert
            Assert.That(body, Does.Contain("Security Team").Or.Contain("TelegramGroupsAdmin"),
                $"{template.GetType().Name} should have footer");
        }
    }

    /// <summary>
    /// Uses reflection to call the private RenderTemplate method for testing.
    /// This allows us to test the rendering logic without needing to mock SendGrid.
    /// </summary>
    private static (string Subject, string Body) RenderTemplate(EmailTemplateData templateData)
    {
        var method = typeof(SendGridEmailService)
            .GetMethod("RenderTemplate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("RenderTemplate method not found");

        var result = method.Invoke(null, [templateData]);
        return ((string Subject, string Body))result!;
    }
}

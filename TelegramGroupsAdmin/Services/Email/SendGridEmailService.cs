using SendGrid;
using SendGrid.Helpers.Mail;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Email service implementation using SendGrid API
/// Configuration loaded from database (hot-reload support)
/// </summary>
public class SendGridEmailService : IEmailService
{
    private readonly ISystemConfigRepository _configRepo;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        ISystemConfigRepository configRepo,
        ILogger<SendGridEmailService> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken ct = default)
    {
        await SendEmailAsync([to], subject, body, isHtml, ct);
    }

    public async Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = true, CancellationToken ct = default)
    {
        // Load configuration from database (supports hot-reload)
        var sendGridConfig = await _configRepo.GetSendGridConfigAsync(ct);
        var apiKeys = await _configRepo.GetApiKeysAsync(ct);

        if (sendGridConfig?.Enabled != true)
        {
            _logger.LogWarning("SendGrid service is disabled. Email not sent to: {Recipients}", string.Join(", ", to));
            return;
        }

        var apiKey = apiKeys?.SendGrid;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("SendGrid API key not configured. Email not sent to: {Recipients}", string.Join(", ", to));
            throw new InvalidOperationException("SendGrid API key not configured");
        }

        if (string.IsNullOrWhiteSpace(sendGridConfig.FromAddress))
        {
            _logger.LogError("SendGrid FromAddress not configured. Email not sent to: {Recipients}", string.Join(", ", to));
            throw new InvalidOperationException("SendGrid FromAddress not configured");
        }

        try
        {
            var recipients = to.Select(email => new EmailAddress(email)).ToList();

            _logger.LogInformation("Attempting to send email via SendGrid to: {Recipients}, Subject: {Subject}",
                string.Join(", ", to), subject);

            var from = new EmailAddress(sendGridConfig.FromAddress, sendGridConfig.FromName);
            var msg = MailHelper.CreateSingleEmailToMultipleRecipients(
                from,
                recipients,
                subject,
                isHtml ? null : body,  // Plain text
                isHtml ? body : null   // HTML
            );

            // Create client with API key from database
            var client = new SendGridClient(apiKey);

            _logger.LogDebug("Sending email via SendGrid API...");
            var response = await client.SendEmailAsync(msg, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent successfully via SendGrid to: {Recipients}, Subject: {Subject}",
                    string.Join(", ", to), subject);
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync(ct);
                _logger.LogError("SendGrid API returned error. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode, responseBody);
                throw new Exception($"SendGrid API error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via SendGrid to: {Recipients}, Subject: {Subject}",
                string.Join(", ", to), subject);
            throw;
        }
    }

    public async Task SendTemplatedEmailAsync(string to, EmailTemplate template, Dictionary<string, string> parameters, CancellationToken ct = default)
    {
        var (subject, body) = GetTemplate(template, parameters);
        await SendEmailAsync(to, subject, body, isHtml: true, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            // Load configuration from database
            var sendGridConfig = await _configRepo.GetSendGridConfigAsync(ct);
            var apiKeys = await _configRepo.GetApiKeysAsync(ct);

            if (sendGridConfig?.Enabled != true)
            {
                _logger.LogWarning("SendGrid service is disabled. Connection test skipped.");
                return false;
            }

            var apiKey = apiKeys?.SendGrid;

            // SendGrid doesn't have a "test connection" endpoint, but we can verify the API key format
            if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith("SG."))
            {
                _logger.LogError("Invalid or missing SendGrid API key");
                return false;
            }

            if (string.IsNullOrWhiteSpace(sendGridConfig.FromAddress))
            {
                _logger.LogError("SendGrid FromAddress not configured");
                return false;
            }

            _logger.LogInformation("SendGrid configuration is valid");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendGrid connection test failed");
            return false;
        }
    }

    private (string Subject, string Body) GetTemplate(EmailTemplate template, Dictionary<string, string> parameters)
    {
        return template switch
        {
            EmailTemplate.PasswordReset => GetPasswordResetTemplate(parameters),
            EmailTemplate.EmailVerification => GetEmailVerificationTemplate(parameters),
            EmailTemplate.WelcomeEmail => GetWelcomeEmailTemplate(parameters),
            EmailTemplate.InviteCreated => GetInviteCreatedTemplate(parameters),
            EmailTemplate.AccountDisabled => GetAccountDisabledTemplate(parameters),
            EmailTemplate.AccountLocked => GetAccountLockedTemplate(parameters),
            EmailTemplate.AccountUnlocked => GetAccountUnlockedTemplate(parameters),
            _ => throw new ArgumentException($"Unknown email template: {template}")
        };
    }

    private (string Subject, string Body) GetPasswordResetTemplate(Dictionary<string, string> parameters)
    {
        var resetLink = parameters.GetValueOrDefault("resetLink", "#");
        var expiryMinutes = parameters.GetValueOrDefault("expiryMinutes", "15");

        var subject = "Password Reset Request";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2>Password Reset Request</h2>
                <p>You requested to reset your password. Click the button below to reset it:</p>
                <p style="margin: 30px 0;">
                    <a href="{resetLink}" style="background-color: #007bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">
                        Reset Password
                    </a>
                </p>
                <p>This link will expire in {expiryMinutes} minutes.</p>
                <p>If you didn't request this, please ignore this email.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin Security Team</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetEmailVerificationTemplate(Dictionary<string, string> parameters)
    {
        var verificationToken = parameters.GetValueOrDefault("VerificationToken", "");
        var baseUrl = parameters.GetValueOrDefault("BaseUrl", "http://localhost:5161");
        var verificationLink = $"{baseUrl}/verify-email?token={verificationToken}";

        var subject = "Verify Your Email Address";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2>Verify Your Email Address</h2>
                <p>Thanks for registering! Please verify your email address by clicking the button below:</p>
                <p style="margin: 30px 0;">
                    <a href="{verificationLink}" style="background-color: #28a745; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">
                        Verify Email
                    </a>
                </p>
                <p>If you didn't create this account, please ignore this email.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetWelcomeEmailTemplate(Dictionary<string, string> parameters)
    {
        var email = parameters.GetValueOrDefault("email", "");
        var loginUrl = parameters.GetValueOrDefault("loginUrl", "#");

        var subject = "Welcome to TelegramGroupsAdmin";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2>Welcome to TelegramGroupsAdmin!</h2>
                <p>Your account has been successfully created.</p>
                <p><strong>Email:</strong> {email}</p>
                <p style="margin: 30px 0;">
                    <a href="{loginUrl}" style="background-color: #007bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">
                        Login Now
                    </a>
                </p>
                <p>We recommend enabling Two-Factor Authentication (2FA) in your profile settings for enhanced security.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetInviteCreatedTemplate(Dictionary<string, string> parameters)
    {
        var inviteLink = parameters.GetValueOrDefault("inviteLink", "#");
        var invitedBy = parameters.GetValueOrDefault("invitedBy", "an administrator");
        var expiryDays = parameters.GetValueOrDefault("expiryDays", "7");

        var subject = "You've Been Invited to TelegramGroupsAdmin";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2>You've Been Invited!</h2>
                <p>You've been invited by {invitedBy} to join TelegramGroupsAdmin.</p>
                <p style="margin: 30px 0;">
                    <a href="{inviteLink}" style="background-color: #28a745; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px; display: inline-block;">
                        Accept Invitation
                    </a>
                </p>
                <p>This invitation will expire in {expiryDays} days.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetAccountDisabledTemplate(Dictionary<string, string> parameters)
    {
        var email = parameters.GetValueOrDefault("email", "");
        var reason = parameters.GetValueOrDefault("reason", "administrative action");

        var subject = "Account Disabled";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2>Account Disabled</h2>
                <p>Your account ({email}) has been disabled due to: {reason}</p>
                <p>If you believe this is an error, please contact your administrator.</p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin Security Team</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetAccountLockedTemplate(Dictionary<string, string> parameters)
    {
        var email = parameters.GetValueOrDefault("email", "");
        var lockedUntil = parameters.GetValueOrDefault("lockedUntil", "");
        var attempts = parameters.GetValueOrDefault("attempts", "");

        var subject = "Account Locked - Security Alert";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2 style="color: #dc3545;">Account Locked - Security Alert</h2>
                <p>Your account ({email}) has been temporarily locked due to {attempts} failed login attempts.</p>
                <p><strong>Locked Until:</strong> {lockedUntil}</p>
                <p>This is an automated security measure to protect your account from unauthorized access.</p>
                <h3>What you can do:</h3>
                <ul>
                    <li>Wait until the lockout period expires and try logging in again</li>
                    <li>Contact an administrator if you need immediate access</li>
                    <li>If you didn't attempt to log in, change your password immediately after the lockout expires</li>
                </ul>
                <p style="color: #dc3545;"><strong>If these login attempts were not from you, your account may be compromised. Please contact your administrator immediately.</strong></p>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin Security Team</p>
            </body>
            </html>
            """;

        return (subject, body);
    }

    private (string Subject, string Body) GetAccountUnlockedTemplate(Dictionary<string, string> parameters)
    {
        var email = parameters.GetValueOrDefault("email", "");

        var subject = "Account Unlocked";
        var body = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <h2 style="color: #28a745;">Account Unlocked</h2>
                <p>Your account ({email}) has been unlocked by an administrator.</p>
                <p>You can now log in normally.</p>
                <p>For your security, we recommend:</p>
                <ul>
                    <li>Changing your password if you suspect it may be compromised</li>
                    <li>Enabling Two-Factor Authentication (2FA) for enhanced security</li>
                    <li>Reviewing recent account activity</li>
                </ul>
                <hr style="border: none; border-top: 1px solid #ddd; margin: 30px 0;">
                <p style="color: #666; font-size: 12px;">TelegramGroupsAdmin Security Team</p>
            </body>
            </html>
            """;

        return (subject, body);
    }
}

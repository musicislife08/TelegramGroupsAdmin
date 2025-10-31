using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Services.Email;

/// <summary>
/// Email service implementation using SendGrid API
/// </summary>
public class SendGridEmailService : IEmailService
{
    private readonly SendGridOptions _options;
    private readonly ILogger<SendGridEmailService> _logger;
    private readonly SendGridClient _client;

    public SendGridEmailService(
        IOptions<SendGridOptions> options,
        ILogger<SendGridEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new SendGridClient(_options.ApiKey);

        // Debug log configuration at startup
        _logger.LogInformation("SendGrid configured: Enabled={Enabled}, FromAddress={FromAddress}, FromName={FromName}, ApiKeySet={ApiKeySet}",
            _options.Enabled,
            _options.FromAddress,
            _options.FromName,
            !string.IsNullOrEmpty(_options.ApiKey));
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken ct = default)
    {
        await SendEmailAsync([to], subject, body, isHtml, ct);
    }

    public async Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = true, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("SendGrid service is disabled. Email not sent to: {Recipients}", string.Join(", ", to));
            return;
        }

        try
        {
            var recipients = to.Select(email => new EmailAddress(email)).ToList();

            _logger.LogInformation("Attempting to send email via SendGrid to: {Recipients}, Subject: {Subject}",
                string.Join(", ", to), subject);

            var from = new EmailAddress(_options.FromAddress, _options.FromName);
            var msg = MailHelper.CreateSingleEmailToMultipleRecipients(
                from,
                recipients,
                subject,
                isHtml ? null : body,  // Plain text
                isHtml ? body : null   // HTML
            );

            _logger.LogDebug("Sending email via SendGrid API...");
            var response = await _client.SendEmailAsync(msg, ct);

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

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("SendGrid service is disabled. Connection test skipped.");
            return Task.FromResult(false);
        }

        try
        {
            // SendGrid doesn't have a "test connection" endpoint, but we can verify the API key format
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || !_options.ApiKey.StartsWith("SG."))
            {
                _logger.LogError("Invalid SendGrid API key format");
                return Task.FromResult(false);
            }

            _logger.LogInformation("SendGrid API key format is valid");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendGrid connection test failed");
            return Task.FromResult(false);
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
}

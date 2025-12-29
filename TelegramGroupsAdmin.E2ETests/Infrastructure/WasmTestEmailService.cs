using System.Collections.Concurrent;
using TelegramGroupsAdmin.Ui.Server.Services.Email;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Stub email service for WASM UI E2E tests.
/// Captures sent emails without actually sending them.
/// </summary>
public class WasmTestEmailService : IEmailService
{
    /// <summary>
    /// Thread-safe collection of all emails "sent" during tests.
    /// </summary>
    public ConcurrentBag<WasmSentEmail> SentEmails { get; } = new();

    public Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        SentEmails.Add(new WasmSentEmail([to], subject, body, isHtml));
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        SentEmails.Add(new WasmSentEmail(to.ToArray(), subject, body, isHtml));
        return Task.CompletedTask;
    }

    public Task SendTemplatedEmailAsync(string to, EmailTemplateData templateData, CancellationToken cancellationToken = default)
    {
        // Store template type name as subject for easy test verification
        var subject = $"[Template:{templateData.GetType().Name}]";
        var body = templateData.ToString() ?? "";
        SentEmails.Add(new WasmSentEmail([to], subject, body, true, templateData));
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>
    /// Clears all captured emails. Call at start of each test for isolation.
    /// </summary>
    public void Clear() => SentEmails.Clear();

    /// <summary>
    /// Gets emails sent to a specific address.
    /// </summary>
    public IEnumerable<WasmSentEmail> GetEmailsTo(string email) =>
        SentEmails.Where(e => e.To.Contains(email, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Gets emails with a specific template type.
    /// </summary>
    public IEnumerable<WasmSentEmail> GetEmailsByTemplate<T>() where T : EmailTemplateData =>
        SentEmails.Where(e => e.TemplateData is T);
}

/// <summary>
/// Represents an email that was "sent" during WASM UI testing.
/// </summary>
public record WasmSentEmail(
    string[] To,
    string Subject,
    string Body,
    bool IsHtml,
    EmailTemplateData? TemplateData = null);

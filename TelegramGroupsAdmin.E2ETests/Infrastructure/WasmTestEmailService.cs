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

    public Task SendTemplatedEmailAsync(string to, EmailTemplate template, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        // Store template info as subject for easy test verification
        var subject = $"[Template:{template}]";
        var body = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
        SentEmails.Add(new WasmSentEmail([to], subject, body, true, template, parameters));
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
    public IEnumerable<WasmSentEmail> GetEmailsByTemplate(EmailTemplate template) =>
        SentEmails.Where(e => e.Template == template);
}

/// <summary>
/// Represents an email that was "sent" during WASM UI testing.
/// </summary>
public record WasmSentEmail(
    string[] To,
    string Subject,
    string Body,
    bool IsHtml,
    EmailTemplate? Template = null,
    Dictionary<string, string>? Parameters = null);

using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

public class UsernameBlacklistService(
    IUsernameBlacklistRepository repository,
    IAuditService auditService) : IUsernameBlacklistService
{
    public async Task<UsernameBlacklistEntry?> CheckDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        // Don't match fallback display names like "User 12345" or "Unknown User"
        if (displayName.StartsWith("User ", StringComparison.Ordinal) ||
            displayName == "Unknown User")
            return null;

        var entries = await repository.GetEnabledEntriesAsync(cancellationToken);

        foreach (var entry in entries)
        {
            var isMatch = entry.MatchType switch
            {
                BlacklistMatchType.Exact =>
                    string.Equals(displayName, entry.Pattern, StringComparison.OrdinalIgnoreCase),
                // Future match types go here
                _ => false
            };

            if (isMatch)
                return entry;
        }

        return null;
    }

    public async Task<long> AddEntryAsync(
        string pattern,
        BlacklistMatchType matchType,
        string? notes,
        Actor actor,
        CancellationToken ct = default)
    {
        var entry = new UsernameBlacklistEntry(
            Id: 0,
            Pattern: pattern,
            MatchType: matchType,
            Enabled: true,
            CreatedAt: DateTimeOffset.UtcNow,
            Notes: notes);

        var id = await repository.AddEntryAsync(entry, ct);

        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            actor,
            pattern,
            ct);

        return id;
    }

    public async Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteEntryAsync(id, ct);
        if (deleted)
        {
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryRemoved,
                actor, actor, pattern, ct);
        }
        return deleted;
    }

    public async Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default)
    {
        var updated = await repository.SetEnabledAsync(id, enabled, ct);
        if (updated)
        {
            var eventType = enabled
                ? AuditEventType.BlacklistEntryEnabled
                : AuditEventType.BlacklistEntryDisabled;
            await auditService.LogEventAsync(eventType, actor, actor, pattern, ct);
        }
        return updated;
    }

    public async Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default)
    {
        var updated = await repository.UpdateNotesAsync(id, notes, ct);
        if (updated)
        {
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryNotesChanged,
                actor, actor, pattern, ct);
        }
        return updated;
    }
}

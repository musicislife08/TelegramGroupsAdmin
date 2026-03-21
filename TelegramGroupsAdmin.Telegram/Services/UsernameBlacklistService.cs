using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

public class UsernameBlacklistService(
    IUsernameBlacklistRepository repository) : IUsernameBlacklistService
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
}

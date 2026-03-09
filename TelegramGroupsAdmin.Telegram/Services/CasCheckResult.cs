namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of a CAS (Combot Anti-Spam) database check.
/// </summary>
/// <param name="IsBanned">Whether the user is banned in the CAS database.</param>
/// <param name="Reason">
/// The reason for the ban if <paramref name="IsBanned"/> is <c>true</c>;
/// otherwise <c>null</c>.
/// </param>
public record CasCheckResult(bool IsBanned, string? Reason);

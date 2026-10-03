using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// The one place a stored user row becomes a UserIdentity, and the one place the verdict rule lives.
/// </summary>
public static class UserIdentityMapping
{
    public static UserIdentity ToIdentity(
        long id, string? firstName, string? lastName, string? username, bool isBot, bool? latestScanExplicit) =>
        new(id, firstName, lastName, username)
        {
            Verdict = isBot || TelegramConstants.IsSystemUser(id) || latestScanExplicit is null
                ? NameVerdict.Unscanned
                : latestScanExplicit.Value ? NameVerdict.Explicit : NameVerdict.Clean
        };

    public static UserIdentity ToIdentity(this UserIdentityView row) =>
        ToIdentity(row.TelegramUserId, row.FirstName, row.LastName, row.Username, row.IsBot, row.LatestScanExplicit);
}

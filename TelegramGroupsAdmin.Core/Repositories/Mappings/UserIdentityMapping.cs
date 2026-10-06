using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// The one place a stored user row becomes a UserIdentity, and the one place the verdict rule lives:
/// a flagged name is masked only while the user is banned (the verdict decides how, the ban whether).
/// </summary>
public static class UserIdentityMapping
{
    public static UserIdentity ToIdentity(
        long id, string? firstName, string? lastName, string? username, bool isBot,
        bool? latestScanExplicit, bool? latestScanPromotional, bool isBanned) =>
        new(id, firstName, lastName, username)
        {
            Verdict = isBot || TelegramConstants.IsSystemUser(id) || latestScanExplicit is null
                ? NameVerdict.Unscanned
                : !isBanned
                    ? NameVerdict.Clean
                    : latestScanExplicit.Value
                        ? NameVerdict.Explicit
                        : latestScanPromotional == true ? NameVerdict.Promotional : NameVerdict.Clean
        };

    /// <summary>
    /// Identity for a row read through a left join: the row's identity, or an id-only identity when
    /// the user has no row.
    /// </summary>
    public static UserIdentity ToIdentityOrIdOnly(this UserIdentityView? row, long id) =>
        row?.ToIdentity() ?? UserIdentity.FromId(id);

    public static UserIdentity ToIdentity(this UserIdentityView row) =>
        ToIdentity(row.TelegramUserId, row.FirstName, row.LastName, row.Username, row.IsBot,
            row.LatestScanExplicit, row.LatestScanPromotional, row.IsBanned);
}

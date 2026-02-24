using MudBlazor;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Helpers;

internal static class ProfileScanDisplayHelpers
{
    public static Color GetScoreColor(decimal score) => score switch
    {
        >= 4.0m => Color.Error,
        >= 2.0m => Color.Warning,
        _ => Color.Success
    };

    public static Color GetOutcomeColor(ProfileScanOutcome outcome) => outcome switch
    {
        ProfileScanOutcome.Clean => Color.Success,
        ProfileScanOutcome.HeldForReview => Color.Warning,
        ProfileScanOutcome.Banned => Color.Error,
        _ => Color.Default
    };
}

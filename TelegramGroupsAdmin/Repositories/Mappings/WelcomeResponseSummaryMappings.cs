using TelegramGroupsAdmin.Models.Analytics;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories.Mappings;

/// <summary>
/// Mapping extensions for WelcomeResponseSummaryView
/// Maps from view to a simpler model for C# consumption
/// </summary>
public static class WelcomeResponseSummaryMappings
{
    extension(DataModels.WelcomeResponseSummaryView view)
    {
        public WelcomeResponseSummary ToModel()
        {
            return new WelcomeResponseSummary
            {
                ChatId = view.ChatId,
                ChatName = view.ChatName,
                JoinDate = view.JoinDate,
                TotalJoins = (int)view.TotalJoins,
                AcceptedCount = (int)view.AcceptedCount,
                DeniedCount = (int)view.DeniedCount,
                TimeoutCount = (int)view.TimeoutCount,
                LeftCount = (int)view.LeftCount,
                AvgAcceptSeconds = view.AvgAcceptSeconds
            };
        }
    }
}

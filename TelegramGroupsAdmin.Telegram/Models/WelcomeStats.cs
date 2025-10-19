namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Analytics data for welcome system
/// </summary>
public record WelcomeStats(
    int TotalResponses,
    int AcceptedCount,
    int DeniedCount,
    int TimeoutCount,
    int LeftCount,
    double AcceptanceRate,
    int DmSuccessCount,
    int DmFallbackCount,
    double DmSuccessRate
);

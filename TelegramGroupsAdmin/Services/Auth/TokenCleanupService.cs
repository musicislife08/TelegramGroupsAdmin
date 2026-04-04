namespace TelegramGroupsAdmin.Services.Auth;

public class TokenCleanupService(
    IIntermediateAuthService authService,
    IPendingRecoveryCodesService codesService,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                authService.CleanupExpiredEntries();
                codesService.CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Token cleanup failed");
            }
        }
    }
}

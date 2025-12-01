using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// One-time migration service to move VAPID keys from ApiKeysConfig to dedicated WebPushConfig storage
/// Runs on startup, checks if migration is needed, and migrates keys if found in old location
///
/// Migration strategy:
/// 1. Check if keys already exist in new location (WebPushConfig + vapid_private_key_encrypted)
/// 2. If not, check old location (ApiKeysConfig.VapidPublicKey/VapidPrivateKey)
/// 3. If found, copy to new location with proper encryption purpose
/// 4. Clear keys from old location to prevent duplication
/// </summary>
public class VapidKeyMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VapidKeyMigrationService> _logger;

    public VapidKeyMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<VapidKeyMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await MigrateVapidKeysAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate VAPID keys from ApiKeysConfig to WebPushConfig");
            // Don't throw - allow application to start even if migration fails
            // VAPID keys will be regenerated if not found
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateVapidKeysAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

        // Step 1: Check if VAPID keys already exist in new location
        if (await configRepo.HasVapidKeysAsync(cancellationToken))
        {
            _logger.LogDebug("VAPID keys already exist in new location, skipping migration");
            return;
        }

        // Step 2: Check old location (ApiKeysConfig)
        var apiKeys = await configRepo.GetApiKeysAsync(cancellationToken);
        if (apiKeys == null || !apiKeys.HasVapidKeys())
        {
            _logger.LogDebug("No VAPID keys found in ApiKeysConfig, nothing to migrate");
            return;
        }

        _logger.LogInformation("Migrating VAPID keys from ApiKeysConfig to dedicated WebPushConfig storage...");

        // Step 3: Get or create WebPushConfig and populate with public key
        var webPushConfig = await configRepo.GetWebPushConfigAsync(cancellationToken) ?? new WebPushConfig();
        webPushConfig.VapidPublicKey = apiKeys.VapidPublicKey;
        webPushConfig.Enabled = true; // Preserve existing behavior

        // Save WebPushConfig (includes public key, contact email, enabled flag)
        await configRepo.SaveWebPushConfigAsync(webPushConfig, cancellationToken);
        _logger.LogInformation("Migrated VAPID public key to WebPushConfig.VapidPublicKey");

        // Step 4: Save private key with proper encryption purpose
        if (!string.IsNullOrWhiteSpace(apiKeys.VapidPrivateKey))
        {
            await configRepo.SaveVapidPrivateKeyAsync(apiKeys.VapidPrivateKey, cancellationToken);
            _logger.LogInformation("Migrated VAPID private key to dedicated encrypted column");
        }

        // Step 5: Verify migration succeeded before clearing old keys
        if (await configRepo.HasVapidKeysAsync(cancellationToken))
        {
            // Only clear old location if new location is fully configured
            apiKeys.VapidPublicKey = null;
            apiKeys.VapidPrivateKey = null;
            await configRepo.SaveApiKeysAsync(apiKeys, cancellationToken);

            _logger.LogInformation(
                "VAPID key migration complete. Keys moved from ApiKeysConfig to dedicated WebPushConfig storage. " +
                "Public key is now stored in plain JSONB (not a secret), private key is encrypted with dedicated purpose.");
        }
        else
        {
            _logger.LogWarning(
                "VAPID key migration verification failed - keys saved but verification returned false. " +
                "Old keys not cleared to prevent data loss. Will retry on next startup.");
        }
    }
}

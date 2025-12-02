using System.Security.Cryptography;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Startup service that ensures VAPID keys exist for Web Push notifications.
/// Auto-generates keys on first startup if not present in database.
/// VAPID keys should never be regenerated once created (breaks all existing subscriptions).
///
/// Key storage:
/// - Public key: WebPushConfig.VapidPublicKey (JSONB, not a secret)
/// - Private key: configs.vapid_private_key_encrypted (encrypted TEXT column)
/// </summary>
public class VapidKeyGenerationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VapidKeyGenerationService> _logger;

    public VapidKeyGenerationService(
        IServiceScopeFactory scopeFactory,
        ILogger<VapidKeyGenerationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("VapidKeyGenerationService starting...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();

            // Check if VAPID keys exist in new location
            if (await configRepo.HasVapidKeysAsync(cancellationToken))
            {
                _logger.LogInformation("VAPID keys already configured for Web Push");
                return;
            }

            _logger.LogInformation("No VAPID keys found, generating new key pair...");

            // Generate new VAPID key pair
            var (publicKey, privateKey) = GenerateVapidKeys();

            // Get or create WebPushConfig and set public key
            var webPushConfig = await configRepo.GetWebPushConfigAsync(cancellationToken) ?? new WebPushConfig();
            webPushConfig.VapidPublicKey = publicKey;
            webPushConfig.Enabled = true;

            // Save public key to WebPushConfig (JSONB, not a secret)
            await configRepo.SaveWebPushConfigAsync(webPushConfig, cancellationToken);

            // Save private key to dedicated encrypted column
            await configRepo.SaveVapidPrivateKeyAsync(privateKey, cancellationToken);

            _logger.LogInformation("Generated and saved new VAPID keys for Web Push notifications");
        }
        catch (Exception ex)
        {
            // Don't fail startup - Web Push will just be disabled
            _logger.LogWarning(ex, "Failed to initialize VAPID keys - Web Push notifications will be disabled");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Generate a new VAPID key pair using P-256 elliptic curve
    /// Returns (publicKey, privateKey) as base64 URL-safe encoded strings
    /// </summary>
    private static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);

        // Public key: 0x04 || X || Y (uncompressed point format)
        var publicKeyBytes = new byte[65];
        publicKeyBytes[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicKeyBytes, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicKeyBytes, 33, 32);

        // Private key: just the D parameter
        var privateKeyBytes = parameters.D!;

        // Base64 URL-safe encoding (no padding)
        var publicKey = Base64UrlEncode(publicKeyBytes);
        var privateKey = Base64UrlEncode(privateKeyBytes);

        return (publicKey, privateKey);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

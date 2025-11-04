using System.Text.Json;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class NotificationPreferencesRepository : INotificationPreferencesRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtector _secretsProtector;
    private readonly ILogger<NotificationPreferencesRepository> _logger;

    public NotificationPreferencesRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<NotificationPreferencesRepository> logger)
    {
        _contextFactory = contextFactory;
        _secretsProtector = dataProtectionProvider.CreateProtector(DataProtectionPurposes.NotificationSecrets);
        _logger = logger;
    }

    public async Task<NotificationPreferences> GetOrCreatePreferencesAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        if (entity != null)
        {
            return entity.ToModel();
        }

        // Create default preferences
        var now = DateTimeOffset.UtcNow;
        var defaultEntity = new DataModels.NotificationPreferencesDto
        {
            UserId = userId,
            TelegramDmEnabled = true,
            EmailEnabled = false,
            ChannelConfigs = """
                {
                    "email": {"address": null, "digest_minutes": 0},
                    "telegram": {}
                }
                """,
            EventFilters = """
                {
                    "spam_detected": true,
                    "spam_auto_deleted": true,
                    "user_banned": true,
                    "message_reported": true,
                    "chat_health_warning": true,
                    "backup_failed": true,
                    "malware_detected": true
                }
                """,
            ProtectedSecrets = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        context.NotificationPreferences.Add(defaultEntity);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created default notification preferences for user {UserId}", userId);

        return defaultEntity.ToModel();
    }

    public async Task UpdatePreferencesAsync(NotificationPreferences preferences, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == preferences.UserId, ct);

        if (entity == null)
        {
            throw new InvalidOperationException($"Notification preferences not found for user {preferences.UserId}");
        }

        // Update fields (preserve ProtectedSecrets - managed separately)
        entity.TelegramDmEnabled = preferences.TelegramDmEnabled;
        entity.EmailEnabled = preferences.EmailEnabled;
        entity.ChannelConfigs = JsonSerializer.Serialize(preferences.ChannelConfigs);
        entity.EventFilters = JsonSerializer.Serialize(preferences.EventFilters);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated notification preferences for user {UserId}", preferences.UserId);
    }

    public async Task SetProtectedSecretAsync(string userId, string secretKey, string secretValue, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        if (entity == null)
        {
            throw new InvalidOperationException($"Notification preferences not found for user {userId}");
        }

        // Deserialize existing secrets
        var secrets = string.IsNullOrWhiteSpace(entity.ProtectedSecrets)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ProtectedSecrets)
              ?? new Dictionary<string, string>();

        // Encrypt and store the secret
        var encryptedValue = _secretsProtector.Protect(secretValue);
        secrets[secretKey] = encryptedValue;

        // Serialize back to JSONB
        entity.ProtectedSecrets = JsonSerializer.Serialize(secrets);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Stored encrypted secret '{SecretKey}' for user {UserId}", secretKey, userId);
    }

    public async Task<string?> GetProtectedSecretAsync(string userId, string secretKey, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        if (entity == null)
        {
            return null;
        }

        // Deserialize secrets
        var secrets = string.IsNullOrWhiteSpace(entity.ProtectedSecrets)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ProtectedSecrets)
              ?? new Dictionary<string, string>();

        if (!secrets.TryGetValue(secretKey, out var encryptedValue))
        {
            return null;
        }

        try
        {
            // Decrypt and return
            return _secretsProtector.Unprotect(encryptedValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret '{SecretKey}' for user {UserId}", secretKey, userId);
            return null;
        }
    }
}

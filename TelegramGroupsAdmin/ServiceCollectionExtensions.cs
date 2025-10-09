using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Web UI data services (Identity repositories + TOTP protection + Data Protection API)
    /// </summary>
    public static IServiceCollection AddTgSpamWebDataServices(
        this IServiceCollection services,
        string dataProtectionKeysPath)
    {
        ConfigureDataProtection(services, dataProtectionKeysPath);

        // Identity-related repositories and services
        services.AddSingleton<ITotpProtectionService, TotpProtectionService>();
        services.AddSingleton<UserRepository>();
        services.AddSingleton<InviteRepository>();
        services.AddSingleton<AuditLogRepository>();
        services.AddSingleton<VerificationTokenRepository>();

        // Message history repository (read-only for Web UI)
        services.AddSingleton<MessageHistoryRepository>();

        return services;
    }

    /// <summary>
    /// Adds API data services (Message history repository only - no identity)
    /// </summary>
    public static IServiceCollection AddTgSpamApiDataServices(
        this IServiceCollection services)
    {
        // Message history repository (read-write for API)
        services.AddSingleton<MessageHistoryRepository>();

        return services;
    }

    private static void ConfigureDataProtection(IServiceCollection services, string dataProtectionKeysPath)
    {
        // Create keys directory
        Directory.CreateDirectory(dataProtectionKeysPath);

        // Set restrictive permissions on keys directory (Linux/macOS only)
        if (!OperatingSystem.IsWindows())
        {
            // Get logger from service provider (early in startup, before app is built)
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetService<ILogger<IServiceCollection>>();

            try
            {
                // chmod 700 - only owner can read/write/execute
                File.SetUnixFileMode(dataProtectionKeysPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                logger?.LogInformation("Set permissions on {KeysPath} to 700", dataProtectionKeysPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to set Unix permissions on {KeysPath}", dataProtectionKeysPath);
            }
        }

        // Configure Data Protection API
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
            .SetApplicationName("TgSpamPreFilter");
    }
}

using System.Text.Json;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services;

public interface IUserDataExportService
{
    Task ExportAsync(string exportPath);
    Task ImportAsync(string importPath);
}

public class UserDataExportService : IUserDataExportService
{
    private readonly UserRepository _userRepository;
    private readonly ILogger<UserDataExportService> _logger;

    public UserDataExportService(UserRepository userRepository, ILogger<UserDataExportService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task ExportAsync(string exportPath)
    {
        _logger.LogInformation("Exporting decrypted user data to {ExportPath}", exportPath);

        var users = await _userRepository.GetAllAsync();

        // Export only essential fields - PasswordHash and TotpSecret will be decrypted
        var export = users.Select(u => new UserExportDto(
            u.Id,
            u.Email,
            u.PasswordHash,      // Auto-decrypted by repository
            u.TotpSecret,        // Auto-decrypted by repository
            u.TotpEnabled,
            u.TotpSetupStartedAt
        )).ToList();

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(exportPath, json);

        _logger.LogInformation("Exported {Count} users (decrypted passwords and TOTP secrets) to {ExportPath}", users.Count, exportPath);
    }

    public async Task ImportAsync(string importPath)
    {
        _logger.LogInformation("Importing user data from {ImportPath}", importPath);

        if (!File.Exists(importPath))
        {
            _logger.LogError("Import file not found: {ImportPath}", importPath);
            return;
        }

        var json = await File.ReadAllTextAsync(importPath);
        var importData = JsonSerializer.Deserialize<List<UserExportDto>>(json);

        if (importData == null || importData.Count == 0)
        {
            _logger.LogWarning("No user data found in import file");
            return;
        }

        _logger.LogInformation("Found {Count} users in import file", importData.Count);

        int updated = 0;
        int skipped = 0;

        foreach (var importUser in importData)
        {
            try
            {
                // Get existing user
                var existingUser = await _userRepository.GetByIdAsync(importUser.Id);

                if (existingUser == null)
                {
                    _logger.LogWarning("User {UserId} ({Email}) not found - skipping", importUser.Id, importUser.Email);
                    skipped++;
                    continue;
                }

                // Update only encrypted fields - repository will re-encrypt with local machine's keys
                var updatedUser = existingUser with
                {
                    PasswordHash = importUser.PasswordHash,  // Will be re-encrypted
                    TotpSecret = importUser.TotpSecret,      // Will be re-encrypted
                    TotpEnabled = importUser.TotpEnabled,
                    TotpSetupStartedAt = importUser.TotpSetupStartedAt
                };

                await _userRepository.UpdateAsync(updatedUser);
                _logger.LogInformation("Updated user {UserId} ({Email}) with re-encrypted credentials", importUser.Id, importUser.Email);
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import user {UserId} ({Email})", importUser.Id, importUser.Email);
                skipped++;
            }
        }

        _logger.LogInformation("Import complete: {Updated} users updated, {Skipped} skipped", updated, skipped);
    }
}

public record UserExportDto(
    string Id,
    string Email,
    string PasswordHash,
    string? TotpSecret,
    bool TotpEnabled,
    long? TotpSetupStartedAt
);

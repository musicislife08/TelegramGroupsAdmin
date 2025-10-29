using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Reads backup configuration settings from the database
/// </summary>
public class BackupConfigurationService : IBackupConfigurationService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BackupConfigurationService> _logger;

    public BackupConfigurationService(
        NpgsqlDataSource dataSource,
        ILogger<BackupConfigurationService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves backup encryption configuration from database
    /// </summary>
    public async Task<BackupEncryptionConfig?> GetEncryptionConfigAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        const string sql = """
            SELECT backup_encryption_config
            FROM configs
            WHERE chat_id IS NULL
            LIMIT 1
            """;

        var configJson = await connection.QuerySingleOrDefaultAsync<string>(sql);

        if (string.IsNullOrEmpty(configJson))
            return null;

        var config = JsonSerializer.Deserialize<BackupEncryptionConfig>(configJson);
        return config;
    }
}

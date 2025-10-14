using Dapper;
using Npgsql;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing Telegram admin status per chat
/// Caches admin permissions to avoid API calls on every command
/// </summary>
public class ChatAdminsRepository : IChatAdminsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ChatAdminsRepository> _logger;

    public ChatAdminsRepository(IConfiguration configuration, ILogger<ChatAdminsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> GetPermissionLevelAsync(long chatId, long telegramId)
    {
        const string sql = """
            SELECT is_creator
            FROM chat_admins
            WHERE chat_id = @ChatId AND telegram_id = @TelegramId AND is_active = true
            LIMIT 1
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var isCreator = await connection.QueryFirstOrDefaultAsync<bool?>(sql, new { ChatId = chatId, TelegramId = telegramId });

        if (isCreator == null)
        {
            return -1; // Not an admin
        }

        return isCreator.Value ? 2 : 1; // Creator = Owner (2), Admin = Admin (1)
    }

    /// <inheritdoc/>
    public async Task<bool> IsAdminAsync(long chatId, long telegramId)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM chat_admins
                WHERE chat_id = @ChatId AND telegram_id = @TelegramId AND is_active = true
            )
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<bool>(sql, new { ChatId = chatId, TelegramId = telegramId });
    }

    /// <inheritdoc/>
    public async Task<List<ChatAdmin>> GetChatAdminsAsync(long chatId)
    {
        const string sql = """
            SELECT id, chat_id, telegram_id, username, is_creator, promoted_at, last_verified_at, is_active
            FROM chat_admins
            WHERE chat_id = @ChatId AND is_active = true
            ORDER BY is_creator DESC, promoted_at ASC
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var dataRecords = await connection.QueryAsync<DataModels.ChatAdminRecordDto>(sql, new { ChatId = chatId });
        return dataRecords.Select(r => r.ToUiModel()).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<long>> GetAdminChatsAsync(long telegramId)
    {
        const string sql = """
            SELECT chat_id
            FROM chat_admins
            WHERE telegram_id = @TelegramId AND is_active = true
            ORDER BY chat_id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var chatIds = await connection.QueryAsync<long>(sql, new { TelegramId = telegramId });
        return chatIds.ToList();
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(long chatId, long telegramId, bool isCreator, string? username = null)
    {
        const string sql = """
            INSERT INTO chat_admins (chat_id, telegram_id, username, is_creator, promoted_at, last_verified_at, is_active)
            VALUES (@ChatId, @TelegramId, @Username, @IsCreator, @Now, @Now, true)
            ON CONFLICT (chat_id, telegram_id)
            DO UPDATE SET
                username = EXCLUDED.username,
                is_creator = EXCLUDED.is_creator,
                last_verified_at = EXCLUDED.last_verified_at,
                is_active = true
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            ChatId = chatId,
            TelegramId = telegramId,
            Username = username,
            IsCreator = isCreator,
            Now = now
        });

        _logger.LogDebug("Upserted admin: chat={ChatId}, user={TelegramId} (@{Username}), creator={IsCreator}",
            chatId, telegramId, username ?? "unknown", isCreator);
    }

    /// <inheritdoc/>
    public async Task DeactivateAsync(long chatId, long telegramId)
    {
        const string sql = """
            UPDATE chat_admins
            SET is_active = false, last_verified_at = @Now
            WHERE chat_id = @ChatId AND telegram_id = @TelegramId
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ChatId = chatId,
            TelegramId = telegramId,
            Now = now
        });

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deactivated admin: chat={ChatId}, user={TelegramId}", chatId, telegramId);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByChatIdAsync(long chatId)
    {
        const string sql = "DELETE FROM chat_admins WHERE chat_id = @ChatId";

        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { ChatId = chatId });

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deleted {Count} admin records for chat {ChatId}", rowsAffected, chatId);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateLastVerifiedAsync(long chatId, long telegramId)
    {
        const string sql = """
            UPDATE chat_admins
            SET last_verified_at = @Now
            WHERE chat_id = @ChatId AND telegram_id = @TelegramId
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            ChatId = chatId,
            TelegramId = telegramId,
            Now = now
        });
    }
}

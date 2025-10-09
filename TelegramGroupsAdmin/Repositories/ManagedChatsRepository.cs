using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class ManagedChatsRepository : IManagedChatsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ManagedChatsRepository> _logger;

    public ManagedChatsRepository(
        IConfiguration configuration,
        ILogger<ManagedChatsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task UpsertAsync(ManagedChatRecord chat)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO managed_chats (
                chat_id, chat_name, chat_type, bot_status,
                is_admin, added_at, is_active, last_seen_at, settings_json
            ) VALUES (
                @ChatId, @ChatName, @ChatType, @BotStatus,
                @IsAdmin, @AddedAt, @IsActive, @LastSeenAt, @SettingsJson
            )
            ON CONFLICT (chat_id) DO UPDATE SET
                chat_name = EXCLUDED.chat_name,
                chat_type = EXCLUDED.chat_type,
                bot_status = EXCLUDED.bot_status,
                is_admin = EXCLUDED.is_admin,
                is_active = EXCLUDED.is_active,
                last_seen_at = EXCLUDED.last_seen_at,
                settings_json = COALESCE(EXCLUDED.settings_json, managed_chats.settings_json);
            """;

        await connection.ExecuteAsync(sql, new
        {
            chat.ChatId,
            chat.ChatName,
            ChatType = (int)chat.ChatType,
            BotStatus = (int)chat.BotStatus,
            chat.IsAdmin,
            chat.AddedAt,
            chat.IsActive,
            chat.LastSeenAt,
            chat.SettingsJson
        });

        _logger.LogDebug(
            "Upserted managed chat {ChatId} ({ChatName}): {BotStatus}, admin={IsAdmin}, active={IsActive}",
            chat.ChatId,
            chat.ChatName,
            chat.BotStatus,
            chat.IsAdmin,
            chat.IsActive);
    }

    public async Task<ManagedChatRecord?> GetByChatIdAsync(long chatId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT chat_id, chat_name, chat_type, bot_status,
                   is_admin, added_at, is_active, last_seen_at, settings_json
            FROM managed_chats
            WHERE chat_id = @ChatId;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<DataModels.ManagedChatRecordDto>(
            sql,
            new { ChatId = chatId });

        return dto?.ToManagedChatRecord().ToUiModel();
    }

    public async Task<List<ManagedChatRecord>> GetActiveChatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT chat_id, chat_name, chat_type, bot_status,
                   is_admin, added_at, is_active, last_seen_at, settings_json
            FROM managed_chats
            WHERE is_active = true
            ORDER BY chat_name ASC;
            """;

        var dtos = await connection.QueryAsync<DataModels.ManagedChatRecordDto>(sql);
        return dtos.Select(dto => dto.ToManagedChatRecord().ToUiModel()).ToList();
    }

    public async Task<List<ManagedChatRecord>> GetAdminChatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT chat_id, chat_name, chat_type, bot_status,
                   is_admin, added_at, is_active, last_seen_at, settings_json
            FROM managed_chats
            WHERE is_active = true
              AND is_admin = true
            ORDER BY chat_name ASC;
            """;

        var dtos = await connection.QueryAsync<DataModels.ManagedChatRecordDto>(sql);
        return dtos.Select(dto => dto.ToManagedChatRecord().ToUiModel()).ToList();
    }

    public async Task<bool> IsActiveAndAdminAsync(long chatId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM managed_chats
                WHERE chat_id = @ChatId
                  AND is_active = true
                  AND is_admin = true
            );
            """;

        return await connection.ExecuteScalarAsync<bool>(sql, new { ChatId = chatId });
    }

    public async Task MarkInactiveAsync(long chatId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE managed_chats
            SET is_active = false
            WHERE chat_id = @ChatId;
            """;

        await connection.ExecuteAsync(sql, new { ChatId = chatId });

        _logger.LogInformation("Marked chat {ChatId} as inactive", chatId);
    }

    public async Task UpdateLastSeenAsync(long chatId, long timestamp)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE managed_chats
            SET last_seen_at = @Timestamp
            WHERE chat_id = @ChatId;
            """;

        await connection.ExecuteAsync(sql, new { ChatId = chatId, Timestamp = timestamp });
    }

    public async Task<List<ManagedChatRecord>> GetAllChatsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT chat_id, chat_name, chat_type, bot_status,
                   is_admin, added_at, is_active, last_seen_at, settings_json
            FROM managed_chats
            ORDER BY is_active DESC, chat_name ASC;
            """;

        var dtos = await connection.QueryAsync<DataModels.ManagedChatRecordDto>(sql);
        return dtos.Select(dto => dto.ToManagedChatRecord().ToUiModel()).ToList();
    }
}

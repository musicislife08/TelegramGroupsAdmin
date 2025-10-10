using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class ReportsRepository : IReportsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ReportsRepository> _logger;

    public ReportsRepository(
        IConfiguration configuration,
        ILogger<ReportsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task<long> InsertAsync(Report report)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO reports (
                message_id, chat_id, report_command_message_id,
                reported_by_user_id, reported_by_user_name, reported_at,
                status, reviewed_by, reviewed_at, action_taken, admin_notes
            ) VALUES (
                @MessageId, @ChatId, @ReportCommandMessageId,
                @ReportedByUserId, @ReportedByUserName, @ReportedAt,
                @Status, @ReviewedBy, @ReviewedAt, @ActionTaken, @AdminNotes
            )
            RETURNING id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, new
        {
            report.MessageId,
            report.ChatId,
            report.ReportCommandMessageId,
            report.ReportedByUserId,
            report.ReportedByUserName,
            report.ReportedAt,
            Status = (int)report.Status,
            report.ReviewedBy,
            report.ReviewedAt,
            report.ActionTaken,
            report.AdminNotes
        });

        _logger.LogInformation(
            "Inserted report {ReportId} for message {MessageId} in chat {ChatId} by user {UserId}",
            id,
            report.MessageId,
            report.ChatId,
            report.ReportedByUserId);

        return id;
    }

    public async Task<Report?> GetByIdAsync(long id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, message_id, chat_id, report_command_message_id,
                   reported_by_user_id, reported_by_user_name, reported_at,
                   status, reviewed_by, reviewed_at, action_taken, admin_notes
            FROM reports
            WHERE id = @Id;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<DataModels.ReportDto>(
            sql,
            new { Id = id });

        return dto?.ToReport().ToUiModel();
    }

    public async Task<List<Report>> GetPendingReportsAsync(long? chatId = null)
    {
        return await GetReportsAsync(chatId, ReportStatus.Pending);
    }

    public async Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var sql = """
            SELECT id, message_id, chat_id, report_command_message_id,
                   reported_by_user_id, reported_by_user_name, reported_at,
                   status, reviewed_by, reviewed_at, action_taken, admin_notes
            FROM reports
            WHERE 1=1
            """;

        var parameters = new DynamicParameters();

        if (chatId.HasValue)
        {
            sql += " AND chat_id = @ChatId";
            parameters.Add("ChatId", chatId.Value);
        }

        if (status.HasValue)
        {
            sql += " AND status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        sql += " ORDER BY reported_at DESC LIMIT @Limit OFFSET @Offset;";
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);

        var dtos = await connection.QueryAsync<DataModels.ReportDto>(sql, parameters);

        return dtos.Select(dto => dto.ToReport().ToUiModel()).ToList();
    }

    public async Task UpdateReportStatusAsync(
        long reportId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE reports
            SET status = @Status,
                reviewed_by = @ReviewedBy,
                reviewed_at = @ReviewedAt,
                action_taken = @ActionTaken,
                admin_notes = @AdminNotes
            WHERE id = @ReportId;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await connection.ExecuteAsync(sql, new
        {
            ReportId = reportId,
            Status = (int)status,
            ReviewedBy = reviewedBy,
            ReviewedAt = now,
            ActionTaken = actionTaken,
            AdminNotes = adminNotes
        });

        _logger.LogInformation(
            "Updated report {ReportId} to status {Status} by user {ReviewedBy} (action: {ActionTaken})",
            reportId,
            status,
            reviewedBy,
            actionTaken);
    }

    public async Task<int> GetPendingCountAsync(long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var sql = "SELECT COUNT(*) FROM reports WHERE status = @Status";
        var parameters = new DynamicParameters();
        parameters.Add("Status", (int)ReportStatus.Pending);

        if (chatId.HasValue)
        {
            sql += " AND chat_id = @ChatId";
            parameters.Add("ChatId", chatId.Value);
        }

        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    public async Task DeleteOldReportsAsync(long olderThanTimestamp)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            DELETE FROM reports
            WHERE reported_at < @OlderThanTimestamp
              AND status != @PendingStatus;
            """;

        var deleted = await connection.ExecuteAsync(sql, new
        {
            OlderThanTimestamp = olderThanTimestamp,
            PendingStatus = (int)ReportStatus.Pending
        });

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} old reports (older than {Timestamp})",
                deleted,
                olderThanTimestamp);
        }
    }
}

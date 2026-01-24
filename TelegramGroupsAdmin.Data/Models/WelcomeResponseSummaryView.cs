using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for welcome response summary statistics.
/// Maps to welcome_response_summary PostgreSQL view.
/// Pre-aggregates welcome response distributions by date and chat.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// NOTE: Regular VIEW = stored query definition, no rows stored. Query runs fresh each time.
/// </summary>
public class WelcomeResponseSummaryView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the welcome_response_summary view. Referenced by migrations.
    /// Consolidates three welcome analytics methods:
    /// - GetWelcomeStatsSummaryAsync
    /// - GetWelcomeResponseDistributionAsync
    /// - GetChatWelcomeStatsAsync
    /// Response enum values: 1=Accepted, 2=Denied, 3=Timeout, 4=Left
    /// </summary>
    public const string CreateViewSql = """
        CREATE VIEW welcome_response_summary AS
        SELECT
            wr.chat_id,
            mc.chat_name,
            DATE(wr.created_at) AS join_date,
            COUNT(*) AS total_joins,
            COUNT(*) FILTER (WHERE wr.response = 1) AS accepted_count,
            COUNT(*) FILTER (WHERE wr.response = 2) AS denied_count,
            COUNT(*) FILTER (WHERE wr.response = 3) AS timeout_count,
            COUNT(*) FILTER (WHERE wr.response = 4) AS left_count,
            AVG(EXTRACT(EPOCH FROM (wr.responded_at - wr.created_at)))
                FILTER (WHERE wr.response = 1) AS avg_accept_seconds
        FROM welcome_responses wr
        LEFT JOIN managed_chats mc ON wr.chat_id = mc.chat_id
        GROUP BY wr.chat_id, mc.chat_name, DATE(wr.created_at);
        """;

    /// <summary>
    /// SQL to drop the welcome_response_summary view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS welcome_response_summary";

    #endregion

    #region Grouping Columns

    /// <summary>
    /// Chat ID for the welcome responses
    /// </summary>
    [Column("chat_id")]
    public long ChatId { get; set; }

    /// <summary>
    /// Chat name from managed_chats
    /// </summary>
    [Column("chat_name")]
    public string? ChatName { get; set; }

    /// <summary>
    /// Date of the joins (UTC date from created_at)
    /// </summary>
    [Column("join_date")]
    public DateOnly JoinDate { get; set; }

    #endregion

    #region Aggregation Columns

    /// <summary>
    /// Total number of joins for this chat on this date
    /// </summary>
    [Column("total_joins")]
    public long TotalJoins { get; set; }

    /// <summary>
    /// Number of accepted (passed challenge) responses
    /// </summary>
    [Column("accepted_count")]
    public long AcceptedCount { get; set; }

    /// <summary>
    /// Number of denied (failed challenge) responses
    /// </summary>
    [Column("denied_count")]
    public long DeniedCount { get; set; }

    /// <summary>
    /// Number of timeout responses
    /// </summary>
    [Column("timeout_count")]
    public long TimeoutCount { get; set; }

    /// <summary>
    /// Number of users who left before responding
    /// </summary>
    [Column("left_count")]
    public long LeftCount { get; set; }

    /// <summary>
    /// Average time in seconds for accepted responses
    /// </summary>
    [Column("avg_accept_seconds")]
    public double? AvgAcceptSeconds { get; set; }

    #endregion
}

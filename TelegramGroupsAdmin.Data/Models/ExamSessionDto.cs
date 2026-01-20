using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for exam_sessions table.
/// Tracks entrance exam progress for users going through the exam welcome flow.
/// </summary>
[Table("exam_sessions")]
public class ExamSessionDto
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// Current question index (0-based) in the exam flow.
    /// </summary>
    [Column("current_question_index")]
    public short CurrentQuestionIndex { get; set; }

    /// <summary>
    /// User's multiple choice answers stored as JSONB.
    /// Format: {"0": "B", "1": "A", "2": "C"} (question index → selected answer)
    /// </summary>
    [Column("mc_answers")]
    public string? McAnswers { get; set; }

    /// <summary>
    /// Answer shuffle state for randomization stored as JSONB.
    /// Format: {"0": [2,0,1,3], "1": [1,3,0,2]} (question index → answer display order)
    /// </summary>
    [Column("shuffle_state")]
    public string? ShuffleState { get; set; }

    /// <summary>
    /// User's answer to the open-ended question (if configured).
    /// </summary>
    [Column("open_ended_answer")]
    public string? OpenEndedAnswer { get; set; }

    [Column("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// When this session expires (matches welcome timeout).
    /// Cleanup job removes expired sessions.
    /// </summary>
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}

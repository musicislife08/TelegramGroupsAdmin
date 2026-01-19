using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing entrance exam sessions.
/// Uses ExamSessionDto from Data layer, maps to ExamSession domain model.
/// </summary>
public class ExamSessionRepository : IExamSessionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ExamSessionRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ExamSessionRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ExamSessionRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> CreateSessionAsync(
        long chatId,
        long userId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new ExamSessionDto
        {
            ChatId = chatId,
            UserId = userId,
            CurrentQuestionIndex = 0,
            StartedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

        context.ExamSessions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created exam session #{SessionId} for user {UserId} in chat {ChatId} (expires: {ExpiresAt})",
            entity.Id, userId, chatId, expiresAt);

        return entity.Id;
    }

    public async Task<ExamSession?> GetSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExamSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ChatId == chatId && s.UserId == userId, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<ExamSession?> GetByIdAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExamSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        return entity?.ToModel();
    }

    public async Task RecordMcAnswerAsync(
        long sessionId,
        int questionIndex,
        string answer,
        int[]? shuffleState = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExamSessions.FindAsync([sessionId], cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Exam session {SessionId} not found for MC answer", sessionId);
            return;
        }

        // Parse existing answers or create new
        var answers = string.IsNullOrEmpty(entity.McAnswers)
            ? new Dictionary<int, string>()
            : JsonSerializer.Deserialize<Dictionary<int, string>>(entity.McAnswers, JsonOptions)
              ?? new Dictionary<int, string>();

        answers[questionIndex] = answer;
        entity.McAnswers = JsonSerializer.Serialize(answers, JsonOptions);

        // Update shuffle state if provided
        if (shuffleState != null)
        {
            var shuffles = string.IsNullOrEmpty(entity.ShuffleState)
                ? new Dictionary<int, int[]>()
                : JsonSerializer.Deserialize<Dictionary<int, int[]>>(entity.ShuffleState, JsonOptions)
                  ?? new Dictionary<int, int[]>();

            shuffles[questionIndex] = shuffleState;
            entity.ShuffleState = JsonSerializer.Serialize(shuffles, JsonOptions);
        }

        // Advance to next question
        entity.CurrentQuestionIndex = (short)(questionIndex + 1);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Recorded MC answer for session {SessionId}: Q{QuestionIndex}={Answer}",
            sessionId, questionIndex, answer);
    }

    public async Task RecordOpenEndedAnswerAsync(
        long sessionId,
        string answer,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExamSessions.FindAsync([sessionId], cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Exam session {SessionId} not found for open-ended answer", sessionId);
            return;
        }

        entity.OpenEndedAnswer = answer;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Recorded open-ended answer for session {SessionId} ({Length} chars)",
            sessionId, answer.Length);
    }

    public async Task DeleteSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var deleted = await context.ExamSessions
            .Where(s => s.Id == sessionId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted exam session {SessionId}", sessionId);
        }
    }

    public async Task DeleteSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var deleted = await context.ExamSessions
            .Where(s => s.ChatId == chatId && s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted exam session for user {UserId} in chat {ChatId}",
                userId, chatId);
        }
    }

    public async Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var deleted = await context.ExamSessions
            .Where(s => s.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} expired exam sessions", deleted);
        }

        return deleted;
    }

    public async Task<bool> HasActiveSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        return await context.ExamSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.ChatId == chatId &&
                s.UserId == userId &&
                s.ExpiresAt > now,
                cancellationToken);
    }

    public async Task<List<ExamSession>> GetActiveSessionsAsync(
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entities = await context.ExamSessions
            .AsNoTracking()
            .Where(s => s.ChatId == chatId && s.ExpiresAt > now)
            .OrderBy(s => s.StartedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<ExamSession?> GetActiveSessionForUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entity = await context.ExamSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.ExpiresAt > now)
            .OrderByDescending(s => s.StartedAt)  // Most recent first
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }
}

/// <summary>
/// Extension methods for mapping ExamSessionDto to domain model.
/// </summary>
internal static class ExamSessionMappings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ExamSession ToModel(this ExamSessionDto dto)
    {
        Dictionary<int, string>? mcAnswers = null;
        Dictionary<int, int[]>? shuffleState = null;

        if (!string.IsNullOrEmpty(dto.McAnswers))
        {
            mcAnswers = JsonSerializer.Deserialize<Dictionary<int, string>>(dto.McAnswers, JsonOptions);
        }

        if (!string.IsNullOrEmpty(dto.ShuffleState))
        {
            shuffleState = JsonSerializer.Deserialize<Dictionary<int, int[]>>(dto.ShuffleState, JsonOptions);
        }

        return new ExamSession
        {
            Id = dto.Id,
            ChatId = dto.ChatId,
            UserId = dto.UserId,
            CurrentQuestionIndex = dto.CurrentQuestionIndex,
            McAnswers = mcAnswers,
            ShuffleState = shuffleState,
            OpenEndedAnswer = dto.OpenEndedAnswer,
            StartedAt = dto.StartedAt,
            ExpiresAt = dto.ExpiresAt
        };
    }
}

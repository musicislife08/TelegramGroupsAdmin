using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class ImpersonationAlertsRepository : IImpersonationAlertsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ImpersonationAlertsRepository> _logger;

    public ImpersonationAlertsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ImpersonationAlertsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<int> CreateAlertAsync(ImpersonationAlertRecord alert, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = alert.ToDto();
        context.ImpersonationAlerts.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created impersonation alert #{AlertId}: User {SuspectedUserId} → Admin {TargetUserId} (score: {Score}, auto_banned: {AutoBanned})",
            entity.Id,
            alert.SuspectedUserId,
            alert.TargetUserId,
            alert.TotalScore,
            alert.AutoBanned);

        return entity.Id;
    }

    public async Task<List<ImpersonationAlertRecord>> GetPendingAlertsAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = from alert in context.ImpersonationAlerts
                    where alert.ReviewedAt == null
                    join suspected in context.TelegramUsers on alert.SuspectedUserId equals suspected.TelegramUserId
                    join target in context.TelegramUsers on alert.TargetUserId equals target.TelegramUserId
                    join chat in context.ManagedChats on alert.ChatId equals chat.ChatId into chatGroup
                    from c in chatGroup.DefaultIfEmpty()
                    join reviewer in context.Users on alert.ReviewedByUserId equals reviewer.Id into reviewerGroup
                    from r in reviewerGroup.DefaultIfEmpty()
                    select new
                    {
                        Alert = alert,
                        SuspectedUserName = suspected.Username,
                        SuspectedFirstName = suspected.FirstName,
                        SuspectedPhotoPath = suspected.UserPhotoPath,
                        TargetUserName = target.Username,
                        TargetFirstName = target.FirstName,
                        TargetPhotoPath = target.UserPhotoPath,
                        ChatName = c != null ? c.ChatName : null,
                        ReviewedByEmail = r != null ? r.Email : null
                    };

        if (chatId.HasValue)
        {
            query = query.Where(x => x.Alert.ChatId == chatId.Value);
        }

        var results = await query
            .AsNoTracking()
            .OrderByDescending(x => x.Alert.RiskLevel)  // Critical first
            .ThenByDescending(x => x.Alert.DetectedAt) // Newest first
            .ToListAsync(cancellationToken);

        return results.Select(x => x.Alert.ToModel(
            suspectedUserName: x.SuspectedUserName,
            suspectedFirstName: x.SuspectedFirstName,
            suspectedPhotoPath: x.SuspectedPhotoPath,
            targetUserName: x.TargetUserName,
            targetFirstName: x.TargetFirstName,
            targetPhotoPath: x.TargetPhotoPath,
            chatName: x.ChatName,
            reviewedByEmail: x.ReviewedByEmail)).ToList();
    }

    public async Task<ImpersonationAlertRecord?> GetAlertAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = await (
            from alert in context.ImpersonationAlerts
            where alert.Id == id
            join suspected in context.TelegramUsers on alert.SuspectedUserId equals suspected.TelegramUserId
            join target in context.TelegramUsers on alert.TargetUserId equals target.TelegramUserId
            join chat in context.ManagedChats on alert.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            join reviewer in context.Users on alert.ReviewedByUserId equals reviewer.Id into reviewerGroup
            from r in reviewerGroup.DefaultIfEmpty()
            select new
            {
                Alert = alert,
                SuspectedUserName = suspected.Username,
                SuspectedFirstName = suspected.FirstName,
                SuspectedPhotoPath = suspected.UserPhotoPath,
                TargetUserName = target.Username,
                TargetFirstName = target.FirstName,
                TargetPhotoPath = target.UserPhotoPath,
                ChatName = c != null ? c.ChatName : null,
                ReviewedByEmail = r != null ? r.Email : null
            }
        )
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

        return result?.Alert.ToModel(
            suspectedUserName: result.SuspectedUserName,
            suspectedFirstName: result.SuspectedFirstName,
            suspectedPhotoPath: result.SuspectedPhotoPath,
            targetUserName: result.TargetUserName,
            targetFirstName: result.TargetFirstName,
            targetPhotoPath: result.TargetPhotoPath,
            chatName: result.ChatName,
            reviewedByEmail: result.ReviewedByEmail);
    }

    public async Task UpdateVerdictAsync(
        int id,
        ImpersonationVerdict verdict,
        string reviewedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ImpersonationAlerts.FindAsync([id], cancellationToken);

        if (entity != null)
        {
            entity.Verdict = verdict;
            entity.ReviewedByUserId = reviewedByUserId;
            entity.ReviewedAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated impersonation alert #{AlertId}: Verdict = {Verdict}, ReviewedBy = {ReviewerId}",
                id,
                verdict,
                reviewedByUserId);
        }
    }

    public async Task<bool> HasPendingAlertAsync(long suspectedUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ImpersonationAlerts
            .AsNoTracking()
            .AnyAsync(a => a.SuspectedUserId == suspectedUserId && a.ReviewedAt == null, cancellationToken);
    }

    public async Task<List<ImpersonationAlertRecord>> GetAlertHistoryAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var results = await (
            from alert in context.ImpersonationAlerts
            where alert.SuspectedUserId == userId
            join suspected in context.TelegramUsers on alert.SuspectedUserId equals suspected.TelegramUserId
            join target in context.TelegramUsers on alert.TargetUserId equals target.TelegramUserId
            join chat in context.ManagedChats on alert.ChatId equals chat.ChatId into chatGroup
            from c in chatGroup.DefaultIfEmpty()
            join reviewer in context.Users on alert.ReviewedByUserId equals reviewer.Id into reviewerGroup
            from r in reviewerGroup.DefaultIfEmpty()
            orderby alert.DetectedAt descending
            select new
            {
                Alert = alert,
                SuspectedUserName = suspected.Username,
                SuspectedFirstName = suspected.FirstName,
                SuspectedPhotoPath = suspected.UserPhotoPath,
                TargetUserName = target.Username,
                TargetFirstName = target.FirstName,
                TargetPhotoPath = target.UserPhotoPath,
                ChatName = c != null ? c.ChatName : null,
                ReviewedByEmail = r != null ? r.Email : null
            }
        )
        .AsNoTracking()
        .ToListAsync(cancellationToken);

        return results.Select(x => x.Alert.ToModel(
            suspectedUserName: x.SuspectedUserName,
            suspectedFirstName: x.SuspectedFirstName,
            suspectedPhotoPath: x.SuspectedPhotoPath,
            targetUserName: x.TargetUserName,
            targetFirstName: x.TargetFirstName,
            targetPhotoPath: x.TargetPhotoPath,
            chatName: x.ChatName,
            reviewedByEmail: x.ReviewedByEmail)).ToList();
    }
}

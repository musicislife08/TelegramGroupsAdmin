using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

public class NotificationPreferencesRepository(IDbContextFactory<AppDbContext> contextFactory)
    : INotificationPreferencesRepository
{
    public async Task<NotificationConfig?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        return entity?.ToModel();
    }

    public async Task<NotificationConfig> GetOrCreateAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        if (entity != null)
        {
            return entity.ToModel();
        }

        // Create new with empty channels
        var newEntity = new DataModels.NotificationPreferencesDto
        {
            UserId = userId,
            Config = """{"channels":[]}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.NotificationPreferences.Add(newEntity);
        await context.SaveChangesAsync(ct);

        return new NotificationConfig();
    }

    public async Task SaveAsync(string userId, NotificationConfig config, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var entity = await context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId, ct);

        var configJson = config.ToConfigJson();

        if (entity != null)
        {
            // Update existing - use ExecuteUpdate for records
            await context.NotificationPreferences
                .Where(np => np.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(np => np.Config, configJson)
                    .SetProperty(np => np.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }
        else
        {
            // Create new
            var newEntity = new DataModels.NotificationPreferencesDto
            {
                UserId = userId,
                Config = configJson,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            context.NotificationPreferences.Add(newEntity);
            await context.SaveChangesAsync(ct);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Writes a telegram_users row for the legacy E2E builders. The app records names only through
/// observations (ITelegramUserRepository.GetOrUpdateAsync), which cannot set the flags these
/// builders take, so the builders write the row directly: inserted, or overwritten when present.
/// </summary>
internal static class TelegramUserRowWriter
{
    public static async Task WriteAsync(IServiceProvider services, TelegramUser user, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        var row = await context.TelegramUsers.FirstOrDefaultAsync(u => u.TelegramUserId == user.TelegramUserId, cancellationToken);
        if (row is null)
        {
            row = new TelegramUserDto { TelegramUserId = user.TelegramUserId, CreatedAt = user.CreatedAt, FirstSeenAt = user.FirstSeenAt };
            context.TelegramUsers.Add(row);
        }

        row.Username = user.Username;
        row.FirstName = user.FirstName;
        row.LastName = user.LastName;
        row.UserPhotoPath = user.UserPhotoPath;
        row.PhotoHash = user.PhotoHash;
        row.PhotoFileUniqueId = user.PhotoFileUniqueId;
        row.IsBot = user.IsBot;
        row.IsTrusted = user.IsTrusted;
        row.IsActive = user.IsActive;
        row.BotDmEnabled = user.BotDmEnabled;
        row.LastSeenAt = user.LastSeenAt;
        row.UpdatedAt = user.UpdatedAt;

        await context.SaveChangesAsync(cancellationToken);
    }
}

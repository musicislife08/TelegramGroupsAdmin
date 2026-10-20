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

    /// <summary>
    /// Inserts an untrusted, inactive telegram_users row for a message author the test has not built, the way the
    /// app's first observation does; an existing row is left as it is. messages.user_id references
    /// telegram_users, so a message's author row must exist before the message is stored.
    /// </summary>
    public static async Task EnsureAuthorAsync(IServiceProvider services, long userId, string? username,
        string? firstName, string? lastName, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var seen = seenAt.ToUniversalTime();

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO telegram_users (
                telegram_user_id, username, first_name, last_name,
                is_bot, is_trusted, is_banned, bot_dm_enabled,
                first_seen_at, last_seen_at, created_at, updated_at, is_active
            ) VALUES (
                {userId}, {username}, {firstName}, {lastName},
                {false}, {false}, {false}, {false},
                {seen}, {seen}, {now}, {now}, {false}
            )
            ON CONFLICT (telegram_user_id) DO NOTHING
            """, cancellationToken);
    }
}

using System.Globalization;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>
/// Builds and parses the /start deep link used by the DM ban celebration start prompt.
/// The payload carries only the chat id — never a message id — so a crafted link can only
/// confirm the sender's own existing subscription, never choose what the bot deletes.
/// </summary>
public static class DmCelebrationDeepLink
{
    private const string Prefix = "dmcel_";

    public static string Build(string botUsername, long chatId) =>
        $"https://t.me/{botUsername}?start={Prefix}{chatId.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParseChatId(string? payload, out long chatId)
    {
        chatId = 0;
        return payload is not null
               && payload.StartsWith(Prefix, StringComparison.Ordinal)
               && long.TryParse(payload.AsSpan(Prefix.Length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out chatId);
    }
}

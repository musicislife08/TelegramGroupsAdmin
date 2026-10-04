namespace TelegramGroupsAdmin.Telegram.Extensions;

public static class TelegramDateExtensions
{
    extension(DateTime date)
    {
        /// <summary>
        /// A Telegram.Bot date (message, edit or chat-member update date) as a UTC offset. Telegram
        /// dates are UTC; the kind is set explicitly so an Unspecified value is not read as local time.
        /// </summary>
        public DateTimeOffset ToUtcOffset() => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
    }
}

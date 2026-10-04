using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Configuration.Services;

public static class ConfigServiceMessageBuilderExtensions
{
    /// <summary>
    /// A builder for text the bot posts into <paramref name="chatId"/>, set to that chat's
    /// "Mask flagged names" value. Direct messages to a person use
    /// <c>TelegramMessageBuilder.For(NameMasking.Off)</c> instead: they are never masked.
    /// </summary>
    public static async ValueTask<TelegramMessageBuilder> CreateChatMessageBuilderAsync(
        this IConfigService configService, long chatId, CancellationToken ct = default)
        => TelegramMessageBuilder.For(await configService.GetNameMaskingAsync(chatId, ct));
}

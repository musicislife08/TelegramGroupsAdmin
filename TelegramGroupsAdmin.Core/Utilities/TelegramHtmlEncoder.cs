using System.Net;

namespace TelegramGroupsAdmin.Core.Utilities;

public static class TelegramHtmlEncoder
{
    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
}

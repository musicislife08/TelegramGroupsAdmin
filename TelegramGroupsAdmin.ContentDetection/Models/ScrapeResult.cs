using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Models;

internal sealed record ScrapeResult(string Url, SeoPreview? Preview);

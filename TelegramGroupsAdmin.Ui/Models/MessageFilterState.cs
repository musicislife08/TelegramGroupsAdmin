using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// State for message filtering in the Messages page.
/// </summary>
public class MessageFilterState
{
    public string? SearchText { get; set; }
    public string? UserName { get; set; }
    public string? ChatName { get; set; }
    public SpamFilterOption SpamFilter { get; set; } = SpamFilterOption.All;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? HasImages { get; set; }
    public bool? HasLinks { get; set; }
    public bool? HasEdits { get; set; }

    public bool Matches(MessageRecord message, ContentCheckRecord? contentCheck)
    {
        // Text search
        if (!string.IsNullOrEmpty(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            var matchesText = message.MessageText?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) == true;
            var matchesUrls = message.Urls?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) == true;
            if (!matchesText && !matchesUrls)
                return false;
        }

        // User filter
        if (!string.IsNullOrEmpty(UserName))
        {
            if (message.UserName?.Contains(UserName, StringComparison.OrdinalIgnoreCase) != true)
                return false;
        }

        // Chat filter
        if (!string.IsNullOrEmpty(ChatName))
        {
            if (message.ChatName?.Contains(ChatName, StringComparison.OrdinalIgnoreCase) != true)
                return false;
        }

        // Spam filter
        if (SpamFilter != SpamFilterOption.All)
        {
            var hasCheck = contentCheck != null;
            switch (SpamFilter)
            {
                case SpamFilterOption.SpamOnly:
                    if (!hasCheck || !contentCheck!.IsSpam)
                        return false;
                    break;
                case SpamFilterOption.CleanOnly:
                    if (!hasCheck || contentCheck!.IsSpam)
                        return false;
                    break;
                case SpamFilterOption.Unchecked:
                    if (hasCheck)
                        return false;
                    break;
            }
        }

        // Date range
        var messageDate = message.Timestamp.Date;
        if (StartDate.HasValue && messageDate < StartDate.Value.Date)
            return false;
        if (EndDate.HasValue && messageDate > EndDate.Value.Date)
            return false;

        // Content type filters
        if (HasImages.HasValue)
        {
            var hasImage = !string.IsNullOrEmpty(message.PhotoFileId);
            if (HasImages.Value != hasImage)
                return false;
        }

        if (HasLinks.HasValue)
        {
            var hasLink = !string.IsNullOrEmpty(message.Urls);
            if (HasLinks.Value != hasLink)
                return false;
        }

        if (HasEdits.HasValue)
        {
            var hasEdit = message.EditDate.HasValue;
            if (HasEdits.Value != hasEdit)
                return false;
        }

        return true;
    }
}

public enum SpamFilterOption
{
    All,
    SpamOnly,
    CleanOnly,
    Unchecked
}

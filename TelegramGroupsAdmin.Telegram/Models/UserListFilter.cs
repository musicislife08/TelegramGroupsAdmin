namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Users page tab filters. <see cref="All"/> applies no status predicate and is the
/// guaranteed-visible view; the others are filtered projections of it.
/// </summary>
public enum UserListFilter { All, Active, Tagged, Trusted, Kicked }

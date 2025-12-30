namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response containing all profile page data.
/// </summary>
public record ProfilePageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    // Account Information
    public string Email { get; init; } = string.Empty;
    public int PermissionLevel { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }

    // TOTP Status
    public bool TotpEnabled { get; init; }

    // Linked Telegram Accounts
    public List<LinkedTelegramAccountDto> LinkedAccounts { get; init; } = [];

    public static ProfilePageResponse Ok(
        string email,
        int permissionLevel,
        DateTimeOffset createdAt,
        DateTimeOffset? lastLoginAt,
        bool totpEnabled,
        List<LinkedTelegramAccountDto> linkedAccounts) => new()
    {
        Success = true,
        Email = email,
        PermissionLevel = permissionLevel,
        CreatedAt = createdAt,
        LastLoginAt = lastLoginAt,
        TotpEnabled = totpEnabled,
        LinkedAccounts = linkedAccounts
    };

    public static ProfilePageResponse Fail(string error) => new() { Success = false, Error = error };
}

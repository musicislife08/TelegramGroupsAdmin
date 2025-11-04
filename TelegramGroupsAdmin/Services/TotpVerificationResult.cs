namespace TelegramGroupsAdmin.Services;

public record TotpVerificationResult(
    bool Success,
    bool SetupExpired,
    string? ErrorMessage
);

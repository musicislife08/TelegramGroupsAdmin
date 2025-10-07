namespace TelegramGroupsAdmin;

public record CheckResult(
    bool Spam,
    string? Reason,
    int Confidence = 0
);
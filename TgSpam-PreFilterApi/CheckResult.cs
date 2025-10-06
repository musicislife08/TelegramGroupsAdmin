namespace TgSpam_PreFilterApi;

public record CheckResult(
    bool Spam,
    string? Reason,
    int Confidence = 0
);
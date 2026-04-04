using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>Result from the full two-layer scoring pipeline.</summary>
public record ScoringResult(
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    string? AiReason,
    string[]? AiSignals,
    bool ContainsNudity = false);

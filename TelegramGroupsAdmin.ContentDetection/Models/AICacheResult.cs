using TelegramGroupsAdmin.AI.Services;

namespace TelegramGroupsAdmin.ContentDetection.Models;

public sealed record AICacheResult(ChatCompletionResult Result, bool FromCache);

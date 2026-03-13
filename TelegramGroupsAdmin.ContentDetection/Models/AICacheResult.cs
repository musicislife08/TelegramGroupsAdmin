using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ContentDetection.Models;

public sealed record AICacheResult(ChatCompletionResult Result, bool FromCache);

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Production implementation backed by a ConcurrentDictionary.
/// Singleton lifetime — one instance per application.
/// </summary>
sealed class AuthFlowStore : IAuthFlowStore
{
    private readonly ConcurrentDictionary<string, AuthFlowContext> _flows = new();

    public bool TryAdd(string webUserId, AuthFlowContext context)
        => _flows.TryAdd(webUserId, context);

    public bool TryGetValue(string webUserId, [MaybeNullWhen(false)] out AuthFlowContext context)
        => _flows.TryGetValue(webUserId, out context);

    public bool TryRemove(string webUserId, [MaybeNullWhen(false)] out AuthFlowContext context)
        => _flows.TryRemove(webUserId, out context);
}

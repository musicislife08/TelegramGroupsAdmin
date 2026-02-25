using System.Diagnostics.CodeAnalysis;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Thread-safe storage for active authentication flow contexts.
/// Registered as a singleton because auth flows span multiple scoped service lifetimes.
/// </summary>
public interface IAuthFlowStore
{
    bool TryAdd(string webUserId, AuthFlowContext context);
    bool TryGetValue(string webUserId, [MaybeNullWhen(false)] out AuthFlowContext context);
    bool TryRemove(string webUserId, [MaybeNullWhen(false)] out AuthFlowContext context);
}

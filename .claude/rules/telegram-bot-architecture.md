---
paths:
  - "**/*Handler*.cs"
  - "**/*Service*.cs"
  - "**/Services/Bot/**/*.cs"
---

# Telegram Bot Architecture

## Layer Rules
- **Application layer** (Commands, Jobs, most Services): Use `IBot*Service` only. NEVER use handlers or `ITelegramBotClientFactory`
- **Bot Services** (`Services/Bot/*.cs`): Can use `IBot*Handler` directly. Add DB/cache/business logic
- **Handlers** (`Services/Bot/Handlers/*.cs`): ONLY layer using `ITelegramBotClientFactory`. Thin API wrappers
- **Sanctioned exception**: `TelegramBotPollingHost` (`Services/BackgroundServices/`) also injects `ITelegramBotClientFactory`. It is infrastructure that owns the single polling connection and needs the raw client; no `IBot*Service` covers that. Do not flag it in layering reviews

## Available Services
- `IBotMessageService`: SendAndSaveMessageAsync, EditAndUpdateMessageAsync, DeleteAndMarkMessageAsync
- `IBotChatService`: GetChatAsync, GetInviteLinkAsync, LeaveChatAsync, RefreshChatAdminsAsync
- `IBotUserService`: GetMeAsync, GetChatMemberAsync, IsAdminAsync
- `IBotMediaService`: GetFileAsync, DownloadFileAsync, GetUserPhotoAsync, GetChatIconAsync
- `IBotModerationService`: BanAsync, UnbanAsync, RestrictAsync, KickAsync
- `IBotDmService`: SendDmAsync, SendDmWithKeyboardAsync, SendDmWithEntitiesAsync, SendDmWithMediaAndKeyboardEntitiesAsync, SendDmWithAnimationEntitiesAsync

## Singleton + Scoped Pattern
Handlers are Scoped. If service is Singleton, create scope:
```csharp
using var scope = _serviceProvider.CreateScope();
var handler = scope.ServiceProvider.GetRequiredService<IBotChatHandler>();
```

## Legacy Note
The `ITelegramOperations` abstraction was removed and replaced with specialized `IBot*Service` interfaces.

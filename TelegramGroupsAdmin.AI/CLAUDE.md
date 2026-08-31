# TelegramGroupsAdmin.AI - AI Reference

## Project Role

Owns AI service abstractions and implementations: chat completion via Microsoft.Extensions.AI (IChatClient), AI-based translation, AI feature factories, and AI feature test runners.

## Dependencies

References: `Configuration` (for `AIProviderConfig` and friends), `Core` (for metrics + `IAuditService`), `Data` (for shared primitives).
Consumed by: `Telegram`, `Host` (`TelegramGroupsAdmin`).

## Design Rules

- All AI services are `Scoped` (matching the lifetime of `ISystemConfigRepository`, which they consume).
- The IChatClient cache in `ChatService` is `static` and persists across scoped instances — do not turn it into instance state. Evicted clients are disposed (IChatClient : IDisposable).
- This project owns the `Microsoft.Extensions.AI*` package references. Do not add them to `Core` or anywhere else.

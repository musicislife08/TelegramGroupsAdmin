# TelegramGroupsAdmin.AI - AI Reference

## Project Role

Owns AI service abstractions and implementations: chat completion via Semantic Kernel, AI-based translation, AI feature factories, and AI feature test runners.

## Dependencies

References: `Configuration` (for `AIProviderConfig` and friends), `Core` (for metrics + `IAuditService`), `Data` (for shared primitives).
Consumed by: `Telegram`, `Host` (`TelegramGroupsAdmin`).

## Design Rules

- All AI services are `Scoped` (matching the lifetime of `ISystemConfigRepository`, which they consume).
- The Semantic Kernel kernel cache in `SemanticKernelChatService` is `static` and persists across scoped instances — do not turn it into instance state.
- This project owns the `Microsoft.SemanticKernel` package reference. Do not add it to `Core` or anywhere else.

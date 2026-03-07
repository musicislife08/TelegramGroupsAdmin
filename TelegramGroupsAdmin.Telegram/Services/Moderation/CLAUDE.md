# Moderation System Architecture

## Boss/Worker Pattern

This folder implements a **Boss/Worker orchestration pattern** for moderation actions.

### The Boss: `BotModerationService`

The orchestrator is the "boss" that:
- **Knows all workers** (handlers) but workers don't know each other
- **Owns business rules** like "bans revoke trust" or "N warnings = auto-ban"
- **Composes workflows** by calling handlers in sequence
- **Protects system accounts** (Telegram system IDs: 777000, 1087968824, etc.)

The orchestrator should:
- Call handlers to perform domain actions
- Coordinate multi-step workflows
- Apply cross-cutting business rules

The orchestrator should NOT:
- Implement domain logic directly
- Know about infrastructure details (job schedulers, API clients)
- Have complex conditional logic that belongs in a handler

### The Workers: Handlers

Handlers are **domain experts** that know how to do one thing well. They:
- Own domain-specific knowledge
- Don't know about other handlers
- Return results for the orchestrator to interpret

## Folder Structure

```
Moderation/
├── Actions/                     # Domain action handlers
│   ├── Results/                 # Result types for handlers
│   └── ...Handler.cs           # Domain handlers (Trust, Warn, etc.)
│
├── Handlers/                    # Support handlers
│   ├── AuditHandler.cs         # Log actions to audit trail
│   ├── NotificationHandler.cs  # Notify admins of actions
│   └── TrainingHandler.cs      # Create ML training samples
│
└── Infrastructure/              # Cross-cutting infrastructure

Bot/
├── BotModerationService.cs     # The boss - coordinates all handlers
├── IBotModerationService.cs    # Public interface for external callers
└── Handlers/                   # Bot API wrappers
    ├── BotBanHandler.cs        # Ban/unban users across chats
    ├── BotRestrictHandler.cs   # Restrict/unrestrict permissions
    └── BotModerationMessageHandler.cs  # Delete messages, backfill, cleanup
```

## Adding New Functionality

### When to extend an existing handler:
- The new functionality is within the handler's domain
- Example: Adding `ScheduleUserMessageCleanupAsync()` to `BotModerationMessageHandler`

### When to create a new handler:
- The functionality represents a distinct domain
- No existing handler owns this responsibility
- Example: A hypothetical `MuteHandler` for time-based muting

### When to add logic to the orchestrator:
- It's a business rule that spans multiple handlers
- Example: "After 3 warnings, auto-ban the user"

## Example Workflow: BanUserAsync

```
BotModerationService.BanUserAsync(userId, executor, reason)
    │
    ├─→ Check system account protection
    │
    ├─→ BotBanHandler.BanAsync()         # Domain: ban across all chats
    │
    ├─→ AuditHandler.LogBanAsync()       # Support: record in audit log
    │
    ├─→ TrustHandler.UntrustAsync()      # Business rule: bans revoke trust
    │
    ├─→ NotificationHandler.NotifyAdminsBanAsync()  # Support: notify admins
    │
    └─→ BotModerationMessageHandler.ScheduleUserMessagesCleanupAsync()  # Domain: cleanup
```

## Testing

- **Unit test handlers** in isolation with mocked dependencies
- **Unit test orchestrator** with mocked handlers to verify workflow composition
- Handler tests live in: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/`

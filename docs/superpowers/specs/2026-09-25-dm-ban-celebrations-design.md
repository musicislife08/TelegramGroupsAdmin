# DM Ban Celebrations — Design

**Date:** 2026-09-25
**Issues:** none filed yet — feature designed directly in session.

## Summary

Let a chat member opt into receiving that chat's ban celebrations (GIF + caption) as a Telegram
DM. This serves mixed-preference chats: an admin can turn the in-chat post off while members who
want the celebrations still get them privately.

A DM subscription is a **second, independent delivery target**. It is not a mode of the chat
config:

| Chat `Enabled` | Subscribers | Result |
|---|---|---|
| on | none | chat post only (today's behaviour) |
| on | some | chat post + subscriber DMs |
| off | some | subscriber DMs only |
| off | none | nothing — and **no GIF/caption is claimed from the rotation** |

The DM has the same GIF and caption as the chat post, with the chat name added as a header so a
user subscribed to several chats can tell where each one came from.

## Decisions

- **Command:** `/dmcelebrations on|off`. Bare `/dmcelebrations` reports the current state. Telegram
  command names allow only `[a-z0-9_]`, so hyphenated forms are not possible. The single-word form
  matches the existing commands (`mystatus`, `tempban`). "Subscribe" was rejected because users
  could confuse it with following the chat/bot or with Telegram's paid Stars subscriptions.
- **Config semantics:** `BanCelebrationConfig.Enabled` means "post in this chat", and nothing more.
  `TriggerOnAutoBan` / `TriggerOnManualBan` control only the chat post. Subscribers get a DM for
  every ban celebration in their chat.
- **No admin control over subscriptions.** A member can subscribe in any chat, at any time.
- **Members only.** A user can subscribe only from inside the group, which proves membership.
  Subscriptions are removed when the user leaves, is kicked, is banned, or blocks the bot.
- **Blocking the bot = unsubscribing** from every chat.
- **No DM `/dmcelebrations off`.** Users who want no more DMs block the bot, which unsubscribes them.
- **No raid throttling.** Subscribers get the same content the chat would get. Sends are only
  paced to stay under Telegram limits.
- **No web UI in v1.** A subscriber count or list in `BanCelebrationChatSettings.razor` can be
  added later with no schema change.

## Command behaviour

`DmCelebrationsCommand` — `PermissionLevel.Member`, deletes the command message. Confirmation
replies auto-delete after ~30s.

| Where | Input | Result |
|---|---|---|
| Group | `on`, `BotDmEnabled` true | Upsert row. Reply "✅ You'll get {chat}'s ban celebrations in your DMs." |
| Group | `on`, `BotDmEnabled` false | Upsert row. Post a **start prompt** (see below). |
| Group | `off` | Delete the row (idempotent) and confirm. If a prompt is open, clean it up. |
| Group | bare / other | Reply with the current state for this chat and a usage hint. |
| DM | anything | Reply "Run this in the chat you want celebrations from." |

### Start prompt lifecycle

Telegram sends no callback when a URL button is tapped. What the bot sees is the resulting
`/start <payload>` in the DM, so "tapped the button" and "enabled the conversation" are the same
event.

- The command sends the prompt **itself** through `IBotMessageService` with an inline URL button,
  and returns `TelegramMessage.Empty`. `CommandResult` cannot carry reply markup, and the command
  needs the sent message ID. The prompt text mentions the user with a `TextMention` so it's clear
  who the button is for in a busy group. The button deep-links `t.me/{bot}?start=dmcel_{chatId}`.
  Telegram shows **Start** to users who never started the bot and **Restart** to users who blocked
  it, so one prompt covers both cases.
- The command schedules a `DeleteMessageJob` for 60s and stores `prompt_message_id` and
  `prompt_delete_job_id` on the subscriber row.
- **`/start dmcel_{chatId}`** (new `StartCommand` branch): look up the row by
  `(sender id, chatId)`. If `prompt_delete_job_id` is set, `CancelJobAsync` it. Delete the prompt
  message and null both columns. Reply in the DM: "You'll get celebrations from {chat}." If no row
  exists (the user unsubscribed in the meantime), reply with a neutral message and change nothing.
- **Timeout:** the job deletes the prompt. The row stays: the user's intent stands, and DMs begin
  whenever `BotDmEnabled` turns true by any route.
- **Re-running `on` while a prompt is open:** clean up the old prompt first, then post the new one,
  so there's never more than one prompt per `(user, chat)`.
- Cleanup treats "job not found" and "message not found" as success. After a timeout, the columns
  still hold stale IDs, and they are nulled on the next cleanup.

**Security note:** the payload carries only the chat ID. The message ID comes from the sender's own
row. A payload carrying the message ID would let anyone craft a link that makes the bot delete an
arbitrary message in a managed chat.

## Data model

New table `ban_celebration_subscribers`, configured with the Fluent API in `AppDbContext` and then
`dotnet ef migrations add`.

| Column | Type | Notes |
|---|---|---|
| `telegram_user_id` | bigint | PK part, FK → `telegram_users`, cascade delete |
| `chat_id` | bigint | PK part, FK → `managed_chats`, cascade delete |
| `subscribed_at` | timestamptz | |
| `prompt_message_id` | int, nullable | open start-prompt message |
| `prompt_delete_job_id` | text, nullable | Quartz job for the 60s prompt delete |

- Composite PK `(telegram_user_id, chat_id)`. Subscribe is an upsert, unsubscribe is a delete.
- There is **no status column**. Whether a subscriber can receive DMs comes from the join to
  `telegram_users.bot_dm_enabled`, so there is a single source of truth that can't drift.
- Named "subscribers" to avoid confusion with the unrelated `blocklist_subscriptions`.

### Layering

This follows the existing `BanCelebrationGif` stack exactly. **Only the repository touches
`AppDbContext`.** Commands, services, handlers, and the fan-out worker all work through the
repository interface and domain model. None of them sees the DTO or the context.

| Layer | Type | Location |
|---|---|---|
| EF entity (DTO) | `BanCelebrationSubscriberDto`, `[Table("ban_celebration_subscribers")]`, `[Column(...)]` on every property | `TelegramGroupsAdmin.Data/Models/` (namespace `TelegramGroupsAdmin.Data.Models`) |
| DbContext | `DbSet<BanCelebrationSubscriberDto> BanCelebrationSubscribers`; composite key, FKs, cascade configured with the Fluent API | `AppDbContext` |
| Domain model | `BanCelebrationSubscriber` record | `TelegramGroupsAdmin.Telegram/Models/` |
| Mapping | `BanCelebrationSubscriberMappings` (`ToModel` / `ToDto`) | `TelegramGroupsAdmin.Telegram/Repositories/Mappings/` |
| Repository | `IBanCelebrationSubscriberRepository` / `BanCelebrationSubscriberRepository` | `TelegramGroupsAdmin.Telegram/Repositories/` |

Repository surface: upsert, delete for `(user, chat)`, delete all for a user,
`HasDeliverableSubscribersAsync(chatId)` (an `EXISTS` query joined to `telegram_users.bot_dm_enabled`;
the join happens in the repository, not the caller), `GetDeliverableSubscribersAsync(chatId)`, and
get/set/clear for the prompt columns. Every write path in this spec goes through these methods:
the command, the `/start` branch, `HandleUserLeftAsync`, `BanUserAsync`, the `BotChatService` block
branch, and the worker.

### Backup naming contract

`TableDiscoveryService` pairs each database table with a type by reflection. It only matches types
in namespace `TelegramGroupsAdmin.Data.Models`, whose name ends in `Dto`, and that carry
`[Table("<table>")]`. A table with no match is **dropped from backups with only a Debug log**. The
DTO above follows all three rules. Restore order is computed from foreign keys, so the new table is
restored after `telegram_users` and `managed_chats` with no extra code.

The existing guard, `TableDiscoveryServiceTests.EveryTableBackedDtoHasTableAttribute`, only catches
a missing attribute. This work adds a stronger guard, `EveryDbContextEntityIsDiscoveredForBackup`:
every `public` base table in the migrated schema must come back from `DiscoverTablesAsync`, except
an explicit allow-list with a reason for each entry. That list starts with `__EFMigrationsHistory`.
Quartz tables live in the `quartz` schema, so discovery never sees them. `cached_blocked_domains`
is excluded later, by `BackupService`, not by discovery. Any other public table found without a DTO
when the test is first written goes on the allow-list only if it is intentionally not backed up.
Otherwise it's an existing bug that gets fixed or reported. A new
table can then no longer silently fall out of backups, whether it's this one or a future one.

## Subscription removal

| Trigger | Scope | Where |
|---|---|---|
| `/dmcelebrations off` | that chat | `DmCelebrationsCommand` |
| User leaves or is kicked | that chat | `WelcomeService.HandleUserLeftAsync` |
| User is banned | all of the user's rows | `BotModerationService.BanUserAsync`, **before** the celebration step |
| User blocks the bot | all of the user's rows | new private-chat branch in `BotChatService.HandleBotMembershipUpdateAsync` (`MyChatMember` with new status `Kicked`); also calls `DisableBotDmAsync` |
| 403 during fan-out | all of the user's rows | fan-out worker (backstop for blocks that happened while the bot was offline) |

The ban-path delete must run before the fan-out is enqueued. Otherwise the worker can read
subscribers before the delete lands and DM the banned user a celebration of their own ban.
`BanUserAsync` is the only place ban side effects happen, so every ban path gets this cleanup
(see memory `moderation_ban_side_effects_single_place`).

On `MyChatMember` for a private chat with new status `Member` (unblock), change nothing. The
subscriptions were already deleted when the user blocked the bot.

## Celebration pipeline

`BanCelebrationService.SendBanCelebrationAsync`, rewritten:

1. **Guard.** Load the effective config. `postToChat = Enabled && trigger flag matches`.
   `hasSubscribers = HasDeliverableSubscribersAsync(chat.Id)`. If neither is true, return
   **before claiming** anything. Rotation claims are durable DB stamps, so a celebration nobody
   will see must not use up a slot.
2. **Build.** Claim the GIF and caption, get the ban count, apply explicit-username masking. This
   is unchanged. Masking runs once, and every recipient gets the masked text.
3. **Chat post** if `postToChat`: inline, `SendAndSaveAnimationAsync`, caches the `file_id` (unchanged).
4. **Banned-user DM** (existing `SendToBannedUser`): now sent as an **animation** through the shared
   dispatcher, reusing the cached `file_id`. This replaces the current video re-upload.
5. **Fan-out** if `hasSubscribers`:
   `IUserNotificationService.SendBanCelebrationAsync(chat, renderedCaption, gifId)`, which only
   enqueues.

The whole method stays inside the existing `SafeExecuteAsync` step in `BanUserAsync`, so it can
never fail a ban.

## Notification layer

### Split by audience, shared implementation

- **Rename `INotificationService` → `IAdminNotificationService`** (and `NotificationService` →
  `AdminNotificationService`). This is a mechanical rename across about 9 production files plus
  tests, done as its own first commit. No compatibility shims.
- **New `IUserNotificationService`** (Core) with `UserNotificationService` (web project,
  `Services/Notifications/`). v1 has one method, `SendBanCelebrationAsync`. This is also where
  System B's user DMs (`INotificationOrchestrator`: warnings, temp-ban notices, `/mystatus`) will
  move when that remnant is retired. That is the planned second caller.
- **No base interface.** No caller works through a common type, and an interface can't hold shared
  code. DRY comes from shared classes that both services call:
  - `NotificationPayload` + `NotificationPayloadBuilder` — one way to describe a message
  - `NotificationRenderer` — one way to render it
  - **`NotificationDmDispatcher`** (new, extracted from `NotificationService.DispatchEntityDmAsync`)
    — one way to send a payload as a DM
  - `IBotDmService` — one transport

| | Admin | User |
|---|---|---|
| Recipients | web users with chat access + unlinked chat admins | opt-in subscribers of the chat |
| Rules | preference matrix, dedup by Telegram ID | pacing, delete on block, `file_id` caching |
| Channels | DM, email, web push | DM only |
| Delivery | inline | background channel |

### Animation support

- `NotificationPayload` gains `Animation` (disk path + optional cached `FileId`).
  `NotificationPayloadBuilder.WithAnimation(path, fileId)`.
- `IBotDmService.SendDmWithAnimationEntitiesAsync` accepts either a `file_id` or a path, following
  the photo and video methods. `IBotMessageHandler.SendAnimationAsync` already exists.
- The dispatcher gains the animation branch and a `queueOnBlock` flag. The admin path passes `true`
  (today's behaviour). Celebrations pass `false`, so a returning user never gets a stale, text-only
  "X got banned!" replayed from `pending_notifications`.
- The dispatcher returns a result with `Sent`, `Blocked` (a distinct 403 signal) and
  `ReturnedFileId`.

### Fan-out channel and worker

Follows the existing `MediaRefetchQueueService` / `MediaRefetchWorkerService` pattern.

- `UserNotificationService.SendBanCelebrationAsync` writes a
  `BanCelebrationFanoutItem(ChatIdentity Chat, string Caption, long GifId)` to a singleton bounded
  `Channel<T>` (capacity 100, `DropOldest`). A minutes-old celebration isn't worth delivering
  during a raid. The queue is in memory, so a restart drops any fan-out in progress, which is
  acceptable for GIFs.
- `BanCelebrationFanoutWorker : BackgroundService` reads with `ReadAllAsync`, one item at a time,
  with a fresh DI scope per item:
  1. Load deliverable subscribers **now**, not at enqueue time, so users who unsubscribed, left or
     were banned in between are excluded.
  2. Reload the GIF row for the current `file_id`. The chat post may have cached one after enqueue.
  3. Build the payload once: subject = chat name, text = caption, `.WithAnimation(path, fileId)`.
  4. For each subscriber, dispatch with `queueOnBlock: false`:
     - `ReturnedFileId` and no cached id yet → `UpdateFileIdAsync`, rebuild the payload with the id
     - `Blocked` → delete all of that user's subscriptions
     - ~50ms between sends (under Telegram's ~30 msg/s global limit)
  5. Invalid cached `file_id` → clear it and fall back to upload (same as the chat path).
- One reader, one celebration at a time. The first send uploads and the rest reuse, with no lock
  and no duplicate uploads.

### Project placement

| Piece | Project |
|---|---|
| `IAdminNotificationService`, `IUserNotificationService` | Core |
| Both implementations, dispatcher, payload/builder, channel, worker | Web (`Services/Notifications/`) — `NotificationPayload` is `internal` there |
| Subscriber DTO + DbSet | Data |
| Subscriber domain model, mappings, repository | Telegram |
| `DmCelebrationsCommand`, `StartCommand` branch, `BotChatService` block branch | Telegram |
| `SendDmWithAnimationEntitiesAsync` | Telegram (`BotDmService`) |

## Error handling

- Celebration failures never fail a ban (existing `SafeExecuteAsync`). Enqueueing only writes to
  the channel.
- The worker catches errors per subscriber and per item. One bad send or item never stops the loop.
  It only stops on host shutdown.
- A missing GIF file with no cached `file_id` skips the item with a warning (same as the chat path).
- Prompt cleanup treats "job not found" and "message not found" as success.

## Observability

`PipelineMetrics` gains:

- `ban_celebration_dm` counter, tag `outcome`: `sent | blocked | failed | dropped` (`dropped` via
  the channel's `DropOldest` callback)
- `ban_celebration_subscription` counter, tag `action`:
  `subscribe | unsubscribe | left | banned | blocked`

Logs use the `ToLogInfo` / `ToLogDebug` identity helpers. There is one Information-level summary
per fan-out item ("sent X, blocked Y to {chat}"), not one line per DM.

## Backup and restore

The new table is backed up and restored with no backup-code change, **provided the DTO follows the
naming contract** in Data model → Backup naming contract. That contract is enforced by the new
discovery guard test. Restored prompt columns may point at messages and jobs that no longer exist,
and cleanup already treats that as success.

## Testing

All integration tests follow `.claude/rules/integration-test-data.md`.

| Layer | Coverage |
|---|---|
| Unit (NSubstitute) | Guard matrix: chat-only, subscribers-only, both, neither (asserts **no claim**). Dispatcher animation branch, `file_id` vs upload. `queueOnBlock: false` writes nothing to pending notifications. Worker saves the first returned `file_id` and reuses it for later recipients. Blocked result → user's subscriptions deleted. `DmCelebrationsCommand` argument parsing, including the DM refusal. Prompt cleanup cancels the job before deleting the message. |
| Integration (canonical DB) | Repository: upsert idempotency, delete for chat, delete all for user, deliverable query returns only `bot_dm_enabled` users. Migration: composite PK and cascade FKs. Ban path: a banned subscriber's rows are gone before the fan-out is enqueued, through the real `BanUserAsync`. `/start dmcel_{chat}` nulls both prompt columns. Backup: `DiscoverTablesAsync` maps `ban_celebration_subscribers` → `BanCelebrationSubscriberDto`, plus the new schema-wide guard `EveryDbContextEntityIsDiscoveredForBackup`. |
| Existing tests | Mechanical updates for the `IAdminNotificationService` rename. Banned-user DM tests change from video to animation. |

Tests where subscribing is the assertion subject write the row through the SUT, which the rule
allows. Tests that need an existing subscription as a precondition (ban cleanup, leave cleanup,
fan-out targeting, `/start` confirmation) need a canonical row.

### Canonical anchors

All of these are active, non-banned, non-bot users that no test or doc references (checked with
`grep` across `TelegramGroupsAdmin.*Tests`, `docs`, and the integration-tests `CLAUDE.md`). Each
has real messages in the chats listed, so the subscriptions look plausible. No existing
`telegram_users` or `managed_chats` row is edited.

| Anchor | Id | Username | `bot_dm_enabled` | Posted in | Role |
|---|---|---|---|---|---|
| Deliverable subscriber | `9183753414221` | `magnetismvoucher` | true | Workshop Alumni | included by the deliverable query; banned in the ban-cleanup test |
| Undeliverable subscriber | `9011393194616` | `thudupper` | false | Workshop Alumni | subscribed but excluded by the deliverable query |
| Two-chat subscriber | `9689750659830` | `deepnessunmapped` | false | Workshop Alumni, Poultry Community | leave/kick removes only one chat's row |
| Unsubscribed member | `9306234060091` | `chummyrepair` | true | Main Community | subscribe-path assertion subject (SUT upsert) |

Chats: **Workshop Alumni** `-100059667856554`, **Poultry Community** `-100017608907459`,
**Main Community** `-100026957614982`.

**Canonical-rule exception (approved by owner 2026-09-25):** `ban_celebration_subscribers` is a new table, so there is
no existing row to flag-edit, and the rule forbids adding rows to canonical. Add
a new `36_ban_celebration_subscribers.sql` with exactly four rows, one per subscription in the
table above (`magnetismvoucher`/Workshop Alumni, `thudupper`/Workshop Alumni, `deepnessunmapped`/
Workshop Alumni and Poultry Community). Prompt columns are all NULL, except that one row carries a
stale `prompt_message_id`/`prompt_delete_job_id` pair for the "cleanup after timeout tolerates
missing job/message" test. Pin the rows in `GoldenDatasetConstants.BanCelebrationSubscribers` and
add a Part 2 recipe marked "(canonical addition 2026-09-25)". Check `LoadCanonicalAsyncTests` for
exact table-count assertions.

## Out of scope

- Web UI for subscribers
- DM-side unsubscribe (blocking the bot covers it)
- Inline-button subscription management
- Throttling or collapsing celebrations during raids
- Retiring System B (`INotificationOrchestrator`). `IUserNotificationService` is its future home,
  but the move is separate work.

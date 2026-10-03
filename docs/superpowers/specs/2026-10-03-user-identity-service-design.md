# User Identity Service

**Date:** 2026-10-03
**Issue:** #552 (part 1 of 2; part 2 is `2026-10-03-flagged-name-masking-design.md`)
**Scope:** One service that every entry point goes through to record what it observed about a
Telegram user and to obtain an immutable `UserIdentity` carrying both the real name (for logs
and the UI) and a bot-safe name (for anything the bot writes to Telegram). Part 1 changes no
visible behaviour except explicit-name masking: it now applies to every bot-written mention, not
only ban-celebration captions, with fixed wording and one "Mask flagged names" setting (global
default, per-chat override).
**PR target:** `develop`.

---

## Context

A banned spammer's display name was substituted into a ban-celebration caption, and Telegram
clients auto-linked the domain in it (#552). Fixing only the caption leaves the same name in
every other message the bot writes: welcome and exam prompts, `/report`, `/warn`, DM fallbacks,
admin notification DMs. The deeper problem is that user names have no single owner:

- `UserIdentity` (`Core/Models/UserIdentity.cs`) is a public record built at about 120 sites:
  ~83 from the Telegram update (`message.From`, `CallbackQuery.From`, `ChatMember.User`), ~20 from
  `telegram_users` rows and views, 13 from an id only, 5 synthesised.
- Most flows rebuild it from `message.From` several times instead of passing one down.
- The message pipeline updates the `telegram_users` row late (`MessageProcessingService.cs:717`),
  after commands, the admin-mention handler and the ban check have run.
- `GetOrCreateAsync` (`ON CONFLICT DO NOTHING`) never refreshes names, so joiners keep stale
  names until they post.
- `UpsertAsync` copies every column from its input, so callers that pass nulls overwrite fields
  they don't own (the pipeline passes null photo fields on every message).
- The profile scan reads live names over the User API and throws them away.
- Quartz job payloads persist a `UserIdentity` snapshot as JSON.

## Goals

1. One way to obtain a `UserIdentity`: the identity service. The compiler enforces it.
2. Every source of name information (Bot API updates, `getChatMember`, User API scans) records
   what it saw through one method, which also owns what a rename means.
3. The identity carries a name verdict, so `Mention` can never write a flagged name.
4. Keep the platform-neutral concepts in core types so a later Matrix or Discord adapter can
   reuse them; keep Telegram specifics at the edges. Build no multi-platform abstractions now.

## Non-goals

- Producing new verdicts (promotional flag, name-only fallback, eval set): part 2.
- `Actor` names (admins and executors). Out of scope.
- Routing every user write (bans, trust, photos) through the service. Only name observation
  goes through it.
- Refreshing names of users who never interact again (a lurker who renames). A later option is
  calling `getChatMember` right before a web-UI ban posts a celebration; not built here.
- Chat titles (`{chatname}`, channel senders). They are chats, not users.

## Design

### `UserIdentity`

```
public sealed record UserIdentity
    long Id, string? FirstName, string? LastName, string? Username   // Telegram-shaped, as today
    NameVerdict Verdict                                               // new
    string DisplayName      // real name: logs, web UI (unchanged)
    string BotDisplayName(NameMasking masking)   // what bot-written text shows
```

```
public enum NameMasking { Off, On }   // Core; the effective "Mask flagged names" value
```

- `NameVerdict` (Core): `Unscanned`, `Clean`, `Promotional`, `Explicit`. Platform-neutral.
- The identity is global: Telegram names and verdicts are per account, not per chat, so one
  resolved identity is reused across chats and flows. Only the masking setting varies by chat, and
  it is applied where the text is written (see `Mention` and Masking config).
- `BotDisplayName(masking)` maps the verdict:
  - masking on: `Explicit` → `[name removed: explicit]`, `Promotional` → `[name removed: spam]`,
    `Unscanned` / `Clean` → `DisplayName`;
  - masking off: `DisplayName` for every verdict.
  Nothing produces `Promotional` until part 2.
- The serialized JSON shape stays compatible: `Verdict` is a new optional member that defaults to
  `Unscanned`, so Quartz payloads queued before the change still deserialize.
- After the migration (stage 4) there is no public constructor and no `From(...)` factory. The
  only other ways in are `UserIdentity.ForPreview(...)` (settings-page previews) and
  `UserIdentity.ForTest(...)` (tests), both with `Verdict = Unscanned`.

### `IUserIdentityService` (Telegram adapter)

```
Task<UserIdentity> ObserveAsync(ObservedUser observed, CancellationToken ct)
Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct)
Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
```

- `ObservedUser` carries the names, `IsBot`, the source (`BotUpdate`, `ChatMember`, `UserApiScan`)
  and `ObservedAt` (message `date` or `edit_date`, or the time a scan fetched the names).
- The service is stateless; lifetime follows the existing repository pattern (`IDbContextFactory`).
- Verdict source: the latest `profile_scan_results` row for the user id, mapping
  `ai_explicit_display_text = true` to `Explicit`, otherwise `Clean`; no row gives `Unscanned`.
  Bots and system accounts (777000, anonymous admin, channel sender) are `Unscanned` in part 1.
- `ResolveAsync` for an id with no row returns an id-only identity, `Unscanned`.
- `ResolveManyAsync` uses one query for names and one for latest verdicts.

### `ObserveAsync` and renames

`ObserveAsync` is the single owner of "we saw this user's names":

1. `ITelegramUserRepository.GetOrUpdateAsync(observed)` replaces `GetOrCreateAsync`:
   - `INSERT … ON CONFLICT DO NOTHING`, then a conditional
     `UPDATE … SET first_name, last_name, username, names_observed_at
      FROM (SELECT … FOR UPDATE) old
      WHERE names IS DISTINCT FROM the observed names AND @observedAt >= names_observed_at
      RETURNING old names`.
   - It touches only the name columns and `names_observed_at` (new column). Photo, trust, ban and
     activity columns are never written here.
   - The returned old names tell the caller a rename happened. Only the statement that changed the
     row sees it; an observation older than the stored one is ignored.
2. If renamed, in the same transaction: the `username_history` row and the `ProfileChange`
   audit row (moved here from `MessageProcessingService.cs:680-700`).
3. After the transaction, for an untrusted non-bot user, request a rescan through
   `IProfileScanGate` (`ProfileScanTrigger.ProfileChange`). The rescan is queued by default; the
   caller can ask for it inline when the moderation decision for the current message needs the new
   verdict (new-message path only). A rename-triggered rescan bypasses the scan service's
   freshness window.
4. Return the resolved identity.

A failure to write never blocks moderation: the service logs it and returns an identity built from
the observation with `Verdict = Unscanned`.

`UpsertAsync` loses the name columns from its `DO UPDATE` list. Its remaining callers are the bot's
own row and `BotProtectionService`; if they fit `GetOrUpdateAsync`, `UpsertAsync` is removed.

### Profile scan changes

- `ProfileScanService` calls `ObserveAsync` with the live User API names it fetches, so a scan can
  record a rename even without a message.
- In-flight single-flight inside the singleton `ProfileScanService`
  (`ConcurrentDictionary<long, Lazy<Task<ProfileScanResult>>>`, entry removed in `finally`):
  concurrent requests for one user share one scan. Today's 60s dedup is check-then-act on
  `profile_scanned_at`, which is written only after the scan finishes, so concurrent scans both
  pass it.
- The scan returns the re-resolved identity so the calling flow continues with the new verdict.

### Concurrency

Bot updates are processed one at a time: `ReceiveAsync` (`TelegramBotPollingHost.cs:187`) awaits
each handler. The concurrent writers are off the polling loop: profile scans (including
`ProfileRescanJob`), Quartz jobs, and the web UI. The design uses no in-process lock for names:
the conditional update's row lock serializes writers and the `names_observed_at` guard makes the
newest observation win. A "first call wins" lock would record a stale scan overwriting a newer
message name as a rename.

### Entry points

Each entry point calls `ObserveAsync` first and passes the returned identity down.

| Entry point | Change |
|---|---|
| New message (`MessageProcessingService`) | `ObserveAsync(message.From)` first, before commands; the late upsert at :717 and the diff block at :673-715 go away. One identity is passed to commands, detection and moderation. |
| Edited message | `ObserveAsync` with `edit_date`. |
| Callback query | `ObserveAsync(CallbackQuery.From)`. |
| Chat-member update | `ObserveAsync(ChatMember.User)`; the join scan then runs on current names. |
| Commands | The caller comes from the pipeline. Reply targets (`/warn`, `/ban`, …) use `ObserveAsync(ReplyToMessage.From)`. |
| Quartz jobs | Payloads keep their snapshot for compatibility; jobs call `ResolveAsync(payload.User.Id)` before writing anything a user sees. |
| Web UI | `ResolveAsync` / `ResolveManyAsync` by id. |
| `getChatMember` results (admin refresh) | `ObserveAsync`. |

### `Mention`

- A builder is created for a masking policy: `TelegramMessageBuilder.For(NameMasking masking)`.
  `Mention(UserIdentity)` renders `BotDisplayName(masking)` and still emits a `TextMention` with the
  user id, so it stays clickable and still pings.
- Callers get the policy from `IConfigService.GetNameMaskingAsync(long? chatId)`: the chat's
  effective value for group posts, the global value when `chatId` is null (admin notification DMs,
  `/start` DMs).
- Stage 4 removes the parameterless builder constructor, so every builder states its policy and a
  forgotten one is a compile error (about 40 construction sites).
- Ban-celebration captions (plain text) substitute `BotDisplayName(masking)` for `{username}`;
  their own explicit-flag lookup in `BanCelebrationService` is removed.

### Masking config

- `ProfileScanConfig.MaskExplicitUsername` is renamed in place to `MaskFlaggedNames` ("Mask flagged
  names"), default on. Like every config it has a global value (`chat_id = 0`) and per-chat
  overrides merged by `GetEffectiveWelcomeAsync`.
- Group posts use the chat's effective value; messages with no chat use the global value.
- `ExplicitUsernameRedactionText` and `DefaultExplicitUsernameRedactionText` are removed; the
  wording is fixed so both labels share one format.
- A data migration renames the JSONB key in every `configs` row so stored values carry over.
- The setting sits where the explicit toggle is today, with helper text naming both labels.
- Masking still requires a verdict, so a chat with profile scanning off gets no masking (admin
  choice).

### Platform boundary

Core (platform-neutral): `NameVerdict`, `NameMasking`, `DisplayName` / `BotDisplayName`, redaction
wording.
Telegram adapter: the name shape (first, last, username), `long` ids, `ObservedUser` sources,
`TextMention` rendering, `IUserIdentityService` implementation.

Telegram accounts have one global name, so the identity is global. Discord has per-server
nicknames; a Discord adapter would need the name (and so the verdict) per server. That is left to
that adapter and does not shape this design.

## Landing

Four stages, each a green commit series:

1. Add `NameVerdict`, `NameMasking`, `BotDisplayName`, `IUserIdentityService`,
   `GetOrUpdateAsync`, `names_observed_at` (migration), the `MaskFlaggedNames` rename and its config
   migration, `GetNameMaskingAsync`, and `TelegramMessageBuilder.For(masking)`. `Mention` renders
   `BotDisplayName(masking)`. Nothing enforced yet.
2. Message pipeline: `ObserveAsync` first, rename handling moved into the service, one identity
   passed down. Then edited messages, callbacks, chat-member updates. Scan single-flight and the
   rename bypass.
3. Migrate remaining sites area by area: welcome/exam, commands, moderation, notifications, jobs,
   web UI.
4. Remove the public `UserIdentity` constructor, the `From(...)` factories and the parameterless
   `TelegramMessageBuilder` constructor; add `ForPreview` / `ForTest`.
   Missed sites become compile errors.

## Testing

Unit:
- `BotDisplayName` per verdict with masking on and off; `Unscanned`/`Clean` show the real name;
  `Explicit` shows `[name removed: explicit]`.
- `Mention` renders `BotDisplayName(masking)` and keeps the `TextMention` user id.
- Service verdict mapping: the latest scan row wins; no row gives `Unscanned`; bots and system
  accounts give `Unscanned`.
- `ProfileScanService` single-flight: two concurrent calls gated by a `TaskCompletionSource` fake
  produce one scan and the same result.
- `ObserveAsync` swallows a repository failure and returns an `Unscanned` identity.
- `GetNameMaskingAsync` (substituted config): a chat override off gives `Off`; no override falls back
  to the global value; `null` chat gives the global value.
- One `Explicit` identity rendered by a builder with `Off` shows the real name and by a builder with
  `On` shows `[name removed: explicit]`.

Integration (real Postgres, canonical data; anchors in the Canonical anchors section):
- `GetOrUpdateAsync` rename: the row changes, `username_history` and the `ProfileChange` audit row
  are written once, in one transaction.
- No-op observation: same names, nothing written.
- Ordering: an observation older than `names_observed_at` with different names changes nothing.
- Race: a raw connection holds `SELECT … FOR UPDATE` on the user row; two `ObserveAsync` calls with
  the same new name start, `pg_stat_activity` shows both waiting on a lock (polled, no sleeps);
  after commit exactly one reports a rename, with one history row and one audit row.
- Photo fields: a name update leaves `user_photo_path` and `photo_hash` unchanged.
- Quartz compatibility: a payload JSON in today's shape (no `Verdict`) deserializes with `Unscanned`.
- Config migration: stored `MaskExplicitUsername` values (global and per-chat) are read back as
  `MaskFlaggedNames`.

- Pipeline: a renamed untrusted user's message updates the row before command routing and requests
  a rescan; a renamed trusted user's row is updated with no rescan.

### Canonical anchors

No canonical rows are edited. Each test clones the template, so a row written as the assertion
subject does not leak between tests.

| Test | Anchor | Shape (already in canonical) |
|---|---|---|
| Verdict: latest scan wins, explicit flag → `Explicit` | 9220500615182 @bagging_armado | Two scans (530 older, 534 newer); 534 is the only row with `ai_explicit_display_text = true`. Read-only: `ProfileScanResultsRepositoryTests` already pins it, so its scan rows must not change. |
| Verdict: no scan rows → `Unscanned` | 9063342700386 @Juvenileii | Not trusted, not a bot, no scan rows, `profile_scanned_at` NULL. |
| Verdict: bot → `Unscanned` | 9742468412405 @doilyemcee | The canonical bot; read-only. |
| Rename (untrusted): row, history, audit, rescan request | 9263051408340 @pastramiherbs | Not trusted, active, no `username_history` rows. |
| Rename (trusted): row updated, no rescan | 9006671634371 @starlightskinless | Trusted, not an admin. |
| No-op and ordering | 9263051408340 @pastramiherbs | As above; observations at and before `names_observed_at`. |
| Race (row lock) | 9680301255238 @violingentleman | Not trusted, active, no history rows. |
| Photo fields untouched by a name update | 9264989724828 @raceoutnumber | Not trusted; `user_photo_path` and `photo_hash` both set. |

Each test reads its anchor back first and asserts the shape (trust, bot flag, scan rows, photo
fields) so a later canonical change fails loudly. New constants go in
`GoldenDatasetConstants.IdentityService`, with a recipe in `IntegrationTests/CLAUDE.md`.

Two tests do not use canonical rows:
- Quartz compatibility: canonical does not seed `qrtz_*` tables, so the test deserializes a payload
  JSON literal in today's shape. That is input to the code under test, not seeded data.
- Config migration: canonical configs never store `MaskExplicitUsername`, so this is a migration
  fixture on the empty template (the rule's exception for migration tests): a config row with the
  old key is the migration's input, and the test asserts the renamed key.

## Follow-ups (not in this work)

- `getChatMember` refresh before a web-UI ban celebration.
- `AdminNotificationService` builds a `UserIdentity` from an `Actor` and loses the name parts.
- Channel titles as message senders.

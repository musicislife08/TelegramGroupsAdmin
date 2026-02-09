# Raw ID Log Call Audit

## Next Steps (WIP - pick up here)

The current plan file (`Fixes 1-8`) covers only the cases where identity objects are already in scope.
The user reviewed this audit and gave critical feedback: **most SKIPs are wrong**.

**The rule**: Every log call at **Info level and above** (Info, Warning, Error, Critical) that logs a raw
numeric ID needs an identity object threaded through from the caller. Debug-level logs with raw IDs are
acceptable (machine-readable diagnostics).

**What needs to happen before finalizing the plan:**

1. **Trace callers** for each service/method that only has raw `long` params to find where in the call
   chain an identity object (UserIdentity, ChatIdentity, SDK User/Chat) already exists
2. **Change signatures** to thread identity objects down through the call chain
3. **Update all Info+ logs** to use `.DisplayName` / `.ToLogInfo()` / `.ToLogDebug()`

**Key areas needing caller tracing:**

- **ExamFlowService** — User annotated lines 141, 158, 194, 250, 331 (chat half). Parents have
  SDK User + Chat objects that aren't being passed through. Line 370 also needs tracing.
- **BanCelebrationService** — Takes `long chatId, long bannedUserId`. Callers likely have identity objects.
- **ContentDetection checks** — All use `ContentCheckRequestV2.UserId/ChatId` (long). Need to check if
  `ContentCheckRequestV2` should carry identity objects. ~30 Info+ logs across 8 check files.
- **BotChatService, BotUserService, BotMediaService** — Multiple Warning-level logs with raw chatId/userId.
  Trace callers to find identity context.
- **MessageContextAdapter, PromptBuilderService** — Error-level logs with raw chatId.
- **Repositories (ChatAdminsRepository, ManagedChatsRepository, ContentDetectionConfigRepository)** —
  All Info+ logs with raw chatId. These are lowest layer, may need identity threaded from callers.

**User-specific annotations on ExamFlowService:**
- Line 141: "requires signature change to pass identity through it. somewhere above it the parents generate one"
- Line 158: "same as above"
- Line 194: "need to fix group as well. it should say chat not group and use a chat identity object"
- Line 231: "this one is probably fine. should be a debug line for expected"
- Line 250: "again. find where parent has identity and thread through"
- Line 331: "also needs chat. only half done"

**User's guiding principle:** "the entire point of this branch is to thread identity objects through
those places. tracing paths with mcp tools to find callers where identity objects exist and changing
signatures to thread through down to the lowest class that contains info logs needing that info."

---

## Legend

- **FIX** = Identity object in scope, should use `.DisplayName` / `.ToLogInfo()` / `.ToLogDebug()`
- **NEEDS TRACING** = No identity in scope yet, but caller likely has one — requires signature change
- **SKIP** = Debug-level only, OR web user ID strings (not Telegram identity), OR entity genuinely doesn't exist
- **DONE** = Already in current plan (Fixes 1-8)

---

## TelegramGroupsAdmin.Telegram

### Services/CasCheckService.cs (DONE - Fix 1)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 49 | WARN | `"CAS check failed for user {UserId}, failing open", userId` | DONE |
| 77 | DEBUG | `"CAS check for user {UserId}: Calling {ApiUrl}", userId, apiUrl` | DONE |
| 105 | INFO | `"CAS check: User {UserId} is BANNED (reason: {Reason})", userId, ...` | DONE |
| 110 | DEBUG | `"CAS check: User {UserId} not found in database", userId` | DONE |

### Services/Bot/Handlers/BotBanHandler.cs (DONE - Fix 5, user part only)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 69 | WARN | `"Failed to ban user {UserId} in chat {ChatId}", user.Id, chatId` | DONE (user) / **NEEDS TRACING** (chat) |
| 158 | WARN | `"Failed to temp ban user {UserId} in chat {ChatId}", user.Id, chatId` | DONE (user) / **NEEDS TRACING** (chat) |
| 223 | WARN | `"Failed to unban user {UserId} in chat {ChatId}", user.Id, chatId` | DONE (user) / **NEEDS TRACING** (chat) |

### Services/Bot/Handlers/BotRestrictHandler.cs (DONE - Fix 6, user part only)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 97 | WARN | `"Failed to restrict user {UserId} in chat {ChatId}", user.Id, targetChatId` | DONE (user) / **NEEDS TRACING** (chat) |

### Services/ExamFlowService.cs — **MAJOR: needs identity threading through signatures**

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 141 | WARN | `"Exam config not valid for chat {ChatId}", groupChatId` | `groupChatId` is `long` param | **NEEDS TRACING** |
| 158 | INFO | `"Created exam session {SessionId} for user {UserId} (group: {GroupId})"` | Need to check args | **NEEDS TRACING** |
| 194 | ERROR | `"Failed to start exam in DM for user {UserId} from group {GroupId}", user.Id, groupChatId` | `user` is SDK `User`, `groupChatId` is `long` | **FIX** (user) / **NEEDS TRACING** (chat) |
| 231 | WARN | `"User {UserId} tried to answer session {SessionId} belonging to {OwnerId}", user.Id, sessionId, session.UserId` | `user` is SDK `User`, `session.UserId` raw | Should be DEBUG level |
| 250 | WARN | `"No exam config for chat {ChatId}", session.ChatId` | `session.ChatId` is `long` | **NEEDS TRACING** |
| 331 | DEBUG | `"No active exam session for user {UserId} in chat {ChatId}", user.Id, chatId` | `user` is SDK `User` | DONE (user) / **NEEDS TRACING** (chat) |
| 370 | INFO | `"Cancelled exam session for user {UserId} in chat {ChatId}", userId, chatId` | both raw `long` params | **NEEDS TRACING** |
| 448 | INFO | `"Open-ended evaluation for user {UserId}: ...", user.Id, ...` | `user` is SDK `User` | DONE |
| 454 | WARN | `"AI unavailable for exam evaluation, sending user {UserId} to review", user.Id` | `user` is SDK `User` | DONE |

### Services/BackgroundServices/MessageProcessingService.cs (DONE - Fix 3)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 116 | INFO | `"Processed open-ended exam answer for user {UserId}:...", message.From.Id, ...` | DONE |
| 146 | ERROR | `"Error processing open-ended exam answer from user {UserId}", message.From?.Id` | DONE |
| 609 | WARN | `LogDisplayName.UserDebug(message.From.FirstName, ..., message.From.Id)` | **CLEANUP** — simplify to `message.From.ToLogDebug()` |

### Services/BanCelebrationService.cs — **NEEDS TRACING: callers have identity objects**

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 54 | DEBUG | `"Ban celebration disabled for chat {ChatId}", chatId` | `chatId` is `long` param | SKIP (debug) |
| 61 | DEBUG | `"Ban celebration skipped for auto-ban in chat {ChatId}...", chatId` | `chatId` is `long` param | SKIP (debug) |
| 67 | DEBUG | `"Ban celebration skipped for manual ban in chat {ChatId}...", chatId` | `chatId` is `long` param | SKIP (debug) |
| 122 | WARN | `"Failed to send ban celebration for chat {ChatId}, user {UserId}", chatId, bannedUserId` | both raw `long` params | **NEEDS TRACING** |
| 257 | WARN | `"Failed to send GIF to chat {ChatId}", chatId` | `chatId` is `long` param | **NEEDS TRACING** |
| 277 | DEBUG | `"Skipping DM to banned user: welcome system not enabled for chat {ChatId}", chatId` | `chatId` is `long` | SKIP (debug) |
| 284 | DEBUG | `"Skipping DM to banned user: chat {ChatId} uses chat-based welcome mode", chatId` | `chatId` is `long` | SKIP (debug) |
| 327 | INFO | `"Ban celebration DM sent to banned user {UserId}", bannedUserId` | `bannedUserId` is `long` param | **NEEDS TRACING** |
| 331 | DEBUG | `"Ban celebration DM failed for user {UserId}:...", bannedUserId, ...` | `bannedUserId` is `long` | SKIP (debug) |
| 338 | DEBUG | `"Failed to send ban celebration DM to user {UserId}", bannedUserId` | `bannedUserId` is `long` | SKIP (debug) |

### Services/Bot/BotUserService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 49 | WARN | `"Failed to check admin status for user {UserId} in chat {ChatId}", userId, chatId` | both raw `long` params | **NEEDS TRACING** |

### Services/Bot/BotChatService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 45 | DEBUG | `"Using cached invite link for chat {ChatId}", chatId` | `chatId` is `long` param | SKIP (debug) |
| 66 | DEBUG | `"Refreshing invite link from Telegram for chat {ChatId}", chatId` | `chatId` is `long` | SKIP (debug) |
| 94 | WARN | `"Health check failed for chat {ChatId}", chatId` | `chatId` is `long` | **NEEDS TRACING** |
| 569 | DEBUG | `"Cached public invite link for chat {ChatId}", chatId` | `chatId` is `long` | SKIP (debug) |
| 582 | DEBUG | `"Using existing cached invite link for private chat {ChatId}", chatId` | `chatId` is `long` | SKIP (debug) |

### Services/Bot/BotMediaService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 206 | WARN | `"Failed to fetch chat icon for chat {ChatId}", chatId` | `chatId` is `long` param | **NEEDS TRACING** |

### Services/Bot/BotDmService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 473 | DEBUG | `"Edited DM text message {MessageId} in chat {ChatId}", messageId, dmChatId` | `dmChatId` is `long` (private chat) | SKIP (debug) |
| 492 | DEBUG | `"Edited DM caption for message {MessageId} in chat {ChatId}", messageId, dmChatId` | same | SKIP (debug) |
| 505 | DEBUG | `"Deleted DM message {MessageId} in chat {ChatId}", messageId, dmChatId` | same | SKIP (debug) |

### Services/Media/MediaRefetchQueueService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 74 | DEBUG | `"User photo already queued: {UserId}", userId` | `userId` is `long` param | SKIP (debug) |
| 79 | DEBUG | `"Enqueued user photo refetch: {UserId}", userId` | `userId` is `long` param | SKIP (debug) |

### Services/ChatHealthRefreshOrchestrator.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 59 | INFO | `"Updated chat name: {OldName} -> {NewName} ({ChatId})"` | `ChatId` included as context alongside name | SKIP (name already shown) |

### Services/BotCommands/CommandRouter.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 193 | DEBUG | `"User {TelegramId} is Telegram {Role} in chat {ChatId}, granting {Level}...", telegramId, ...` | raw `long` params only | SKIP (debug) |
| 199 | DEBUG | `"User {TelegramId} has no permissions in chat {ChatId}", telegramId, chatId` | raw `long` params only | SKIP (debug) |

### Repositories/ChatAdminsRepository.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 120 | DEBUG | `"Upserted admin: chat={ChatId}, user={TelegramId},...", chatId, telegramId,...` | raw `long` params (repo layer) | SKIP (debug) |
| 146 | INFO | `"Deactivated admin: chat={ChatId}, user={TelegramId}", chatId, telegramId` | same | **NEEDS TRACING** |
| 166 | INFO | `"Deleted {Count} admin records for chat {ChatId}", rowsAffected, chatId` | same | **NEEDS TRACING** |

### Repositories/ManagedChatsRepository.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 139 | INFO | `"Marked chat {ChatId} as inactive", chatId` | `chatId` is `long` param (repo) | **NEEDS TRACING** |

### Handlers/MessageEditProcessor.cs (DONE - Fix 4)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 108 | INFO | `"Recorded edit for message {MessageId} in chat {ChatId}", editedMessage.MessageId, editedMessage.Chat.Id` | DONE |

---

## TelegramGroupsAdmin.ContentDetection

### ContentCheckRequestV2 — **NEEDS TRACING: should carry identity objects?**

All content detection checks use `req.UserId` (long) and `req.ChatId` (long) from `ContentCheckRequestV2`.
The caller (ContentDetectionOrchestrator) likely has identity objects from the SDK Message.
Adding `UserIdentity` and `ChatIdentity` to the request would fix ~30 logs across 8 check files at once.

### Checks/AIContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 125 | WARN | `"AI API returned null result for user {UserId}, abstaining", req.UserId` | **NEEDS TRACING** |
| 137 | WARN | `"AI check for user {UserId}: Request timed out, abstaining", req.UserId` | **NEEDS TRACING** |
| 149 | ERROR | `"Error in AI V2 check for user {UserId}, abstaining", req.UserId` | **NEEDS TRACING** |

### Checks/BayesContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 133 | ERROR | `"Error in BayesSpamCheckV2 for user {UserId}", req.UserId` | **NEEDS TRACING** |

### Checks/ImageContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 260 | ERROR | `"Error in ImageSpamCheckV2 for user {UserId}, abstaining", req.UserId` | **NEEDS TRACING** |
| 331 | DEBUG | `"ImageSpam V2 check for user {UserId}: Calling AI Vision API", req.UserId` | SKIP (debug) |
| 350 | WARN | `"Empty response from AI Vision for user {UserId}, abstaining", req.UserId` | **NEEDS TRACING** |
| 366 | ERROR | `"AI Vision API error for user {UserId}, abstaining", req.UserId` | **NEEDS TRACING** |
| 443 | WARN | `"Failed to deserialize AI Vision response for user {UserId}:...", req.UserId,...` | **NEEDS TRACING** |
| 456 | DEBUG | `"AI Vision V2 analysis for user {UserId}: Spam=...", req.UserId,...` | SKIP (debug) |
| 504 | ERROR | `"Error parsing AI Vision response for user {UserId}:...", req.UserId,...` | **NEEDS TRACING** |

### Checks/VideoContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 164 | ERROR | `"Error in VideoSpamCheckV2 for user {UserId}", req.UserId` | **NEEDS TRACING** |
| 454 | DEBUG | `"VideoSpam Layer 3: Calling AI Vision API for user {UserId}", req.UserId` | SKIP (debug) |
| 471 | WARN | `"Empty response from AI Vision for user {UserId}", req.UserId` | **NEEDS TRACING** |
| 559 | WARN | `"Failed to deserialize AI Vision response for user {UserId}:...", req.UserId,...` | **NEEDS TRACING** |
| 572 | DEBUG | `"AI Vision analysis for user {UserId}: Spam=...", req.UserId,...` | SKIP (debug) |
| 608 | ERROR | `"Error parsing AI Vision response for user {UserId}:...", req.UserId,...` | **NEEDS TRACING** |

### Checks/StopWordsContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 126 | ERROR | `"Error in StopWordsSpamCheckV2 for user {UserId}", req.UserId` | **NEEDS TRACING** |

### Checks/ThreatIntelContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 59 | DEBUG | `"ThreatIntel check for user {UserId}: VirusTotal flagged {Url}", req.UserId,...` | SKIP (debug) |
| 74 | DEBUG | `"ThreatIntel check for user {UserId}: No threats found...", req.UserId,...` | SKIP (debug) |
| 88 | ERROR | `"ThreatIntel check failed for user {UserId}", req.UserId` | **NEEDS TRACING** |

### Checks/UrlBlocklistContentCheckV2.cs

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 71 | DEBUG | `"URL filter check for user {UserId}: Checking ... in chat {ChatId}", req.UserId,...` | SKIP (debug) |
| 103 | INFO | `"URL filter match for user {UserId}: Domain {Domain} on soft block list...", req.UserId,...` | **NEEDS TRACING** |
| 117 | DEBUG | `"URL filter check for user {UserId}: No soft blocks found...", req.UserId,...` | SKIP (debug) |
| 131 | ERROR | `"URL filter check failed for user {UserId}", req.UserId` | **NEEDS TRACING** |

### Services/MessageContextProvider.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 63 | ERROR | `"Failed to retrieve message history for chat {ChatId}", chatId` | `chatId` is `long` param | **NEEDS TRACING** |

### Services/UrlPreFilterService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 40 | DEBUG | `"Checking {Count} URLs for hard blocks in chat {ChatId}", urls.Count, chatId` | `chatId` is `long` param | SKIP (debug) |
| 76 | WARN | `"Hard block triggered for domain {Domain} in chat {ChatId}:...", domain, chatId,...` | same | **NEEDS TRACING** |

### Services/Blocklists/BlocklistSyncService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 142 | INFO | `"Starting full cache rebuild for chatId={ChatId}", ...` | `chatId` is `long` param | **NEEDS TRACING** |

### Repositories/ContentDetectionConfigRepository.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 117 | DEBUG | `"No chat-specific config found for chat {ChatId}", chatId` | `chatId` is `long` param (repo) | SKIP (debug) |
| 121 | DEBUG | `"Loaded raw chat config for {ChatId}", chatId` | same | SKIP (debug) |
| 126 | ERROR | `"Failed to retrieve chat config for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 159 | DEBUG | `"No chat-specific config for {ChatId}, using global", chatId` | same | SKIP (debug) |
| 193 | DEBUG | `"Loaded merged config for chat {ChatId}...", chatId` | same | SKIP (debug) |
| 198 | ERROR | `"Failed to retrieve content detection configuration for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 237 | INFO | `"Updated content detection configuration for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 242 | ERROR | `"Failed to update content detection configuration for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 294 | INFO | `"Deleted content detection configuration for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 299 | ERROR | `"Failed to delete content detection configuration for chat {ChatId}", chatId` | same | **NEEDS TRACING** |
| 358 | ERROR | `"Failed to get critical check names for chat {ChatId}", chatId` | same | **NEEDS TRACING** |

---

## TelegramGroupsAdmin.BackgroundJobs

### Jobs/ChatHealthCheckJob.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 90 | INFO | `"Running health check for chat {ChatId}", payload.ChatId.Value` | `payload.ChatId` is `long?` | **NEEDS TRACING** |

### Jobs/BlocklistSyncJob.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 70 | INFO | `"BlocklistSyncJob started with payload: ...ChatId={ChatId},...", ..., payload.ChatId,...` | `payload.ChatId` is `long?` | **NEEDS TRACING** |

### Jobs/RotateBackupPassphraseJob.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 68 | INFO | `"Starting passphrase rotation for user {UserId}...", userId,...` | `userId` is web user ID string | SKIP (web user, not Telegram) |

### Services/Backup/PassphraseManagementService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 84 | INFO | `"Initiating passphrase rotation for user {UserId}", userId` | `userId` is web user ID string | SKIP (web user, not Telegram) |

---

## TelegramGroupsAdmin (Main Web App)

### Endpoints/EmailVerificationEndpoints.cs (DONE - Fix 2)

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 44 | WARN | `"Verification token {Token} references non-existent user {UserId}", token, verificationToken.UserId` | no UserRecord available (user is null) | SKIP (entity doesn't exist) |
| 51 | INFO | `"User {UserId} already verified, but token matched", user.Id` | `user` is `UserRecord` | DONE |
| 68 | INFO | `"Email verified for user {UserId}", user.Id` | `user` is `UserRecord` | DONE |

### Services/AuthService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 414 | WARN | `"Password change attempt for non-existent user: {UserId}", userId` | `userId` is string, user not found | SKIP (entity doesn't exist) |
| 635 | WARN | `"Password reset token references non-existent user {UserId}", resetToken.UserId` | user is null (not found) | SKIP (entity doesn't exist) |

### Services/MessageContextAdapter.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 46 | ERROR | `"Failed to get message history for chat {ChatId}", chatId` | `chatId` is `long` param | **NEEDS TRACING** |

### Services/PromptBuilder/PromptBuilderService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 95 | ERROR | `"Error generating custom prompt for chat {ChatId}", request.ChatId` | `request.ChatId` is `long` | **NEEDS TRACING** |

### Services/NotificationStateService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 60 | ERROR | `"Failed to refresh notifications for user {UserId}", _userId` | `_userId` is web user ID string | SKIP (web user, not Telegram) |

### Services/Auth/*.cs

| File | Line | Level | Log | Status |
|------|------|-------|-----|--------|
| IntermediateAuthService.cs | 34 | INFO | `"Created intermediate auth token for user {UserId}...", userId,...` | SKIP (web user ID string) |
| IntermediateAuthService.cs | 124 | INFO | `"Successfully validated and consumed token for user {UserId}", userId` | SKIP (web user ID string) |
| TotpService.cs | 303 | INFO | `"Generated {Count} recovery codes for user: {UserId}", codes.Count, userId` | SKIP (web user ID string) |
| AccountLockoutService.cs | 42 | WARN | `"User {UserId} not found when handling failed login", userId` | SKIP (entity doesn't exist) |
| AccountLockoutService.cs | 107 | WARN | `"User {UserId} not found when attempting manual unlock", userId` | SKIP (entity doesn't exist) |
| PendingRecoveryCodesService.cs | 85 | INFO | `"Successfully retrieved pending recovery codes for user {UserId}", userId` | SKIP (web user ID string) |

### Repositories/UserRepository.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 285 | INFO | `"Deleted all recovery codes for user {UserId}", userId` | web user ID string (repo) | SKIP (web user) |
| 312 | INFO | `"Added {Count} recovery codes for user {UserId}", codeHashes.Count, userId` | same | SKIP (web user) |
| 366 | INFO | `"Invite {Token} used by user {UserId}", token, userId` | same | SKIP (web user) |

### Repositories/VerificationTokenRepository.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 27 | DEBUG | `"Created verification token {Id} for user {UserId}, type {TokenType}",...` | web user ID string | SKIP (debug) |
| 114 | DEBUG | `"Deleted {Count} verification tokens for user {UserId}", tokens.Count, userId` | same | SKIP (debug) |

### Services/InviteService.cs

| Line | Level | Log | Context | Status |
|------|-------|-----|---------|--------|
| 47 | INFO | `"Created invite {Token} by user {UserId}, expires at {ExpiresAt}",...` | web user ID string | SKIP (web user) |

---

## TelegramGroupsAdmin.Core

### Repositories/ReportsRepository.cs (DONE - Fix 8)

| Line | Level | Log | Status |
|------|-------|-----|--------|
| 234 | INFO | `"...in chat {ChatId} by {Reporter}", ..., report.Chat.Id,...` | DONE |
| 487-490 | INFO | `"User {UserId} in chat {ChatId}...", examFailure.User.Id, examFailure.Chat.Id,...` | DONE |

---

## Summary

| Status | Count | Description |
|--------|-------|-------------|
| **DONE** | 22 | Already in current plan (Fixes 1-8) |
| **NEEDS TRACING** | ~50 | Callers need to be traced, signatures changed to thread identity through |
| **CLEANUP** | 1 | MessageProcessingService:609 — simplify verbose LogDisplayName to `.ToLogDebug()` |
| **SKIP** | ~30 | Debug-level, web user IDs, or entity genuinely doesn't exist |

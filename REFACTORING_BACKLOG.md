# Refactoring Backlog

This document tracks refactoring opportunities identified by automated analysis and manual review. Items are prioritized by impact and effort.

**Last Updated:** 2025-10-15

---

## Critical Issues (Do Immediately)

### C1. Replace Fire-and-Forget Tasks with TickerQ Scheduled Jobs

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
**Lines:** 132-190, 301-312, 847-866
**Severity:** 🔴 **CRITICAL** - Production Risk

**Problem:**
Currently using `Task.Run` with fire-and-forget pattern (`_ = Task.Run(...)`) for delayed execution:
1. **Welcome timeout handling** (line 132) - waits X seconds then kicks user if no response
2. **Warning message deletion** (line 301) - waits 10s then deletes warning
3. **Fallback message deletion** (line 847) - waits 30s then deletes fallback message

**Why This Is Bad:**
- ❌ **Unhandled exceptions** - If task fails, exception goes to threadpool (no logging, silent failure)
- ❌ **No persistence** - If app restarts, all pending tasks are lost (users not kicked, messages not deleted)
- ❌ **No retry logic** - Telegram API failures = permanent failure
- ❌ **Memory leaks** - Tasks not tracked, can't cancel on shutdown
- ❌ **Ignores TickerQ** - We have TickerQ installed but aren't using it!

**Current Code Example:**
```csharp
// Line 132 - Welcome timeout (WRONG)
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

    try
    {
        // Kick user if they didn't respond
        await KickUserAsync(botClient, chatId, userId, default);
        // Delete welcome message
        await botClient.DeleteMessage(chatId: chatId, messageId: messageId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process welcome timeout...");
    }
});
```

**Correct Solution - Use TickerQ:**
```csharp
// Create TickerQ job class
public class WelcomeTimeoutJob(
    IDbContextFactory<AppDbContext> contextFactory,
    TelegramBotClientFactory botClientFactory,
    IOptions<TelegramOptions> telegramOptions,
    ILogger<WelcomeTimeoutJob> logger)
{
    [TickerFunction(functionName: "ProcessWelcomeTimeout")]
    public async Task ProcessTimeoutAsync(
        TickerFunctionContext<WelcomeTimeoutData> context,
        CancellationToken cancellationToken)
    {
        var data = context.Payload;
        var botClient = botClientFactory.GetOrCreate(telegramOptions.Value.BotToken);

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Check if user already responded
        var response = await dbContext.WelcomeResponses
            .FirstOrDefaultAsync(r => r.UserId == data.UserId && r.GroupChatId == data.ChatId, cancellationToken);

        if (response != null)
        {
            logger.LogDebug("User {UserId} already responded, skipping timeout", data.UserId);
            return;
        }

        // Kick user
        try
        {
            await botClient.BanChatMember(chatId: data.ChatId, userId: data.UserId, cancellationToken: cancellationToken);
            await botClient.UnbanChatMember(chatId: data.ChatId, userId: data.UserId, onlyIfBanned: true, cancellationToken: cancellationToken);
            logger.LogInformation("Kicked user {UserId} from chat {ChatId} due to welcome timeout", data.UserId, data.ChatId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kick user {UserId}", data.UserId);
        }

        // Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: data.ChatId, messageId: data.WelcomeMessageId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", data.WelcomeMessageId);
        }
    }
}

public record WelcomeTimeoutData(long UserId, long ChatId, int WelcomeMessageId);

// Schedule in WelcomeService instead of Task.Run
await _tickerScheduler.ScheduleAsync(
    functionName: "ProcessWelcomeTimeout",
    payload: new WelcomeTimeoutData(userId, chatId, messageId),
    runAt: DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds),
    cancellationToken: cancellationToken);
```

**Benefits:**
- ✅ **Persistence** - Survives app restarts (TickerQ uses database)
- ✅ **Retry logic** - TickerQ automatically retries failed jobs
- ✅ **Logging** - All exceptions properly logged
- ✅ **Testable** - Can unit test job logic
- ✅ **Monitoring** - Can query pending/failed jobs from database
- ✅ **Cancellation** - Proper cancellation token support

**Tasks to Create:**
1. `WelcomeTimeoutJob` - Kick user if no response within timeout
2. `DeleteMessageJob` - Delete messages after delay (reusable for warnings + fallback)
3. Update `WelcomeService` to schedule TickerQ jobs instead of `Task.Run`

**Effort:** Medium (4-6 hours with testing)
**Impact:** 🔥 **HIGH** - Prevents data loss, improves reliability

---

## High Priority (Do Soon)

### H1. Extract Duplicate ChatPermissions Objects

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
**Lines:** 404-420, 463-479
**Severity:** 🟡 Medium - Code Duplication

**Problem:** Two nearly identical 28-line `ChatPermissions` definitions differ only in boolean values.

**Solution:**
```csharp
private static ChatPermissions CreateRestrictedPermissions() => new()
{
    CanSendMessages = false,
    CanSendAudios = false,
    CanSendDocuments = false,
    CanSendPhotos = false,
    CanSendVideos = false,
    CanSendVideoNotes = false,
    CanSendVoiceNotes = false,
    CanSendPolls = false,
    CanSendOtherMessages = false,
    CanAddWebPagePreviews = false,
    CanChangeInfo = false,
    CanInviteUsers = false,
    CanPinMessages = false,
    CanManageTopics = false
};

private static ChatPermissions CreateDefaultPermissions() => new()
{
    CanSendMessages = true,
    CanSendAudios = true,
    CanSendDocuments = true,
    CanSendPhotos = true,
    CanSendVideos = true,
    CanSendVideoNotes = true,
    CanSendVoiceNotes = true,
    CanSendPolls = true,
    CanSendOtherMessages = true,
    CanAddWebPagePreviews = true,
    CanChangeInfo = false,      // Still restricted
    CanInviteUsers = true,
    CanPinMessages = false,     // Still restricted
    CanManageTopics = false     // Still restricted
};
```

**Benefits:** Single source of truth, easier to modify permission policies
**Effort:** Low (1-2 hours)
**Impact:** Maintainability

---

### H2. Extract Magic Numbers to Named Constants (SpamDetectorFactory)

**File:** `TelegramGroupsAdmin.SpamDetection/Services/SpamDetectorFactory.cs`
**Lines:** 212, 287, 294, 314, 382
**Severity:** 🟡 Medium - Maintainability

**Problem:** Magic numbers scattered throughout spam detection logic make thresholds hard to understand and modify.

**Solution:**
```csharp
private const int NetConfidenceVetoThreshold = 50;
private const int MinimumTranslationLength = 20;
private const double LatinScriptThreshold = 0.8;

// Usage:
var shouldVeto = netConfidence > NetConfidenceVetoThreshold && config.OpenAI.VetoMode;
if (netConfidence > NetConfidenceVetoThreshold)
    return SpamAction.ReviewQueue;
if (request.Message.Length >= MinimumTranslationLength)
    await TranslateAsync(...);
return (double)latinCount / letterCount > LatinScriptThreshold;
```

**Benefits:** Self-documenting code, easier to tune thresholds
**Effort:** Low (30 minutes)
**Impact:** Readability + Maintainability

---

## Medium Priority (Nice to Have)

### M1. Reduce Nesting with Guard Clauses (SpamDetectorFactory)

**File:** `TelegramGroupsAdmin.SpamDetection/Services/SpamDetectorFactory.cs`
**Lines:** 72-93, 113-128

**Problem:** Deep nesting (3 levels) makes control flow hard to follow.

**Solution:** Use guard clauses with early returns:
```csharp
var openAICheck = _spamChecks.FirstOrDefault(check => check.CheckName == "OpenAI");
if (openAICheck is null)
    return AggregateResults(checkResults, config);

_logger.LogDebug("Running OpenAI veto check for user {UserId}", request.UserId);

var vetoRequest = request with { HasSpamFlags = true };
if (!openAICheck.ShouldExecute(vetoRequest))
    return AggregateResults(checkResults, config);

var vetoResult = await openAICheck.CheckAsync(vetoRequest, cancellationToken);
checkResults.Add(vetoResult);

if (!vetoResult.IsSpam)
{
    _logger.LogInformation("OpenAI vetoed spam detection...");
    return CreateVetoedResult(checkResults, vetoResult);
}
```

**Benefits:** Flatter code, easier to follow "happy path"
**Effort:** Low (20 minutes)
**Impact:** Readability

---

### M2. Use Switch Expressions for Action Determination (SpamDetectorFactory)

**File:** `TelegramGroupsAdmin.SpamDetection/Services/SpamDetectorFactory.cs`
**Lines:** 285-302

**Current:**
```csharp
private SpamAction DetermineActionFromNetConfidence(int netConfidence, SpamDetectionConfig config)
{
    if (netConfidence > 50)
        return SpamAction.ReviewQueue;
    if (netConfidence > 0)
        return SpamAction.ReviewQueue;
    return SpamAction.Allow;
}
```

**Suggested:**
```csharp
private SpamAction DetermineActionFromNetConfidence(int netConfidence, SpamDetectionConfig config)
{
    return netConfidence switch
    {
        > NetConfidenceVetoThreshold => SpamAction.ReviewQueue, // High confidence - pending veto
        > 0 => SpamAction.ReviewQueue,                          // Low confidence - review
        _ => SpamAction.Allow                                   // No spam
    };
}
```

**Benefits:** More concise, pattern matching
**Effort:** Low (10 minutes)
**Impact:** Readability

---

### M3. Extract Callback Data Parsing (WelcomeService)

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
**Lines:** 232-280

**Problem:** Complex parsing logic embedded in handler makes flow hard to follow.

**Solution:** Extract to dedicated parsing method with type-safe record types:
```csharp
private record CallbackData(string Action, long TargetUserId);
private record DmAcceptData(long GroupChatId, long TargetUserId);

private bool TryParseCallbackData(string? data, out CallbackData? result, out DmAcceptData? dmAcceptResult)
{
    // ... parsing logic ...
}
```

**Benefits:** Type safety, self-documenting, easier to add new callback formats
**Effort:** Medium (3-4 hours)
**Impact:** Readability + Maintainability

---

### M4. Extract User Validation Logic (WelcomeService)

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
**Lines:** 245-258, 283-320

**Problem:** Duplicate "wrong user clicked button" logic.

**Solution:** Extract to reusable validation method with enum for response type.

**Benefits:** DRY principle, centralized validation
**Effort:** Medium (2-3 hours)
**Impact:** Maintainability

---

## Low Priority (Optional Polish)

### L1. Use Collection Expressions (SpamDetectorFactory)

**Lines:** 59, 107

**Current:** `var checkResults = new List<SpamCheckResponse>();`
**Suggested:** `List<SpamCheckResponse> checkResults = [];`

**Effort:** Low (5 minutes)
**Impact:** Consistency with modern C# style

---

### L2. Use Pattern Matching for Filtering (SpamDetectorFactory)

**Line:** 134

**Current:** `check.CheckName != "OpenAI" && check.CheckName != "InvisibleChars"`
**Suggested:** `check.CheckName is not ("OpenAI" or "InvisibleChars")`

**Effort:** Low (5 minutes)
**Impact:** Readability (more concise)

---

### L3. Consider Primary Constructors (Team Decision)

**Files:** `SpamDetectorFactory.cs`, `WelcomeService.cs`

**Consideration:** C# 12 primary constructors reduce boilerplate for simple DI scenarios.

**Pros:**
- Eliminates 10+ lines of field declarations + assignments
- Modern C# 12 idiom

**Cons:**
- Less familiar to developers from older .NET versions
- Some teams prefer explicit field declarations

**Recommendation:** Team discussion required before adopting across codebase.

---

## Deferred / Won't Fix

### ❌ Primary Constructors for Complex Services

**Reason:** WelcomeService has complex initialization logic (`.Value` extraction). Primary constructors work best for straightforward DI-only scenarios.

---

## Summary Statistics

| Priority | Count | Est. Effort | Impact |
|----------|-------|-------------|--------|
| Critical | 1 | 4-6 hours | Production reliability |
| High | 2 | 2-3 hours | Maintainability |
| Medium | 4 | 7-10 hours | Code quality |
| Low | 3 | 30 minutes | Style consistency |

**Total Estimated Effort:** 13-19 hours

**Recommended Execution Order:**
1. **C1** (TickerQ tasks) - Prevents production issues
2. **H1** (ChatPermissions) + **H2** (Magic numbers) - Quick wins
3. **M1-M4** - Quality improvements (can be done incrementally)
4. **L1-L3** - Polish (optional, low ROI)

---

## Notes

- All suggestions reviewed for readability-first principle
- Modern C# features only suggested when they improve clarity
- Focus on high-impact changes over novelty
- Team discussion required for primary constructors (L3)

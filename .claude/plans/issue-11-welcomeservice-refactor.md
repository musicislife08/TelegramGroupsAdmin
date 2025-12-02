# REFACTOR-4: Extract WelcomeService Pure Logic Components

## Summary

Issue #11: Extract testable pure-function components from WelcomeService (1,044 lines) to enable unit testing without Telegram API dependencies.

**Approach**: Extract pure logic first (message builders, keyboard builders, callback parsers). If WelcomeService remains massive after extraction, split into partial class files.

---

## Current State Analysis

| Metric | Value |
|--------|-------|
| File Size | 1,044 lines |
| Public Methods | 2 (HandleChatMemberUpdateAsync, HandleCallbackQueryAsync) |
| Private Methods | 10+ |
| Dependencies | 5 constructor + 5 scoped |
| Existing Tests | None |

### Methods by Testability

**Pure Logic (Extractable)**:
- Message formatting with variable substitution
- Keyboard building (Accept/Deny, DM deep link)
- Callback data parsing and validation
- Deep link URL construction

**Telegram-Coupled (Stay in WelcomeService)**:
- RestrictUserPermissionsAsync, RestoreUserPermissionsAsync, KickUserAsync
- GetChatNameAsync
- Main orchestration flows

---

## Extraction Plan

### Phase 1: Extract WelcomeMessageBuilder (Test-First)

**New File**: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeMessageBuilder.cs`

Extract pure message formatting functions:

```csharp
public static class WelcomeMessageBuilder
{
    // Format welcome message with variable substitution
    public static string FormatWelcomeMessage(
        WelcomeConfig config,
        string username,
        string chatName,
        WelcomeMode mode);

    // Format rules confirmation with footer
    public static string FormatRulesConfirmation(
        WelcomeConfig config,
        string username,
        string chatName);

    // Format DM acceptance confirmation
    public static string FormatDmAcceptanceConfirmation(string chatName);

    // Format wrong-user warning
    public static string FormatWrongUserWarning(string username);
}
```

**Tests**: `WelcomeMessageBuilderTests.cs` - 100% testable, no mocks

### Phase 2: Extract WelcomeKeyboardBuilder

**New File**: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeKeyboardBuilder.cs`

Extract keyboard construction logic:

```csharp
public static class WelcomeKeyboardBuilder
{
    // Build chat mode keyboard (Accept/Deny buttons)
    public static InlineKeyboardMarkup BuildChatModeKeyboard(
        WelcomeConfig config,
        long userId);

    // Build DM mode keyboard (deep link button)
    public static InlineKeyboardMarkup BuildDmModeKeyboard(
        WelcomeConfig config,
        long chatId,
        long userId,
        string botUsername);

    // Build return-to-chat keyboard for DM confirmation
    public static InlineKeyboardMarkup BuildReturnToChatKeyboard(
        string chatName,
        string chatLink);
}
```

**Tests**: `WelcomeKeyboardBuilderTests.cs` - 100% testable, no mocks

### Phase 3: Extract WelcomeCallbackParser

**New File**: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeCallbackParser.cs`

Extract callback data parsing and validation:

```csharp
public static class WelcomeCallbackParser
{
    // Parse callback data format
    public static WelcomeCallbackData? ParseCallbackData(string? data);

    // Validate caller is target user
    public static bool ValidateCallerIsTarget(long callerId, long targetUserId);
}

public record WelcomeCallbackData(
    WelcomeCallbackType Type,  // Accept, Deny, DmAccept
    long UserId,
    long? ChatId = null);      // Only for DmAccept

public enum WelcomeCallbackType { Accept, Deny, DmAccept }
```

**Tests**: `WelcomeCallbackParserTests.cs` - 100% testable, no mocks

### Phase 4: Extract WelcomeDeepLinkBuilder

**New File**: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeDeepLinkBuilder.cs`

Extract deep link URL construction:

```csharp
public static class WelcomeDeepLinkBuilder
{
    // Build /start deep link for DM welcome flow
    public static string BuildStartDeepLink(
        string botUsername,
        long chatId,
        long userId);

    // Build chat link (public or null for private - caller handles invite)
    public static string? BuildPublicChatLink(string? chatUsername);
}
```

**Tests**: `WelcomeDeepLinkBuilderTests.cs` - 100% testable, no mocks

### Phase 5: Update WelcomeService

Replace inline logic with calls to extracted builders:

```csharp
// Before (inline):
var messageText = config.MainWelcomeMessage
    .Replace("{username}", username)
    .Replace("{chat_name}", chatName)
    .Replace("{timeout}", config.TimeoutSeconds.ToString());

// After (extracted):
var messageText = WelcomeMessageBuilder.FormatWelcomeMessage(
    config, username, chatName, config.Mode);
```

### Phase 6: Evaluate Remaining Size

After extraction, if WelcomeService is still >500 lines, consider partial classes:

```
WelcomeService.cs              (core orchestration, ~200 lines)
WelcomeService.Accept.cs       (HandleAcceptAsync, HandleDmAcceptAsync)
WelcomeService.Deny.cs         (HandleDenyAsync)
WelcomeService.Permissions.cs  (Restrict/Restore/Kick)
```

---

## Files to Create

| File | Purpose | Testability |
|------|---------|-------------|
| `Services/Welcome/WelcomeMessageBuilder.cs` | Message formatting | 100% pure |
| `Services/Welcome/WelcomeKeyboardBuilder.cs` | Keyboard construction | 100% pure |
| `Services/Welcome/WelcomeCallbackParser.cs` | Callback parsing | 100% pure |
| `Services/Welcome/WelcomeDeepLinkBuilder.cs` | URL construction | 100% pure |

## Files to Modify

| File | Changes |
|------|---------|
| `WelcomeService.cs` | Replace inline logic with builder calls |

## Test Files to Create

| File | Coverage |
|------|----------|
| `WelcomeMessageBuilderTests.cs` | Variable substitution, all message types |
| `WelcomeKeyboardBuilderTests.cs` | Both modes, button labels, callback data |
| `WelcomeCallbackParserTests.cs` | All formats, validation, edge cases |
| `WelcomeDeepLinkBuilderTests.cs` | URL formats, public/private handling |

**Note**: No baseline tests for WelcomeService orchestration methods - too much Telegram mocking for low value. Pure function tests on extracted builders + manual testing is sufficient.

---

## Success Criteria

- [ ] All new builder classes are static with pure functions
- [ ] Each builder has dedicated test file
- [ ] WelcomeService compiles and calls extracted builders
- [ ] No behavior changes (verify via manual testing)
- [ ] Build succeeds with `dotnet build`

---

## Critical Files to Read Before Implementation

1. `WelcomeService.cs` - Current implementation (1,044 lines)
2. `WelcomeConfig.cs` - Configuration model with modes
3. `TranslationHandler.cs` + `TranslationHandlerTests.cs` - Pattern for pure function extraction
4. `MediaProcessingHandler.cs` + tests - Similar handler extraction pattern
5. `ContentCheckCoordinatorTests.cs` - NSubstitute mocking patterns

# Coding Conventions

**Analysis Date:** 2026-03-15

## Naming Patterns

**Files:**
- PascalCase for all C# files (e.g., `UserMessagingService.cs`, `PassphraseGenerator.cs`)
- Suffix conventions:
  - `*Dto` for data transfer objects in `TelegramGroupsAdmin.Data.Models` (required for backup/restore system)
  - `*Service` for service classes (e.g., `UserMessagingService`, `TelegramPhotoService`)
  - `*Repository` for data access layer classes
  - `*Tests` for test classes (e.g., `PassphraseGeneratorTests`, `ContentDetectionConfigMappingsTests`)
  - `*Handler` for message/command handlers
  - `*Helper` for test infrastructure (e.g., `MigrationTestHelper`)
  - Interfaces start with `I` (e.g., `IUserMessagingService`, `IBotDmService`, `IBotMessageService`)

**Functions:**
- PascalCase for all public/internal methods (e.g., `Generate()`, `CalculateEntropy()`, `SendToUserAsync()`)
- Async methods end with `Async` suffix (e.g., `SendDmAsync()`, `CreateDatabaseAndApplyMigrationsAsync()`)
- Private methods follow same PascalCase convention (e.g., `LoadEmbeddedWordlist()`)

**Variables:**
- camelCase for local variables and parameters (e.g., `wordCount`, `passphrase`, `userId`, `cancellationToken`)
- Private fields use camelCase with leading underscore (e.g., `_logger`, `_telegramUserRepository`, `_context`)
- Readonly static fields use PascalCase with readonly modifier (e.g., `private static readonly Lazy<string[]> Wordlist`)
- Constants follow standard .NET convention: PascalCase for `const` fields (e.g., `private const int MinimumWords`)
- Named parameters at call sites use camelCase: `wordCount: 6`, `separator: "-"`, `cancellationToken: cancellationToken`

**Types:**
- Records use PascalCase (e.g., `sealed record UserIdentity`, `sealed record ChatIdentity`)
- Enums use PascalCase values (e.g., `PermissionLevel.Owner`, `UserStatus.Active`)
- Nested classes use PascalCase (e.g., `class Messages { const int Msg1_Id = 82619; }`)

## Code Style

**Formatting:**
- No explicit formatter configured - project relies on Visual Studio default formatting
- Four spaces for indentation (implicit in Visual Studio defaults)
- Line length: no hard limit enforced
- One statement per line

**Linting:**
- Compiler treats warnings as errors: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in all project files
- .NET Analyzers enabled by default
- NUnit.Analyzers enabled in test projects: `<PackageReference Include="NUnit.Analyzers" />`
- Suppressed warnings documented in .csproj: `<NoWarn>$(NoWarn);EF1002</NoWarn>` (e.g., Entity Framework SQL injection warnings safe in tests)

## Import Organization

**Order:**
1. System namespaces (`using System;`, `using System.Collections.Generic;`)
2. System.Linq, System.Text, System.Threading
3. Microsoft namespaces (`using Microsoft.EntityFrameworkCore;`, `using Microsoft.Extensions.Logging;`)
4. Third-party packages (`using Telegram.Bot.Types;`, `using NSubstitute;`, `using NUnit.Framework;`)
5. Project-internal namespaces (`using TelegramGroupsAdmin.Core;`, `using TelegramGroupsAdmin.Data.Models;`)

**Path Aliases:**
- No global `using` aliases configured
- Fully qualified namespaces used throughout

**Global Usings:**
- `using NUnit.Framework;` is globally imported in test projects via `.csproj`:
  ```xml
  <ItemGroup>
    <Using Include="NUnit.Framework" />
  </ItemGroup>
  ```
- No other global usings configured

## Error Handling

**Patterns:**
- Exceptions are caught with fully-qualified type: `catch (Exception ex)`
- Logging error with exception context: `_logger.LogError(ex, "message", args)`
- No silent catches (always log or re-throw)
- Arguments are validated with descriptive exceptions:
  ```csharp
  if (wordCount < MinimumWords)
  {
      throw new ArgumentException(
          $"Minimum {MinimumWords} words required...",
          nameof(wordCount));
  }
  ```
- Repository methods check for null and return `null` for not-found cases: `var dto = await _context.Users.FindAsync(id); return dto?.ToModel();`
- Async operations use try-catch with proper exception propagation (no swallowing exceptions)

## Logging

**Framework:** Microsoft.Extensions.Logging via dependency injection

**Patterns:**
- Inject via constructor: `private readonly ILogger<ClassName> _logger;`
- Generic type parameter is the containing class name: `ILogger<UserMessagingService>`
- Log levels follow conventions:
  - **Information**: Significant operations completed successfully (e.g., "Sent DM to user {User}")
  - **Debug**: Detailed diagnostics, fallback decisions (e.g., "DM to {User} failed, falling back to chat mention")
  - **Warning**: Recoverable issues (e.g., API timeout, retry needed)
  - **Error**: Recoverable exceptions with context (e.g., "Failed to send message")
- Use structured logging with named parameters:
  ```csharp
  _logger.LogInformation(
      "Sent DM to user {User}: {MessagePreview}",
      user.ToLogInfo(userId),
      messageText[..50]);
  ```
- Identity logging extensions on types:
  - `user.ToLogInfo()` - For Information level (name only)
  - `user.ToLogDebug()` - For Debug, Warning, and Error levels (name + ID)
  - Log extensions live in `Core/Extensions/CoreLoggingExtensions.cs` and `Telegram/Extensions/TelegramLoggingExtensions.cs`

## Comments

**When to Comment:**
- Document XML summary comments on public types and methods (aspirational, not enforced):
  ```csharp
  /// <summary>
  /// Generates a secure, memorable passphrase using the EFF Large Wordlist.
  /// </summary>
  /// <param name="wordCount">Number of words (minimum 5, recommended 6). Default: 6.</param>
  /// <returns>A passphrase like "correct-horse-battery-staple"</returns>
  /// <exception cref="ArgumentException">Thrown if wordCount is less than 5.</exception>
  public static string Generate(int wordCount = RecommendedWords, string separator = "-")
  ```
- Document architectural decisions and non-obvious logic:
  ```csharp
  // Use cryptographically secure random number generation
  var index = RandomNumberGenerator.GetInt32(0, WordlistSize);
  ```
- Explain WHY, not WHAT (code should be self-documenting for WHAT)
- Mark temporary workarounds with comments:
  ```csharp
  // TODO: EF Core generates DROP+CREATE instead of RENAME - manually fix in migrations
  ```

## Function Design

**Size:**
- No hard limits enforced
- Aim for single responsibility - one logical task per function
- Helper methods extract repeated logic
- Example: `SendToUserAsync` delegates to `SendChatMentionAsync` for fallback logic

**Parameters:**
- Use named parameters for bool arguments (user preference):
  ```csharp
  // Good
  await helper.CreateDatabaseAndApplyMigrationsAsync();

  // Bad (implicit bool would be harder to read)
  await helper.CreateDatabase(true, false);
  ```
- CancellationToken always named `cancellationToken` (not `ct`), placed at end of parameter list
- Prefer descriptive names over abbreviations everywhere — readability over brevity (e.g., `cancellationToken` not `ct`, `userId` not `uid`)
- Default parameter values used for optional behavior (e.g., `wordCount = RecommendedWords`)

**Return Values:**
- Never return tuples - use named records or single values
- Async methods return `Task<T>` or `Task` (not `Task` wrapping void)
- Repositories return domain models (never expose `Dto` types in public interfaces)
- Maps via `.ToModel()` extension on read paths, `.ToDto()` on write paths

## Module Design

**Exports:**
- Interfaces are public: `public interface IUserMessagingService`
- Implementation classes are public: `public class UserMessagingService : IUserMessagingService`
- Helper classes are internal when not part of public API
- Test helpers are internal: `internal class MigrationTestHelper`

**Barrel Files:**
- No barrel files (no `index.ts`-like aggregation) - each type is in its own file
- Namespaces organize logically: `TelegramGroupsAdmin.Telegram.Services`, `TelegramGroupsAdmin.Data.Models`

**Extension Methods:**
- Grouped in `Extensions/` subdirectories per project
- Examples:
  - `TelegramGroupsAdmin.Telegram/Extensions/IdentityExtensions.cs` - Extensions for identity types
  - `TelegramGroupsAdmin.Core/Extensions/CoreLoggingExtensions.cs` - Logging helpers
  - `TelegramGroupsAdmin.Data/Extensions/MappingExtensions.cs` - Model mapping helpers
- Static class naming: `[Type]Extensions` (e.g., `IdentityExtensions`)

---

*Convention analysis: 2026-03-15*

# Testing Patterns

**Analysis Date:** 2026-03-15

## Test Framework

**Runner:**
- NUnit 4.x with NUnit3TestAdapter
- Config: No explicit config file - uses NUnit defaults
- .NET Test SDK: Microsoft.NET.Test.Sdk

**Assertion Library:**
- NUnit assertions: `Assert.That(actual, Is.EqualTo(expected))`
- Fluent syntax: `Is.EqualTo()`, `Does.Contain()`, `Is.GreaterThan()`, `Does.Match()` (regex patterns)
- Multiple assertions in single test: `using (Assert.EnterMultipleScope()) { ... }` for grouped assertions

**Run Commands:**
```bash
# Run all tests in solution
dotnet test

# Run specific test project
dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj

# Run tests with specific filter
dotnet test --filter "Namespace=TelegramGroupsAdmin.UnitTests.Core.Security"

# Watch mode (not standard for dotnet test - use IDE)
# IDE: Right-click test class → Run or Run with Coverage

# Coverage (via coverlet - included in test projects)
dotnet test /p:CollectCoverage=true
```

## Test File Organization

**Location:**
- Parallel structure to source code
- Example: Source `TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs` → Test `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs`
- Migration tests: `TelegramGroupsAdmin.IntegrationTests/Migrations/InfrastructureTests.cs`
- Test data: `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs`

**Naming:**
- Test files: `[ClassUnderTest]Tests.cs` (e.g., `PassphraseGeneratorTests.cs`, `UserMessagingServiceTests.cs`)
- Test classes: `[ClassUnderTest]Tests` with `[TestFixture]` attribute
- Test methods: `[MethodUnderTest]_[Scenario]_[Expected]` pattern (e.g., `Generate_Default_Returns6Words()`, `CalculateEntropy_ZeroTimeout_PreservesZero()`)
- Alternative pattern: `Should[ExpectedBehavior]` (less common)

**Structure:**
```
TelegramGroupsAdmin.UnitTests/
├── Configuration/
│   ├── ContentDetectionConfigMappingsTests.cs
│   ├── AIProviderConfigTests.cs
│   └── ApiKeysConfigTests.cs
├── Core/
│   ├── Security/
│   │   └── PassphraseGeneratorTests.cs
│   ├── Utilities/
│   │   ├── UrlUtilitiesTests.cs
│   │   ├── BitwiseUtilitiesTests.cs
│   │   └── ...
├── Telegram/
│   └── ...
└── ...

TelegramGroupsAdmin.IntegrationTests/
├── Migrations/
│   ├── InfrastructureTests.cs
│   ├── CascadeBehaviorTests.cs
│   ├── CriticalMigrationTests.cs
│   └── ...
├── TestData/
│   ├── GoldenDataset.cs
│   └── SQL/
│       ├── 00_base_telegram_users.sql
│       ├── 01_base_web_users.sql
│       └── ...
└── TestHelpers/
    └── MigrationTestHelper.cs
```

## Test Structure

**Suite Organization:**
```csharp
[TestFixture]
public class PassphraseGeneratorTests
{
    #region Generate - Basic Tests

    [Test]
    public void Generate_Default_Returns6Words()
    {
        // Arrange
        var passphrase = PassphraseGenerator.Generate();

        // Act (implicit in Arrange above)

        // Assert
        var words = passphrase.Split('-');
        Assert.That(words.Length, Is.EqualTo(6));
    }

    #endregion

    #region Generate - Randomness Tests

    [Test]
    public void Generate_MultipleGenerated_AllDifferent()
    {
        var passphrases = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            var passphrase = PassphraseGenerator.Generate();
            passphrases.Add(passphrase);
        }

        Assert.That(passphrases.Count, Is.EqualTo(100));
    }

    #endregion
}
```

**Patterns:**
- **Setup/Teardown**: Use `[SetUp]` / `[TearDown]` for test initialization (used in integration tests with databases)
- **One-time setup**: Use `[OneTimeSetUp]` / `[OneTimeTearDown]` for class-level fixtures (rare)
- **Assertion grouping**: `using (Assert.EnterMultipleScope()) { ... }` to report all failures together instead of stopping at first
- **Parameterized tests**: `[TestCase(...)]` for multiple scenarios:
  ```csharp
  [TestCase(5)]
  [TestCase(6)]
  [TestCase(7)]
  public void Generate_VariousWordCounts_ReturnsCorrectCount(int wordCount)
  {
      var passphrase = PassphraseGenerator.Generate(wordCount: wordCount);
      var words = passphrase.Split('-');
      Assert.That(words.Length, Is.EqualTo(wordCount));
  }
  ```

## Mocking

**Framework:** NSubstitute 5.x

**Patterns:**
```csharp
// Create substitute (mock)
var mockUserRepository = Substitute.For<ITelegramUserRepository>();

// Configure return value
mockUserRepository.GetByTelegramIdAsync(userId, Arg.Any<CancellationToken>())
    .Returns(new TelegramUserIdentity { ... });

// Verify call was made
mockUserRepository.Received(1).GetByTelegramIdAsync(userId, Arg.Any<CancellationToken>());

// Verify never called
mockUserRepository.DidNotReceive().GetByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
```

**Critical Rule for Telegram.Bot Types:**
- `Telegram.Bot.Types` classes (`Message`, `User`, `Chat`, etc.) are concrete types with **non-virtual properties**
- **DO NOT use `Substitute.For<Message>()`** — NSubstitute cannot mock non-virtual members
- **Always use direct object initialization:**
  ```csharp
  // GOOD - Direct initialization
  var message = new Message
  {
      MessageId = 123,
      From = new User { Id = 100, IsBot = false },
      Text = "test"
  };

  // BAD - Will not work
  var message = Substitute.For<Message>();
  message.MessageId.Returns(123);  // ← Non-virtual, won't be mocked
  ```

**What to Mock:**
- Repository interfaces: `ITelegramUserRepository`, `IMessageRepository`
- Service dependencies: `IBotDmService`, `IBotMessageService`, `ILogger<T>`
- External API clients: configured via WireMock.Net for integration tests
- Database context: Use real `AppDbContext` with Testcontainers.PostgreSQL (not mocked) in integration tests

**What NOT to Mock:**
- Domain models: Use real objects or test fixtures
- Enum values: Use actual enum members
- Telegram.Bot SDK types: Use direct object initialization (see critical rule above)
- Database in integration tests: Use real PostgreSQL 18 via Testcontainers for schema validation

## Fixtures and Factories

**Test Data:**
```csharp
// GoldenDataset - Centralized test data constants
public static class GoldenDataset
{
    public static class Users
    {
        public const string User1_Id = "b388ee38-0ed3-4c09-9def-5715f9f07f56";
        public const string User1_Email = "owner@example.com";
        public const int User1_PermissionLevel = 2; // Owner
        // ...
    }

    public static class TelegramUsers
    {
        public const long User1_TelegramUserId = 100001;
        public const string User1_Username = "alice_user";
        // ...
    }

    // Seed methods for different test scenarios
    public static async Task SeedAsync(AppDbContext context, IDataProtectionProvider? dataProtectionProvider = null)
    {
        await SeedBaseDataAsync(context, dataProtectionProvider);
        await SeedGoldenDatasetTrainingLabelsAsync(context);
        await SeedMLTrainingDataScriptAsync(context);
        await context.SaveChangesAsync();
    }

    public static async Task SeedWithoutTrainingDataAsync(AppDbContext context)
    {
        await SeedBaseDataAsync(context);
        await context.SaveChangesAsync();
    }

    public static async Task SeedWithMinimalTrainingDataAsync(AppDbContext context)
    {
        await SeedBaseDataAsync(context);
        await SeedGoldenDatasetTrainingLabelsAsync(context);
        await context.SaveChangesAsync();
    }
}
```

**Location:**
- `TelegramGroupsAdmin.IntegrationTests/TestData/GoldenDataset.cs` - Centralized test data constants and seeding
- `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/` - Embedded SQL scripts for data seeding:
  - `00_base_telegram_users.sql`, `01_base_web_users.sql`, etc.
  - ML training data: `11_training_full.sql` (20 spam + 20 ham)
  - Unbalanced data: `20_unbalanced_100_20.sql`, `21_unbalanced_20_100.sql`
  - Deduplication data: `30_dedup_test_data.sql`
  - Analytics data: `50_analytics_test_data.sql`

## Coverage

**Requirements:** Not explicitly enforced - no coverage thresholds configured

**View Coverage:**
```bash
# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# Coverage files generated to: obj/Release/coverage.opencover.xml (or similar per project)
# View in IDE or upload to coverage service (CodeCov, etc.)
```

**Tool:** Coverlet.Collector (included in test projects via NuGet)

## Test Types

**Unit Tests:**
- **Project:** `TelegramGroupsAdmin.UnitTests`
- **Scope:** Individual functions/methods in isolation
- **Examples:**
  - `PassphraseGeneratorTests` - Tests `Generate()` and `CalculateEntropy()` functions
  - `ContentDetectionConfigMappingsTests` - Tests model mapping conversions
  - `UrlUtilitiesTests` - Tests utility functions
- **Speed:** Fast (~2 seconds total)
- **Dependencies:** Mocked via NSubstitute (no database, no I/O)
- **Run without special flags:** `dotnet test TelegramGroupsAdmin.UnitTests/TelegramGroupsAdmin.UnitTests.csproj`

**Integration Tests:**
- **Project:** `TelegramGroupsAdmin.IntegrationTests`
- **Scope:** Database operations with real PostgreSQL 18 via Testcontainers
- **Examples:**
  - `InfrastructureTests` - Verifies migrations apply, database isolation
  - `CascadeBehaviorTests` - Tests cascade delete rules
  - `DataIntegrityTests` - Tests constraints and data validation
- **Speed:** Slow (~20 minutes total) - includes container startup/teardown
- **Database:** Real PostgreSQL via `Testcontainers.PostgreSql`
- **Setup:** `MigrationTestHelper` creates unique database per test
  ```csharp
  [Test]
  public async Task ShouldCreateDatabaseAndApplyMigrations()
  {
      using var helper = new MigrationTestHelper();
      await helper.CreateDatabaseAndApplyMigrationsAsync();

      var tableCount = await helper.ExecuteScalarAsync(
          "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'");

      Assert.That(Convert.ToInt32(tableCount), Is.GreaterThan(0));
  }
  ```

**E2E Tests:**
- **Project:** `TelegramGroupsAdmin.E2ETests`
- **Framework:** Playwright Browser automation
- **Scope:** Full application flows (Blazor UI → API → Database)
- **Speed:** Very slow (requires running full app + browser)
- **Setup:** Uses `WebApplicationFactory` with `.UseKestrel()` for real HTTP server
- **Note:** See [E2E_TESTING.md](E2E_TESTING.md) in repo for full documentation

## Common Patterns

**Async Testing:**
```csharp
[Test]
public async Task SendToUserAsync_WithDmEnabled_SendsDm()
{
    // Arrange
    var mockRepository = Substitute.For<ITelegramUserRepository>();
    var mockDmService = Substitute.For<IBotDmService>();
    var service = new UserMessagingService(mockRepository, mockDmService, ...);

    var userId = 123L;
    var user = new TelegramUserIdentity { Id = userId, BotDmEnabled = true };

    mockRepository.GetByTelegramIdAsync(userId, Arg.Any<CancellationToken>())
        .Returns(user);
    mockDmService.SendDmAsync(userId, Arg.Any<string>(), null, Arg.Any<CancellationToken>())
        .Returns(new DmResult { DmSent = true });

    // Act
    var result = await service.SendToUserAsync(userId, new Chat { ... }, "test", cancellationToken: CancellationToken.None);

    // Assert
    Assert.That(result.Success, Is.True);
}
```

**Error Testing:**
```csharp
[Test]
public void Generate_BelowMinimum_ThrowsArgumentException()
{
    // Act & Assert
    Assert.Throws<ArgumentException>(() =>
        PassphraseGenerator.Generate(wordCount: 4));
}

[Test]
public void Generate_BelowMinimum_ExceptionContainsMinimumAndEntropy()
{
    // Act
    var exception = Assert.Throws<ArgumentException>(() =>
        PassphraseGenerator.Generate(wordCount: 3));

    // Assert - Multiple assertions on exception
    using (Assert.EnterMultipleScope())
    {
        Assert.That(exception!.Message, Does.Contain("5"));  // Minimum
        Assert.That(exception.Message, Does.Contain("64"));  // Entropy bits
        Assert.That(exception.ParamName, Is.EqualTo("wordCount"));
    }
}
```

**Database Integration Testing:**
```csharp
[TestFixture]
public class MigrationTests
{
    private MigrationTestHelper _helper;

    [SetUp]
    public async Task SetupAsync()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndApplyMigrationsAsync();
    }

    [TearDown]
    public void Cleanup()
    {
        _helper.Dispose();  // ← Deletes test database
    }

    [Test]
    public async Task ShouldApplyAllMigrations()
    {
        // Arrange
        await using var context = _helper.GetDbContext();

        // Act
        var migrations = await context.Database.GetAppliedMigrationsAsync();

        // Assert
        Assert.That(migrations, Is.Not.Empty);
    }
}
```

**Data Seeding for Tests:**
```csharp
[TestFixture]
public class AnalyticsRepositoryTests
{
    private AppDbContext _context;

    [SetUp]
    public async Task SetupAsync()
    {
        var helper = new MigrationTestHelper();
        await helper.CreateDatabaseAndApplyMigrationsAsync();
        _context = helper.GetDbContext();

        // Seed test data
        await GoldenDataset.SeedAnalyticsDataAsync(_context);
    }

    [Test]
    public async Task DailySpamSummary_Returns3SpamDetectionsForToday()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken: CancellationToken.None);

        // Assert - Uses constants from GoldenDataset.AnalyticsData
        Assert.That(summary.Count, Is.EqualTo(GoldenDataset.AnalyticsData.TodaySpamCount));
    }
}
```

---

*Testing analysis: 2026-03-15*

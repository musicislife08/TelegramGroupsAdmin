# Playwright E2E Testing Guide

This document covers the architecture and patterns used for end-to-end testing with Playwright in this project.

## Overview

E2E tests use **Playwright** for browser automation and **Testcontainers** for database isolation. Each test gets its own PostgreSQL database on a shared container.

## .NET 10 WebApplicationFactory + Kestrel

### The Problem

Playwright needs a **real HTTP server** - it launches an actual browser that makes real network requests. The default `WebApplicationFactory` creates an in-memory `TestServer` that doesn't bind to a network port.

### Pre-.NET 10 Workarounds (Don't Use)

Before .NET 10, developers used a "dual-host hack" that created TWO hosts:
- TestServer (for WebApplicationFactory compatibility)
- Kestrel (for real HTTP)

This caused issues like:
- Double resource usage
- Migration race conditions (both hosts trying to run migrations)
- Duplicate log output

### .NET 10 Solution: `UseKestrel()`

.NET 10 introduced native Kestrel support in `WebApplicationFactory`:

```csharp
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestWebApplicationFactory()
    {
        // Call UseKestrel() BEFORE StartServer() or accessing Services
        UseKestrel(0);  // Port 0 = dynamic port assignment
    }

    // Get the server address after initialization
    public string ServerAddress => ClientOptions.BaseAddress.ToString().TrimEnd('/');
}
```

**Key Points:**
- `UseKestrel()` must be called before the factory is initialized
- Use port `0` for dynamic port assignment
- Access `ClientOptions.BaseAddress` to get the bound address
- Only ONE host is created (no dual-host issues)

### API Reference

```csharp
// Overloads available:
void UseKestrel();                                    // Default settings
void UseKestrel(int port);                           // Specific port (0 = dynamic)
void UseKestrel(Action<KestrelServerOptions> config); // Full configuration

// Start the server explicitly (optional - accessing Services also starts it)
void StartServer();
```

**Documentation:** https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel

## Project Structure

```
TelegramGroupsAdmin.E2ETests/
├── TelegramGroupsAdmin.E2ETests.csproj
├── Fixtures/
│   ├── E2EFixture.cs              # Assembly-level: PostgreSQL container + Playwright
│   └── E2ETestBase.cs             # Base class for all E2E tests
├── Infrastructure/
│   ├── TestWebApplicationFactory.cs  # Custom factory with Kestrel + test DB
│   ├── TestEmailService.cs           # Stub email service for tests
│   ├── TestCredentials.cs            # Credential generation utilities
│   └── TestUserBuilder.cs            # Fluent builder for test users
├── PageObjects/
│   ├── LoginPage.cs                  # Login page object (static SSR)
│   ├── LoginVerifyPage.cs            # TOTP verification page object
│   └── RegisterPage.cs               # Registration page object (interactive Blazor)
└── Tests/
    ├── NavigationTests.cs            # Navigation and redirect tests
    └── Authentication/
        ├── AuthSecurityTests.cs      # Security edge case tests
        ├── LoginTests.cs             # Login flow tests
        ├── RegistrationTests.cs      # Registration flow tests
        └── TwoFactorTests.cs         # 2FA/TOTP tests
```

## Key Components

### E2EFixture (Assembly-Level Setup)

Starts once per test run:
- PostgreSQL container via Testcontainers
- Playwright browser installation

```csharp
[SetUpFixture]
public class E2EFixture
{
    public static string BaseConnectionString { get; private set; }
    public static IPlaywright Playwright { get; private set; }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Start PostgreSQL container
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:18")
            .WithCleanUp(true)
            .Build();
        await _container.StartAsync();

        // Initialize Playwright
        _playwright = await Playwright.CreateAsync();
    }
}
```

### TestWebApplicationFactory

Per-test factory that:
- Creates isolated database for each test
- Configures Kestrel via `UseKestrel(0)`
- Replaces external services with stubs (email, Telegram bot)
- Cleans up database on dispose

### E2ETestBase

Base class providing:
- Browser context setup/teardown
- Screenshot capture on test failure
- Navigation helpers
- URL assertion helpers

## Database Isolation

Each test gets a unique database:

```csharp
// In TestWebApplicationFactory
private readonly string _databaseName = $"e2e_test_{Guid.NewGuid():N}";

// Created before test runs
using var cmd = connection.CreateCommand();
cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";

// Dropped after test completes
cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
```

## Running Tests

### Prerequisites (One-Time Setup)

1. **Install PowerShell** (required by Playwright on macOS):
   ```bash
   brew install powershell/tap/powershell
   ```

2. **Install Playwright browsers** (after building the test project):
   ```bash
   pwsh TelegramGroupsAdmin.E2ETests/bin/Debug/net10.0/playwright.ps1 install
   ```

### Running Tests

```bash
# Run all E2E tests
dotnet test TelegramGroupsAdmin.E2ETests

# Run with detailed output
dotnet test TelegramGroupsAdmin.E2ETests --logger "console;verbosity=detailed"

# Run specific test
dotnet test TelegramGroupsAdmin.E2ETests --filter "NavigationTests"
```

### Debugging

Set `Headless = false` in `E2ETestBase.cs` to see the browser:

```csharp
Browser = await E2EFixture.Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = false  // Set to false for debugging
});
```

## Stubbed Services

### Email Service

`TestEmailService` captures sent emails without actually sending them:

```csharp
// In test
EmailService.Clear();  // Clear before test
// ... trigger email send ...
var emails = EmailService.GetEmailsTo("user@example.com");
Assert.That(emails, Has.Count.EqualTo(1));
```

### Background Services Philosophy

Background services (Telegram bot, Quartz scheduler, etc.) are **not removed** during E2E tests. The test philosophy is:

> Override ONLY what's necessary for testing. Let the real app run as-is.

This means:
- **Bot service**: Runs but stays inactive (no bot token configured in test DB)
- **Quartz jobs**: Run normally but are harmless against isolated test database
- **Other services**: Function as in production

This approach catches integration issues that might be hidden by heavy mocking.

## Writing Tests

### Basic Test Pattern

```csharp
[TestFixture]
public class MyTests : E2ETestBase
{
    [Test]
    public async Task MyTest()
    {
        // Arrange - database is fresh and empty

        // Act
        await NavigateToAsync("/some-page");

        // Assert
        await WaitForUrlAsync("**/expected-url**");
        await AssertUrlAsync("/expected-url");
    }
}
```

### Creating Test Users

Use `TestUserBuilder` to create users with specific states:

```csharp
// Generate secure test credentials
var email = TestCredentials.GenerateEmail("mytest");
var password = TestCredentials.GeneratePassword();

// Create a verified user with TOTP enabled
var user = await new TestUserBuilder(Factory.Services)
    .WithEmail(email)
    .WithPassword(password)
    .WithEmailVerified()
    .WithTotpEnabled()  // Returns user with TotpSecret for test code generation
    .BuildAsync();

// Generate valid TOTP code for login
var totpCode = TotpService.GenerateCode(user.TotpSecret!);
```

Available builder methods:
- `.WithEmail(email)` - Set email address
- `.WithPassword(password)` - Set password (hashed automatically)
- `.WithEmailVerified()` - Mark email as verified
- `.WithTotpEnabled()` - Enable 2FA (secret returned in `TestUser.TotpSecret`)
- `.WithPermissionLevel(level)` - Set permission level (Admin, GlobalAdmin, Owner)
- `.WithStatus(status)` - Set user status (Active, Disabled, etc.)

## References

- [.NET 10 UseKestrel() API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0)
- [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [Playwright .NET Documentation](https://playwright.dev/dotnet/)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)

## Related GitHub Issues

- [Decouple WebApplicationFactory and TestServer #33846](https://github.com/dotnet/aspnetcore/issues/33846) - Closed in .NET 10
- [Add UseKestrel() API #60758](https://github.com/dotnet/aspnetcore/issues/60758)

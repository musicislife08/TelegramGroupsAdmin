# Code Smells Reference Guide

A comprehensive reference for identifying and remediating code smells in the TelegramGroupsAdmin project. This guide is tailored for **homelab context** - prioritizing simplicity, maintainability, and practical security over enterprise-scale patterns.

## Philosophy

- **Simpler is better** for single-maintainer homelab apps
- Many "best practices" are designed for enterprise scale or library authors
- **Security updates > deterministic builds**
- Trusted users reduce some security concerns
- Avoid premature optimization and over-engineering

---

## Table of Contents

1. [Classic Code Smells](#1-classic-code-smells)
2. [SOLID Principle Violations](#2-solid-principle-violations)
3. [Async/Await Anti-Patterns](#3-asyncawait-anti-patterns)
4. [Entity Framework Core Anti-Patterns](#4-entity-framework-core-anti-patterns)
5. [Dependency Injection Anti-Patterns](#5-dependency-injection-anti-patterns)
6. [Blazor Server Anti-Patterns](#6-blazor-server-anti-patterns)
7. [Exception Handling Anti-Patterns](#7-exception-handling-anti-patterns)
8. [Memory/Performance Anti-Patterns](#8-memoryperformance-anti-patterns)
9. [Testing Anti-Patterns](#9-testing-anti-patterns)
10. [Security Smells](#10-security-smells)
11. [Docker/Dockerfile Anti-Patterns](#11-dockerdockerfile-anti-patterns)

---

## 1. Classic Code Smells

*Based on Martin Fowler's "Refactoring" and Refactoring Guru*

### Bloaters

Code that has grown too large to work with effectively.

| Smell | Description | Why It's Bad | Detection |
|-------|-------------|--------------|-----------|
| **Long Method** | Methods > 20-30 lines | Hard to understand, test, and maintain | Manual review, IDE metrics |
| **Large Class** | Classes with too many responsibilities | Violates SRP, hard to modify | Line count, method count |
| **Primitive Obsession** | Using primitives instead of small objects | Lost type safety, scattered validation | `string email` instead of `Email` |
| **Long Parameter List** | Methods with > 3-4 parameters | Hard to call correctly, order confusion | Parameter count |
| **Data Clumps** | Groups of data that appear together repeatedly | Indicates missing abstraction | Repeated parameter groups |

### Object-Orientation Abusers

Incomplete or incorrect application of OO principles.

| Smell | Description | Why It's Bad | Detection |
|-------|-------------|--------------|-----------|
| **Switch Statements** | Type-based switching that could be polymorphism | Adding new types requires modification | `switch` on type/enum |
| **Refused Bequest** | Subclass doesn't use inherited members | Indicates wrong inheritance hierarchy | Unused base members |
| **Temporary Field** | Fields only set in certain circumstances | Confusing, null checks everywhere | Fields that are often null |

### Change Preventers

Issues that make changes ripple through the codebase.

| Smell | Description | Why It's Bad | Detection |
|-------|-------------|--------------|-----------|
| **Divergent Change** | One class changed for multiple unrelated reasons | SRP violation | Git history analysis |
| **Shotgun Surgery** | One change requires editing many classes | High coupling | Impact analysis |

### Dispensables

Code that serves no purpose and should be removed.

| Smell | Description | Why It's Bad | Detection |
|-------|-------------|--------------|-----------|
| **Duplicate Code** | Same code in multiple places | Bug fixes missed in copies | Code similarity tools |
| **Dead Code** | Unreachable or unused code | Confusion, maintenance burden | IDE warnings, coverage |
| **Lazy Class** | Class that doesn't do enough | Unnecessary abstraction | Low method/field count |
| **Speculative Generality** | Unused abstractions "for the future" | Complexity without benefit | Unused interfaces/params |
| **Excessive Comments** | Comments explaining unclear code | Code should be self-documenting | Comment density |

### Couplers

Excessive coupling between classes.

| Smell | Description | Why It's Bad | Detection |
|-------|-------------|--------------|-----------|
| **Feature Envy** | Method uses another class's data more than its own | Misplaced responsibility | External field access |
| **Inappropriate Intimacy** | Classes too involved with each other's internals | Tight coupling | Friend classes, internal access |
| **Message Chains** | Long chains like `a.B().C().D()` | Coupling to structure | Chain length |
| **Middle Man** | Class that only delegates | Unnecessary indirection | Methods that only forward |

### References
- [Martin Fowler - Refactoring](https://martinfowler.com/books/refactoring.html)
- [Refactoring Guru - Code Smells](https://refactoring.guru/refactoring/smells)

---

## 2. SOLID Principle Violations

### Single Responsibility Principle (SRP)

> "A class should have only one reason to change."

| Violation | Example | Fix |
|-----------|---------|-----|
| **God Class** | Class handling UI, business logic, and data access | Extract responsibilities to separate classes |
| **Mixed Concerns** | Repository that also sends emails | Separate into focused services |

### Open/Closed Principle (OCP)

> "Open for extension, closed for modification."

| Violation | Example | Fix |
|-----------|---------|-----|
| **Type Switching** | `if (type == "A") ... else if (type == "B")` | Use polymorphism or strategy pattern |
| **Modification Required** | Adding new feature requires changing existing class | Use abstractions and composition |

**Note**: For small, stable type sets, simple switch statements are acceptable.

### Liskov Substitution Principle (LSP)

> "Subtypes must be substitutable for their base types."

| Violation | Example | Fix |
|-----------|---------|-----|
| **NotImplementedException** | Derived class throws for inherited method | Redesign inheritance hierarchy |
| **Explicit Type Casting** | `if (animal is Dog dog)` in base handler | Use proper polymorphism |
| **Weakened Postconditions** | Derived method returns less than promised | Honor base contract |

### Interface Segregation Principle (ISP)

> "Clients should not depend on interfaces they don't use."

| Violation | Example | Fix |
|-----------|---------|-----|
| **Fat Interface** | `IRepository` with 20 methods | Split into focused interfaces |
| **Empty Implementations** | Methods that throw or return null | Remove unused interface members |

### Dependency Inversion Principle (DIP)

> "Depend on abstractions, not concretions."

| Violation | Example | Fix |
|-----------|---------|-----|
| **Direct `new`** | `var service = new EmailService()` in business logic | Inject via constructor |
| **Static Cling** | Heavy use of static utility classes | Inject dependencies |
| **Concrete Dependencies** | Constructor takes `SqlRepository` not `IRepository` | Depend on interfaces |

**Note**: Direct `new` is fine for value objects, DTOs, and simple data structures.

### References
- [Microsoft - SOLID Violations](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/may/csharp-best-practices-dangers-of-violating-solid-principles-in-csharp)
- [JetBrains - Code Review SOLID](https://blog.jetbrains.com/upsource/2015/08/31/what-to-look-for-in-a-code-review-solid-principles-2/)

---

## 3. Async/Await Anti-Patterns

*Based on Stephen Cleary and Mark Heath's guidance*

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **`async void`** | Exceptions crash the app, can't be awaited | Use `async Task` (except event handlers) |
| **`.Result` / `.Wait()`** | Deadlocks in ASP.NET/Blazor context | Use `await` instead |
| **Fire-and-Forget** | Lost exceptions, no error handling | Use `Task.Run` with error handling or background service |
| **Not Awaiting Tasks** | Same as fire-and-forget, compiler warning ignored | Always await or explicitly handle |
| **Sync over Async** | Blocks thread pool, potential deadlock | Async all the way down |

### Detection Patterns

```csharp
// BAD: async void
async void OnButtonClick() { }  // Only OK for event handlers

// BAD: Blocking
var result = GetDataAsync().Result;  // Deadlock risk
task.Wait();  // Deadlock risk

// BAD: Fire and forget
_ = DoWorkAsync();  // Exception lost

// BAD: Not awaiting
DoWorkAsync();  // Compiler warning CS4014
```

### Not Applicable to This Project

- **`ConfigureAwait(false)`** - This is for library authors. Our projects all run in the same application context.
- **Async over sync** (`Task.Run` wrapping sync code) - Low priority at our scale.

### References
- [Stephen Cleary - Async Best Practices](https://blog.stephencleary.com/)
- [Mark Heath - Async Antipatterns](https://markheath.net/post/async-antipatterns)
- [Microsoft - Async/Await Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)

---

## 4. Entity Framework Core Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **N+1 Query Problem** | Executes N additional queries in a loop | Use `.Include()` for eager loading |
| **Cartesian Explosion** | Multiple `Include()` creates huge result sets | Use `AsSplitQuery()` or separate queries |
| **No `AsNoTracking()`** | Tracks changes for read-only data | Add `.AsNoTracking()` for queries |
| **Premature `.ToList()`** | Loads all data before filtering | Filter before materializing |
| **Fetching Entire Entities** | Loads all columns when few needed | Use `.Select()` with projections |
| **Missing Indexes** | Full table scans on filtered columns | Add indexes for WHERE columns |
| **Lazy Loading in Loops** | Silent N+1 from navigation access | Explicit loading or Include |
| **No Bulk Operations** | `SaveChanges()` in loops | Use EFCore.BulkExtensions |
| **Ignoring Generated SQL** | Hidden performance issues | Log and review SQL |

### Detection Examples

```csharp
// BAD: N+1
foreach (var order in orders)
{
    var items = order.Items;  // Query per order!
}

// GOOD: Eager load
var orders = await _context.Orders
    .Include(o => o.Items)
    .ToListAsync();

// BAD: Premature materialization
var result = _context.Users
    .ToList()  // Loads ALL users
    .Where(u => u.IsActive);  // Filters in memory

// GOOD: Filter first
var result = await _context.Users
    .Where(u => u.IsActive)  // SQL WHERE
    .ToListAsync();

// BAD: Fetching entire entity
var names = await _context.Users.ToListAsync();
return names.Select(u => u.Name);

// GOOD: Project what you need
var names = await _context.Users
    .Select(u => u.Name)
    .ToListAsync();
```

### References
- [EF Core Query Anti-Patterns](https://www.woodruff.dev/debugging-entity-framework-core-8-real-world-query-anti-patterns-and-how-to-fix-them/)
- [The Reformed Programmer - EF Best Practices](https://www.thereformedprogrammer.net/six-ways-to-build-better-entity-framework-core-and-ef6-applications/)

---

## 5. Dependency Injection Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Captive Dependency** | Singleton holds scoped/transient | Use `IServiceScopeFactory` |
| **Service Locator** | Injecting `IServiceProvider` directly | Inject specific dependencies |
| **Lifetime Mismatch** | Scoped injected into singleton | Match or use scope factory |
| **Disposable Transient Leak** | Container holds transient IDisposables | Use factory pattern |
| **Async DI Factory Deadlock** | `.Result` in DI factory | Avoid async factories or use sync |
| **Constructor Over-Injection** | > 5 constructor parameters | Split class (SRP violation) |

### Captive Dependency Example

```csharp
// BAD: Singleton captures scoped
public class MySingleton  // Singleton
{
    private readonly IScopedService _scoped;  // Scoped - CAPTURED!

    public MySingleton(IScopedService scoped)
    {
        _scoped = scoped;  // Lives forever now
    }
}

// GOOD: Create scope when needed
public class MySingleton
{
    private readonly IServiceScopeFactory _factory;

    public MySingleton(IServiceScopeFactory factory)
    {
        _factory = factory;
    }

    public async Task DoWork()
    {
        using var scope = _factory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<IScopedService>();
        // Use scoped service
    }
}
```

### Context-Dependent: Service Locator

Using `IServiceProvider` is acceptable in:
- Quartz.NET job factories
- Generic factory patterns
- Middleware that needs per-request services

Flag and review case-by-case.

### References
- [Microsoft - DI Guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [Jimmy Bogard - Service Lifetimes](https://www.jimmybogard.com/choosing-a-servicelifetime/)

---

## 6. Blazor Server Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Poor State Management** | Local variables reset on re-render | Use cascading parameters, services |
| **Mixed UI and Logic** | Business logic in .razor files | Extract to services, use code-behind |
| **Missing Async/Await** | Blocks UI thread | Use async for I/O operations |
| **Unnecessary Re-renders** | Performance degradation | Implement `ShouldRender()` |
| **Large Lists** | Memory/performance issues | Use `Virtualize` component |
| **Missing `@key`** | Inefficient diff algorithm | Add `@key` for dynamic lists |
| **Missing Error Boundaries** | Errors crash entire circuit | Wrap with `<ErrorBoundary>` |
| **No Try/Catch in Async** | Silent failures | Handle exceptions explicitly |

### Large .razor Files

When a .razor file gets too large, it usually indicates the component is doing too much. Prefer:
1. **Split into smaller components** (best solution)
2. Code-behind file (`.razor.cs`) as fallback

### Detection Examples

```razor
@* BAD: Logic in component *@
@code {
    private async Task LoadData()
    {
        // 50 lines of business logic...
    }
}

@* GOOD: Delegate to service *@
@inject IDataService DataService
@code {
    private async Task LoadData()
    {
        _data = await DataService.GetDataAsync();
    }
}

@* BAD: Large list without virtualization *@
@foreach (var item in _items)  // 10,000 items!
{
    <ItemComponent Item="@item" />
}

@* GOOD: Virtualized *@
<Virtualize Items="_items" Context="item">
    <ItemComponent Item="@item" />
</Virtualize>
```

### References
- [Microsoft - Blazor Performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/)
- [Microsoft - Blazor Rendering](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering)

---

## 7. Exception Handling Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Swallowing Exceptions** | Bugs disappear silently | Log or rethrow |
| **Generic `catch`** | Catches too much (OOM, etc.) | Catch specific exceptions |
| **`throw ex`** | Loses stack trace | Use `throw` alone |
| **Exceptions for Flow Control** | Performance, clarity | Use conditionals |
| **Not Logging Exception Object** | Loses stack trace, inner exception | Log full exception |
| **Log and Throw** | Double logging | Do one or the other |

### Generic Exception Handling

Catching `Exception` is acceptable at **boundary handlers**:
- ASP.NET middleware
- Background job top-level
- Event handlers

Flag non-boundary generic catches only.

### Detection Examples

```csharp
// BAD: Swallowing
try { DoWork(); }
catch { }  // Bug hidden forever

// BAD: throw ex
try { DoWork(); }
catch (Exception ex)
{
    throw ex;  // Stack trace lost!
}

// GOOD: throw
try { DoWork(); }
catch (Exception ex)
{
    _logger.LogError(ex, "DoWork failed");
    throw;  // Preserves stack trace
}

// BAD: Only logging message
catch (Exception ex)
{
    _logger.LogError("Error: " + ex.Message);  // No stack trace!
}

// GOOD: Log full exception
catch (Exception ex)
{
    _logger.LogError(ex, "Operation failed");  // Full context
}

// BAD: Exception for flow control
try { return int.Parse(input); }
catch { return 0; }

// GOOD: TryParse
if (int.TryParse(input, out var result))
    return result;
return 0;
```

### References
- [Matt Eland - Exception Anti-Patterns](https://newdevsguide.com/2022/11/06/exception-anti-patterns-in-csharp/)
- [Microsoft - Exception Best Practices](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)

---

## 8. Memory/Performance Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Not Pre-sizing Collections** | Array resizing overhead | Provide capacity when known |
| **String Concat in Loops** | O(n²) allocations | Use `StringBuilder` |
| **Boxing Value Types** | Unnecessary heap allocations | Use generics, avoid `object` |
| **Holding References Too Long** | Prevents garbage collection | Null out or use `using` |
| **Improper HttpClient** | Socket exhaustion | Use `IHttpClientFactory` |
| **Busy Front End** | UI freezes | Offload to background |

### Pre-sizing Collections

Only flag when:
- Collection size is known upfront
- Collection could be large (100+ items)

```csharp
// BAD: Unknown resizing
var list = new List<int>();
for (int i = 0; i < 10000; i++)
    list.Add(i);

// GOOD: Pre-sized
var list = new List<int>(10000);
for (int i = 0; i < 10000; i++)
    list.Add(i);
```

### Boxing Detection

```csharp
// BAD: Boxing
int value = 42;
object boxed = value;  // Boxing
int unboxed = (int)boxed;  // Unboxing

// BAD: Interface on struct
struct MyStruct : IComparable { }
IComparable comparable = new MyStruct();  // Boxing

// GOOD: Use generics
List<int> numbers;  // No boxing
```

### References
- [Criteo - Memory Anti-Patterns](https://medium.com/criteo-engineering/memory-anti-patterns-in-c-7bb613d55cf0)

---

## 9. Testing Anti-Patterns

Focus on quality issues that make tests unreliable.

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Excessive Mocking** | Tests verify mocks, not code | Mock boundaries only |
| **Shared State** | Tests affect each other | Isolate test fixtures |
| **Flaky Tests** | Non-deterministic results | Fix timing, use deterministic data |
| **Structural Inspection** | Tests break on refactor | Test behavior, not structure |

### Detection Examples

```csharp
// BAD: Excessive mocking - testing nothing
var mockA = new Mock<IA>();
var mockB = new Mock<IB>();
var mockC = new Mock<IC>();
mockA.Setup(x => x.GetData()).Returns(mockB.Object);
mockB.Setup(x => x.Process()).Returns(mockC.Object);
// What are we actually testing?

// BAD: Shared state
private static List<string> _testData = new();  // Shared!

[Test]
public void Test1() { _testData.Add("a"); }

[Test]
public void Test2() { Assert.That(_testData.Count, Is.EqualTo(0)); }  // Fails if Test1 runs first

// GOOD: Isolated
[SetUp]
public void Setup()
{
    _testData = new List<string>();  // Fresh each test
}
```

### References
- [Test Smells](https://testsmells.org/)
- [xUnit Patterns - Test Smells](http://xunitpatterns.com/TestSmells.html)

---

## 10. Security Smells

| Smell | Why It's Bad | Fix |
|-------|--------------|-----|
| **Hardcoded Secrets** | Exposed in source control | Use environment vars, secrets manager |
| **SQL Injection** | Data breach, data loss | Use parameterized queries |
| **Missing Input Validation** | Exploitation at boundaries | Validate user input |
| **Information Disclosure** | Leaks internal details | Generic error messages |

### Detection Examples

```csharp
// BAD: Hardcoded secret
var apiKey = "sk-abc123...";

// BAD: SQL injection
var query = $"SELECT * FROM users WHERE name = '{userInput}'";

// GOOD: Parameterized (EF Core does this automatically)
await _context.Users.Where(u => u.Name == userInput).ToListAsync();
```

### Not Applicable (Homelab Context)
- **CORS**: Local network, trusted users
- **Rate limiting**: Single instance, not public-facing

### References
- [OWASP Top 10](https://owasp.org/www-project-top-ten/)

---

## 11. Docker/Dockerfile Anti-Patterns

### Image Selection

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Wrong Base Image** | SDK image in production | Use `aspnet` for runtime |
| **SDK in Production** | 700MB+ image, security surface | Multi-stage build |

### Build Optimization

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **No Multi-Stage Build** | Huge images with build tools | Separate build and runtime stages |
| **No .dockerignore** | Slow builds, bloated context | Exclude bin, obj, .git |
| **Cache Invalidation** | `COPY . .` before restore | Copy csproj first, then restore |

### Security

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **Running as Root** | Container escape risk | Add `USER` directive |
| **Secrets in Dockerfile** | Exposed in image layers | Use env vars, secrets |

### Runtime

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| **No HEALTHCHECK** | Orchestrator can't detect failures | Add HEALTHCHECK instruction |
| **Wrong Ports** | ASPNETCORE_URLS mismatch | Configure consistently |

### Proper Multi-Stage Dockerfile

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["MyApp.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Security: Don't run as root
USER $APP_UID

COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MyApp.dll"]
```

### Not Applicable (Homelab Context)
- **`latest` tag**: Prefer automatic security updates over determinism
- **SHA digests**: Major.minor pinning is sufficient
- **Multiple processes**: Intentional singleton design
- **Resource limits**: Single instance, not shared cluster

### References
- [Microsoft - Containerize .NET](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)
- [Docker Best Practices](https://docs.docker.com/build/building/best-practices/)
- [Snyk - Docker Security](https://snyk.io/blog/10-docker-image-security-best-practices/)

---

## Quick Reference: Detection Commands

```bash
# Async void (except event handlers)
grep -rn "async void" --include="*.cs" | grep -v "EventHandler\|_Click\|_Changed"

# Blocking calls
grep -rn "\.Result\|\.Wait()" --include="*.cs"

# Empty catch blocks
grep -rn "catch.*{[\s]*}" --include="*.cs"

# Generic exception (review for non-boundary)
grep -rn "catch (Exception" --include="*.cs"

# throw ex (loses stack trace)
grep -rn "throw ex;" --include="*.cs"

# Hardcoded connection strings
grep -rn "Server=\|Data Source=" --include="*.cs"

# Missing AsNoTracking (in repositories)
grep -rn "\.ToList\|\.FirstOrDefault\|\.SingleOrDefault" --include="*Repository.cs" | grep -v "AsNoTracking"
```

---

## Summary Table

| Category | Key Smells | Homelab Priority |
|----------|-----------|-----------------|
| Classic | Long Method, Duplicate Code, Dead Code | High |
| SOLID | God Classes, NotImplementedException | Medium |
| Async | async void, .Result/.Wait() | High |
| EF Core | N+1, Missing AsNoTracking | High |
| DI | Captive Dependency, Lifetime Mismatch | High |
| Blazor | Missing Async, Error Handling | Medium |
| Exceptions | Swallowing, throw ex | High |
| Performance | Boxing, String Concat in Loops | Medium |
| Testing | Flaky Tests, Shared State | Medium |
| Security | Hardcoded Secrets, SQL Injection | High |
| Docker | Running as Root, No HEALTHCHECK | Medium |

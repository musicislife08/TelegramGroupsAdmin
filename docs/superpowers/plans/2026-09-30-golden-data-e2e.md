# Golden Data for E2E Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** New Playwright E2E tests run against a fresh per-test clone of the canonical golden dataset (shaped by reduce/mutate), using a golden library shared with the integration tests, and the E2E coverage gaps found in the web-first sweep are closed with such tests.

**Architecture:** Extract the NUnit-free golden infrastructure from `TelegramGroupsAdmin.IntegrationTests/TestData` into a new class library `TelegramGroupsAdmin.Testing.Golden` (plus a `GoldenTemplates` build/clone/drop helper). Integration tests consume it unchanged in behaviour. The E2E fixture builds `empty_template` / `golden_template` once per run with a shared data-protection key ring; a new `GoldenE2ETestBase` clones a template per test, runs the test's reduce/mutate arrangement, then starts a fresh app on the clone and logs in as canonical users by cookie injection.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL 18 (Testcontainers), NUnit 4, Playwright .NET 1.62, MudBlazor 9.9, ASP.NET Core Data Protection.

**Spec:** `docs/superpowers/specs/2026-09-30-golden-data-e2e-design.md`

## Global Constraints

- Branch `test/e2e-web-first-assertions` (already checked out). Never create git worktrees. Never push; never open a PR.
- Conventional commits via heredoc (`git commit -F- <<'EOF'`), one self-contained commit per task (or per area in Task 7+), each ending with the `Co-Authored-By:` line for the model that wrote it. Prefer new commits over amending.
- `TreatWarningsAsErrors` is on in every test project; builds must be 0 warnings / 0 errors.
- E2E: one-shot Playwright reads are banned (RS0030, `TelegramGroupsAdmin.E2ETests/BannedSymbols.txt`); assert with `Expect(...)`; absence checks follow a presence check on the same render; `ToHaveTextAsync`/`ToContainTextAsync` ignore `RegexOptions.IgnoreCase` (pass `new() { IgnoreCase = true }`); interactive pages wait with `page.WaitForInteractiveAsync()`, never NetworkIdle or fixed sleeps.
- Data rule for every NEW test (`.claude/rules/integration-test-data.md`): canonical as-is → reduce/mutate in `ArrangeDataAsync` (add a missing reduce/mutate op to the builders, with its own self-test) → flag-edit an unreferenced canonical row (pin in `GoldenDatasetConstants`, recipe in `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` Part 2 marked "(canonical edit 2026-09-30)", read-back guard) → app write only when it is the assertion subject. No `Test*Builder`, no raw INSERT, no SUT write as setup.
- Existing E2E tests keep their builders and base classes; only behaviour-preserving changes to shared infrastructure (Task 4 adds one virtual hook to `E2ETestBase`; spec's "no diff" is relaxed to "no behaviour change" to avoid duplicating the browser/trace lifecycle).
- NSubstitute matchers use `x!.Prop`, never `?.`.
- Canonical data contains scrubbed real values: never paste canonical message text, usernames or chat titles into commit messages, docs or GitHub text; reference ids/constants.
- Test commands: integration `dotnet test TelegramGroupsAdmin.IntegrationTests` (~60 s); E2E `dotnet test TelegramGroupsAdmin.E2ETests` (~5–6 min, run in background or with a long timeout); targeted `--filter "FullyQualifiedName~<Class>"`.

## Review Focus

1. **Template clone while a session holds the template** — Postgres rejects `CREATE DATABASE … TEMPLATE` if any session is connected to the template; every template-touching connection must use `Pooling=false` and close before cloning. Pinned by Task 4's two-tests-in-a-row isolation test (second clone succeeds).
2. **App cannot decrypt canonical secrets** — if the E2E template is built with a different key ring than the app uses, `api_keys` silently decrypts to null (logged, not thrown). Pinned by Task 4's key-ring smoke test asserting the AI provider reads as configured.
3. **SetUp fails midway (arrange throws, server fails to start)** — TearDown must still dispose what exists and drop the clone so later tests and the container are not polluted. Pinned by Task 4's TearDown null-safety and a test that throws from `ArrangeDataAsync` under `Assert.ThrowsAsync`-style harness check (see Task 4 Step 6).
4. **Frozen canonical timestamps** — date-windowed pages render "no data" against unshifted canonical; a smoke test asserting a dashboard number would pass/fail by date. Pinned by Task 4's dashboard smoke using `Mutate` re-time and asserting a runtime-read count.
5. **Integration behaviour drift from the move** — namespace/resource-name mistakes surface as missing embedded resources or a different canonical load. Pinned by Task 1/2's before/after integration suite counts and `LoadCanonicalAsyncTests`.

---

### Task 1: Extract the golden library

**Files:**
- Create: `TelegramGroupsAdmin.Testing.Golden/TelegramGroupsAdmin.Testing.Golden.csproj`
- Move (git mv) from `TelegramGroupsAdmin.IntegrationTests/TestData/` to `TelegramGroupsAdmin.Testing.Golden/`: `GoldenDataset.cs`, `GoldenReducePlanBuilder.cs`, `ChildReducePlan.cs`, `GoldenMutatePlanBuilder.cs`, `TimestampShift.cs`, `GoldenReducePlanException.cs`, `GoldenDatasetConstants.cs`, `SQL/` (whole directory, including `tools/`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj`, `TelegramGroupsAdmin.sln`
- Keep in IntegrationTests: `TestData/CanonicalSlices.cs`, `TestData/Tests/*`

**Interfaces:**
- Produces: namespace `TelegramGroupsAdmin.Testing.Golden` with public `GoldenDataset` (`LoadCanonicalAsync(AppDbContext, IDataProtectionProvider, CancellationToken)`, `Reduce(AppDbContext)`, `Mutate(AppDbContext)`), `GoldenReducePlanBuilder`, `ChildReducePlan`, `GoldenMutatePlanBuilder`, `TimestampShift`, `GoldenReducePlanException`, and **public** `GoldenDatasetConstants` (was `internal`). `GoldenReducePlanState` and builder constructors stay `internal`.

- [ ] **Step 1: Record the baseline**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -3`
Expected: `Passed! - Failed: 0, Passed: N` — write N down (≈887).

- [ ] **Step 2: Create the project**

```xml
<!-- TelegramGroupsAdmin.Testing.Golden/TelegramGroupsAdmin.Testing.Golden.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- EF1002: ExecuteSqlRawAsync SQL injection warning - all SQL here is hardcoded test fixture text -->
    <NoWarn>$(NoWarn);EF1002</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TelegramGroupsAdmin.Data\TelegramGroupsAdmin.Data.csproj" />
    <ProjectReference Include="..\TelegramGroupsAdmin.Core\TelegramGroupsAdmin.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Canonical SQL fixtures (canonical/*.sql is FK-ordered by filename prefix). -->
    <EmbeddedResource Include="SQL\**\*.sql" Exclude="SQL\tools\**" />
  </ItemGroup>

</Project>
```

If `Microsoft.AspNetCore.DataProtection` does not resolve transitively through Data, add `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.

Run: `dotnet sln TelegramGroupsAdmin.sln add TelegramGroupsAdmin.Testing.Golden/TelegramGroupsAdmin.Testing.Golden.csproj`

- [ ] **Step 3: Move the files**

```bash
cd TelegramGroupsAdmin.IntegrationTests/TestData
for f in GoldenDataset.cs GoldenReducePlanBuilder.cs ChildReducePlan.cs GoldenMutatePlanBuilder.cs TimestampShift.cs GoldenReducePlanException.cs GoldenDatasetConstants.cs SQL; do
  git mv "$f" "../../TelegramGroupsAdmin.Testing.Golden/$f"
done
cd ../..
```

- [ ] **Step 4: Fix namespace, visibility and resource prefix**

In every moved `.cs` file: `namespace TelegramGroupsAdmin.IntegrationTests.TestData;` → `namespace TelegramGroupsAdmin.Testing.Golden;`.
In `GoldenDatasetConstants.cs`: `internal static class GoldenDatasetConstants` → `public static class GoldenDatasetConstants`.
In `GoldenDataset.LoadCanonicalSqlScriptAsync`, replace the hardcoded prefix:

```csharp
        var assembly = typeof(GoldenDataset).Assembly;
        var resourceName = $"{assembly.GetName().Name}.{scriptPath}";
```

- [ ] **Step 5: Point IntegrationTests at the library**

In `TelegramGroupsAdmin.IntegrationTests.csproj`: remove the `<EmbeddedResource Include="TestData\SQL\**\*.sql" … />` item group; add
```xml
    <ProjectReference Include="..\TelegramGroupsAdmin.Testing.Golden\TelegramGroupsAdmin.Testing.Golden.csproj" />
```
and, next to `<Using Include="NUnit.Framework" />`:
```xml
    <Using Include="TelegramGroupsAdmin.Testing.Golden" />
```
`CanonicalSlices` and `TestData/Tests/*` keep namespace `TelegramGroupsAdmin.IntegrationTests.TestData`; the global using covers every consumer. Remove now-redundant `using TelegramGroupsAdmin.IntegrationTests.TestData;` lines only where the file no longer uses `CanonicalSlices` (the compiler won't flag unused usings; leaving them is harmless — do not churn files for it).

- [ ] **Step 6: Build and run**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests -v q 2>&1 | grep -E 'error|Warn|Error\(s\)'` → `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -3` → `Passed: N` (same N as Step 1, `Failed: 0`).

- [ ] **Step 7: Commit**

```bash
git add -A TelegramGroupsAdmin.Testing.Golden TelegramGroupsAdmin.IntegrationTests TelegramGroupsAdmin.sln
git commit -F- <<'EOF'
refactor(tests): extract the golden dataset into TelegramGroupsAdmin.Testing.Golden

Moves GoldenDataset, the reduce/mutate builders, constants and canonical SQL
into an NUnit-free library so E2E tests can share them. Embedded resource names
derive from the library assembly. Integration behaviour is unchanged (same pass
count before and after).

Co-Authored-By: <model line>
EOF
```

---

### Task 2: Shared template build / clone / drop

**Files:**
- Create: `TelegramGroupsAdmin.Testing.Golden/GoldenTemplate.cs`, `TelegramGroupsAdmin.Testing.Golden/GoldenTemplates.cs`
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/GoldenTemplatesTests.cs`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Fixtures/PostgresFixture.cs` (replace `BuildEmptyTemplateAsync`/`BuildGoldenTemplateAsync` bodies with one call), `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs` (`CreateDatabaseFromEmptyTemplateAsync`, `CreateDatabaseFromGoldenTemplateAsync`, `DropDatabaseAsync` delegate to `GoldenTemplates`)

**Interfaces:**
- Produces:
  - `public enum GoldenTemplate { Empty, Golden }`
  - `public static class GoldenTemplates`
    - `public const string EmptyTemplateName = "empty_template";`
    - `public const string GoldenTemplateName = "golden_template";`
    - `public static Task BuildAsync(string baseConnectionString, IDataProtectionProvider dataProtection, CancellationToken ct = default)`
    - `public static Task CloneAsync(string baseConnectionString, GoldenTemplate template, string databaseName, CancellationToken ct = default)`
    - `public static Task DropAsync(string baseConnectionString, string databaseName, CancellationToken ct = default)`
    - `public static string ConnectionStringFor(string baseConnectionString, string databaseName)`

- [ ] **Step 1: Write the failing test**

```csharp
// TelegramGroupsAdmin.IntegrationTests/TestData/Tests/GoldenTemplatesTests.cs
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// GoldenTemplates clone/drop against the session templates PostgresFixture built.
/// </summary>
[TestFixture]
public class GoldenTemplatesTests
{
    [TestCase(GoldenTemplate.Golden, true)]
    [TestCase(GoldenTemplate.Empty, false)]
    public async Task CloneAsync_CopiesTheTemplate(GoldenTemplate template, bool expectUsers)
    {
        var name = $"tmpl_test_{Guid.NewGuid():N}";
        await GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, template, name);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(GoldenTemplates.ConnectionStringFor(PostgresFixture.BaseConnectionString, name))
                .Options;
            await using var ctx = new AppDbContext(options);

            var hasOwner = await ctx.Users.AnyAsync(u => u.Id == GoldenDatasetConstants.WebUsers.OwnerId);

            Assert.That(hasOwner, Is.EqualTo(expectUsers));
        }
        finally
        {
            await GoldenTemplates.DropAsync(PostgresFixture.BaseConnectionString, name);
        }
    }

    [Test]
    public async Task DropAsync_RemovesTheDatabase()
    {
        var name = $"tmpl_test_{Guid.NewGuid():N}";
        await GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, GoldenTemplate.Empty, name);

        await GoldenTemplates.DropAsync(PostgresFixture.BaseConnectionString, name);

        await using var admin = new Npgsql.NpgsqlConnection(
            GoldenTemplates.ConnectionStringFor(PostgresFixture.BaseConnectionString, "postgres"));
        await admin.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = $1", admin);
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = name });
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.Zero);
    }
}
```

(Check the `AppDbContext` users DbSet name with `grep -n 'DbSet<.*User' TelegramGroupsAdmin.Data/AppDbContext.cs` and use it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build TelegramGroupsAdmin.IntegrationTests -v q 2>&1 | grep -E 'error' | head -3`
Expected: `CS0103`/`CS0246` — `GoldenTemplates` / `GoldenTemplate` do not exist.

- [ ] **Step 3: Implement**

```csharp
// TelegramGroupsAdmin.Testing.Golden/GoldenTemplate.cs
namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>Which session template a test database is cloned from.</summary>
public enum GoldenTemplate
{
    /// <summary>Migrated schema, no rows.</summary>
    Empty,

    /// <summary>Migrated schema plus the full canonical dataset.</summary>
    Golden,
}
```

```csharp
// TelegramGroupsAdmin.Testing.Golden/GoldenTemplates.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>
/// Builds the session templates once and clones/drops per-test databases from them.
/// Every connection here uses Pooling=false: Postgres refuses CREATE DATABASE … TEMPLATE
/// while any other session is connected to the template.
/// </summary>
public static class GoldenTemplates
{
    public const string EmptyTemplateName = "empty_template";
    public const string GoldenTemplateName = "golden_template";

    /// <summary>
    /// Creates empty_template (migrated) and golden_template (empty + canonical, encrypted
    /// columns protected with <paramref name="dataProtection"/>), then flags both as templates.
    /// </summary>
    public static async Task BuildAsync(
        string baseConnectionString, IDataProtectionProvider dataProtection, CancellationToken ct = default)
    {
        await ExecuteAdminAsync(baseConnectionString, $"CREATE DATABASE \"{EmptyTemplateName}\"", ct);
        await using (var ctx = CreateContext(baseConnectionString, EmptyTemplateName))
        {
            await ctx.Database.MigrateAsync(ct);
        }
        await FlagAsTemplateAsync(baseConnectionString, EmptyTemplateName, ct);

        await ExecuteAdminAsync(
            baseConnectionString,
            $"CREATE DATABASE \"{GoldenTemplateName}\" TEMPLATE \"{EmptyTemplateName}\"", ct);
        await using (var ctx = CreateContext(baseConnectionString, GoldenTemplateName))
        {
            await GoldenDataset.LoadCanonicalAsync(ctx, dataProtection, ct);
        }
        await FlagAsTemplateAsync(baseConnectionString, GoldenTemplateName, ct);
    }

    /// <summary>Creates <paramref name="databaseName"/> as a copy of the chosen template (~50–150 ms).</summary>
    public static Task CloneAsync(
        string baseConnectionString, GoldenTemplate template, string databaseName, CancellationToken ct = default)
    {
        var source = template == GoldenTemplate.Golden ? GoldenTemplateName : EmptyTemplateName;
        return ExecuteAdminAsync(baseConnectionString, $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{source}\"", ct);
    }

    /// <summary>Terminates sessions on <paramref name="databaseName"/> and drops it if it exists.</summary>
    public static async Task DropAsync(string baseConnectionString, string databaseName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString(baseConnectionString));
        await conn.OpenAsync(ct);

        await using (var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1 AND pid <> pg_backend_pid()",
            conn))
        {
            terminate.Parameters.Add(new NpgsqlParameter { Value = databaseName });
            await terminate.ExecuteNonQueryAsync(ct);
        }

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", conn);
        await drop.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The base connection string retargeted at <paramref name="databaseName"/>.</summary>
    public static string ConnectionStringFor(string baseConnectionString, string databaseName)
        => new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString;

    private static AppDbContext CreateContext(string baseConnectionString, string databaseName)
    {
        var cs = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName, Pooling = false }.ConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }

    private static string AdminConnectionString(string baseConnectionString)
        => new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;

    private static async Task ExecuteAdminAsync(string baseConnectionString, string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString(baseConnectionString));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Task FlagAsTemplateAsync(string baseConnectionString, string databaseName, CancellationToken ct)
        => ExecuteAdminAsync(baseConnectionString, $"UPDATE pg_database SET datistemplate = true WHERE datname = '{databaseName}'", ct);
}
```

Then refactor the integration consumers:

```csharp
// PostgresFixture.GlobalSetup — replace the two Build*TemplateAsync calls (and delete both private methods):
        await GoldenTemplates.BuildAsync(BaseConnectionString, SharedDataProtectionProvider);
```

```csharp
// MigrationTestHelper
    public Task CreateDatabaseFromEmptyTemplateAsync()
        => GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, GoldenTemplate.Empty, _databaseName);

    public Task CreateDatabaseFromGoldenTemplateAsync()
        => GoldenTemplates.CloneAsync(PostgresFixture.BaseConnectionString, GoldenTemplate.Golden, _databaseName);

    private Task DropDatabaseAsync()
        => GoldenTemplates.DropAsync(PostgresFixture.BaseConnectionString, _databaseName);
```
Keep the existing XML doc comments on the two public methods. Delete `BuildAdminConnectionString` if no longer used.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GoldenTemplatesTests"` → 3 passed.
Run: `dotnet test TelegramGroupsAdmin.IntegrationTests 2>&1 | tail -3` → `Passed: N+3`, `Failed: 0`.

- [ ] **Step 5: Commit** (`refactor(tests): share golden template build, clone and drop via GoldenTemplates`)

---

### Task 3: Canonical web-user anchors and doc corrections

**Files:**
- Modify: `TelegramGroupsAdmin.Testing.Golden/GoldenDatasetConstants.cs` (`WebUsers`)
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestData/Tests/LoadCanonicalAsyncTests.cs` (or create `CanonicalWebUserAnchorTests.cs` beside it)
- Modify: `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` (paths to the new library; rerun@/reshoot@ status swap; "bcrypt" → "PBKDF2"; GlobalAdmin anchor recipe)

**Interfaces:**
- Produces in `GoldenDatasetConstants.WebUsers`:
  - `public const string AdminEmail = "admin@example.com";`
  - `public const string GlobalAdminId = "8e3a7211-d0eb-40c6-af8e-7d15bb42d10a";` / `GlobalAdminEmail = "ahead@canonical.test";` (TOTP enabled, no secret)
  - `public const string NoTotpGlobalAdminId = "c2674f3a-16e6-4537-9cbc-a80a0ea9c686";` / `NoTotpGlobalAdminEmail = "machine@canonical.test";`
  - `public const string NoTotpAdminId = "28d7aa41-5be5-43a3-a48e-7b1a4bbe5891";` / `NoTotpAdminEmail = "reshoot@canonical.test";`
  - `public const string SecurityStamp = "TEST_SECURITY_STAMP";`
  - `public const string Password = "Passw0rd!SaidNoSecurityAuditorEver";` (already documented in the class comment; the canonical hash equals the E2E `PrehashedTestCredentials.StandardPasswordHash`)

- [ ] **Step 1: Write the failing test**

```csharp
// TelegramGroupsAdmin.IntegrationTests/TestData/Tests/CanonicalWebUserAnchorTests.cs
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// Pins the canonical web users E2E logs in as: id, email, permission, status, TOTP flag.
/// A canonical regeneration that changes any of these must fail here, not in E2E.
/// </summary>
[TestFixture]
public class CanonicalWebUserAnchorTests
{
    private MigrationTestHelper _db = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = new MigrationTestHelper();
        await _db.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [TestCase(GoldenDatasetConstants.WebUsers.OwnerId, GoldenDatasetConstants.WebUsers.OwnerEmail, 2, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.AdminId, GoldenDatasetConstants.WebUsers.AdminEmail, 0, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.GlobalAdminId, GoldenDatasetConstants.WebUsers.GlobalAdminEmail, 1, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminId, GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminEmail, 1, false)]
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpAdminId, GoldenDatasetConstants.WebUsers.NoTotpAdminEmail, 0, false)]
    public async Task Anchor_HasExpectedShape(string id, string email, int permissionLevel, bool totpEnabled)
    {
        await using var ctx = _db.GetDbContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.Email, Is.EqualTo(email));
            Assert.That((int)user.PermissionLevel, Is.EqualTo(permissionLevel));
            Assert.That((int)user.Status, Is.EqualTo(1), "anchor must be active");
            Assert.That(user.TotpEnabled, Is.EqualTo(totpEnabled));
            Assert.That(user.SecurityStamp, Is.EqualTo(GoldenDatasetConstants.WebUsers.SecurityStamp));
        }
    }
}
```

(Match the entity's property names with `grep -n 'public .* \(Email\|PermissionLevel\|Status\|TotpEnabled\|SecurityStamp\)' TelegramGroupsAdmin.Data/Models/*User*.cs`.)

- [ ] **Step 2: Run to verify it fails** — build error: `GlobalAdminId` etc. not defined.

- [ ] **Step 3: Add the constants** to `WebUsers` with XML docs in the existing style (role, status, TOTP flag, what to use it for — e.g. "use for UI password login; no TOTP prompt"). Fix the class comment "4 hand-picked anchors" to the real count.

- [ ] **Step 4: Run to verify it passes** — `--filter "FullyQualifiedName~CanonicalWebUserAnchorTests"` → 5 passed.

- [ ] **Step 5: Fix `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md`** — `TestData/SQL/canonical` paths → `TelegramGroupsAdmin.Testing.Golden/SQL/canonical`; `GoldenDatasetConstants` location; the web-user recipe table: `rerun@` is deleted (status 3), `reshoot@` is active Admin without TOTP; password hash is PBKDF2; add the GlobalAdmin / no-TOTP anchors. Also update the path in `.claude/rules/integration-test-data.md` (`TelegramGroupsAdmin.IntegrationTests/TestData/SQL/canonical/` → the new location; `TestData/GoldenDatasetConstants.cs` → new location).

- [ ] **Step 6: Commit** (`test(golden): pin canonical web-user login anchors and correct the dataset docs`)

---

### Task 4: E2E golden harness

**Files:**
- Modify: `TelegramGroupsAdmin.E2ETests/TelegramGroupsAdmin.E2ETests.csproj` (reference `TelegramGroupsAdmin.Testing.Golden`)
- Modify: `TelegramGroupsAdmin.E2ETests/Fixtures/E2EFixture.cs` (build templates + shared key ring)
- Modify: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs` (template-clone option, shared keys)
- Modify: `TelegramGroupsAdmin.E2ETests/Fixtures/E2ETestBase.cs` (one virtual hook; no behaviour change)
- Create: `TelegramGroupsAdmin.E2ETests/Fixtures/GoldenE2ETestBase.cs`
- Create: `TelegramGroupsAdmin.E2ETests/Tests/Golden/GoldenHarnessTests.cs`

**Interfaces:**
- Consumes: `GoldenTemplates`, `GoldenTemplate`, `GoldenDataset.Reduce/Mutate`, `GoldenDatasetConstants.WebUsers.*` (Tasks 1–3).
- Produces:
  - `E2EFixture.SharedKeysDirectory` (`static string`) — data-protection key ring the golden template was built with.
  - `TestWebApplicationFactory(string? databaseName = null, bool databaseAlreadyExists = false, string? sharedKeysDirectory = null)` — when `databaseAlreadyExists`, `EnsureDatabaseCreated` skips `CREATE DATABASE`; when `sharedKeysDirectory` is set, its key XML files are copied into `{tempDataPath}/keys` before the host builds.
  - `E2ETestBase`: `protected virtual Task<TestWebApplicationFactory> CreateFactoryAsync() => Task.FromResult(new TestWebApplicationFactory());` called from `BaseSetUp`; `protected virtual Task OnFactoryDisposedAsync() => Task.CompletedTask;` called at the end of `BaseTearDown`.
  - `GoldenE2ETestBase : E2ETestBase` with `protected virtual GoldenTemplate Template => GoldenTemplate.Golden;`, `protected virtual Task ArrangeDataAsync(AppDbContext context) => Task.CompletedTask;`, `protected string DatabaseName`, `protected AppDbContext CreateDbContext()` (direct context on the clone for read-back assertions), and login helpers `LoginAsCanonicalAsync(string userId)`, `LoginAsOwnerAsync()`, `LoginAsAdminAsync()`, `LoginAsGlobalAdminAsync()`.

- [ ] **Step 1: Write the failing harness tests**

```csharp
// TelegramGroupsAdmin.E2ETests/Tests/Golden/GoldenHarnessTests.cs
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Golden;

/// <summary>
/// Proves the golden E2E path: per-test clone, canonical login, arrange-before-start,
/// isolation between tests, and the shared key ring.
/// </summary>
[TestFixture]
public class GoldenHarnessTests : GoldenE2ETestBase
{
    [Test]
    public async Task CanonicalOwner_SeesTheProfileWithCanonicalEmail()
    {
        await LoginAsOwnerAsync();

        await Page.GotoAsync("/profile");
        await Page.WaitForInteractiveAsync();

        await Expect(Page.GetByText(GoldenDatasetConstants.WebUsers.OwnerEmail).First).ToBeVisibleAsync();
    }

    [Test]
    public async Task CanonicalChats_AreListed()
    {
        string firstChatTitle;
        await using (var ctx = CreateDbContext())
        {
            firstChatTitle = await ctx.ManagedChats.AsNoTracking()
                .Where(c => c.IsActive).OrderBy(c => c.ChatName).Select(c => c.ChatName!).FirstAsync();
        }
        await LoginAsOwnerAsync();

        var chats = new ChatsPage(Page);
        await chats.NavigateAsync();

        await Expect(chats.ChatName(firstChatTitle)).ToBeVisibleAsync();
    }

    [Test]
    public async Task ArrangeDataAsync_RunsBeforeTheAppStarts()
    {
        // ArrangeDataAsync (below) reduced messages to zero for this test only.
        await LoginAsOwnerAsync();

        var home = new HomePage(Page);
        await home.NavigateAsync();
        await home.WaitForLoadAsync();

        await Expect(home.StatValue("Total Messages")).ToHaveTextAsync("0");
    }

    [Test, Order(1)]
    public async Task Isolation_WriteInOneTest()
    {
        await LoginAsOwnerAsync();
        await using var ctx = CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE users SET email = 'isolation-probe@e2e.local' WHERE id = {0}",
            GoldenDatasetConstants.WebUsers.NoTotpAdminId);
        Assert.Pass("wrote a marker; Isolation_NextTestSeesCleanCanonical asserts it is gone");
    }

    [Test, Order(2)]
    public async Task Isolation_NextTestSeesCleanCanonical()
    {
        await using var ctx = CreateDbContext();
        var email = await ctx.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId).Select(u => u.Email).SingleAsync();

        Assert.That(email, Is.EqualTo(GoldenDatasetConstants.WebUsers.NoTotpAdminEmail));
    }

    [Test]
    public async Task SharedKeyRing_AppDecryptsCanonicalApiKeys()
    {
        await LoginAsOwnerAsync();

        using var scope = Factory.Services.CreateScope();
        var configs = scope.ServiceProvider.GetRequiredService<TelegramGroupsAdmin.Configuration.Repositories.ISystemConfigRepository>();
        var apiKeys = await configs.GetApiKeysAsync();

        Assert.That(apiKeys?.OpenAI, Is.EqualTo("sk-canonical-test-key"));
    }

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        if (TestContext.CurrentContext.Test.MethodName == nameof(ArrangeDataAsync_RunsBeforeTheAppStarts))
        {
            await GoldenDataset.Reduce(context).KeepMessages(0).ApplyAsync();
        }
    }
}
```

Second class in the same file, for the empty template (the template is per-class):

```csharp
/// <summary>The Empty template has no web users, so the app is in first-run mode.</summary>
[TestFixture]
public class GoldenHarnessEmptyTemplateTests : GoldenE2ETestBase
{
    protected override GoldenTemplate Template => GoldenTemplate.Empty;

    [Test]
    public async Task EmptyTemplate_RedirectsToFirstRunRegistration()
    {
        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/register"));
    }
}
```

Adjust names to the real API before running: the `ManagedChats` DbSet/columns (`grep -n 'DbSet' TelegramGroupsAdmin.Data/AppDbContext.cs`), `ChatsPage.ChatName`, `ISystemConfigRepository`'s API-key getter and its return type (`grep -n 'ApiKeys' TelegramGroupsAdmin.Configuration/Repositories/ISystemConfigRepository.cs`), `KeepMessages(0)` validity (`GoldenReducePlanBuilder` validation rules — if 0 is rejected, use the allowlist overload with an empty list, or add a `DropAllMessages()` reducer with its own self-test per the data rule). The isolation pair is a deliberate write-as-subject probe (the write *is* what is being tested: that the next clone is clean); keep its comment.

- [ ] **Step 2: Run to verify it fails** — build errors: `GoldenE2ETestBase` not found.

- [ ] **Step 3: Implement**

`E2ETests.csproj`: add `<ProjectReference Include="..\TelegramGroupsAdmin.Testing.Golden\TelegramGroupsAdmin.Testing.Golden.csproj" />`.

`E2EFixture.GlobalSetup`, after `BaseConnectionString` is set:

```csharp
        // Golden templates for GoldenE2ETestBase. The key ring is shared with every golden-test app
        // instance so canonical encrypted columns (configs.api_keys) decrypt in the app under test.
        SharedKeysDirectory = Path.Combine(Path.GetTempPath(), "e2e_tests", $"golden_keys_{Guid.NewGuid():N}");
        Directory.CreateDirectory(SharedKeysDirectory);
        var keyRing = DataProtectionProvider.Create(
            new DirectoryInfo(SharedKeysDirectory), b => b.SetApplicationName("TgSpamPreFilter"));
        await GoldenTemplates.BuildAsync(BaseConnectionString, keyRing);
        Console.WriteLine("Golden templates built");
```
plus `public static string SharedKeysDirectory { get; private set; } = string.Empty;` and deletion of the directory in `GlobalTeardown` (best-effort try/catch like the factory's temp cleanup).

`TestWebApplicationFactory`: new constructor parameters stored in fields; in `EnsureDatabaseCreated`, after creating `_tempDataPath`:

```csharp
        if (_sharedKeysDirectory != null)
        {
            var keysDir = Directory.CreateDirectory(Path.Combine(_tempDataPath, "keys"));
            foreach (var key in Directory.EnumerateFiles(_sharedKeysDirectory, "*.xml"))
            {
                File.Copy(key, Path.Combine(keysDir.FullName, Path.GetFileName(key)), overwrite: true);
            }
        }

        if (_databaseAlreadyExists)
        {
            _databaseCreated = true;
            return;
        }
```
(`Program.cs:39` derives the key path as `Path.Combine(dataPath, "keys")`.)

`E2ETestBase`: replace `Factory = new TestWebApplicationFactory();` with `Factory = await CreateFactoryAsync();`; add the two virtual members; call `await OnFactoryDisposedAsync();` as the last line of `BaseTearDown` (after the factory dispose). The factory still drops its own database in `Dispose`, so golden tests need no extra drop unless setup failed before the factory existed.

`GoldenE2ETestBase`:

```csharp
// TelegramGroupsAdmin.E2ETests/Fixtures/GoldenE2ETestBase.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for E2E tests on canonical golden data. Each test clones a session template,
/// shapes it in <see cref="ArrangeDataAsync"/> (reduce/mutate, precondition read-backs) BEFORE
/// the app starts — so no app cache ever sees the pre-arrangement state — then starts a fresh
/// app instance on the clone. Log in as canonical users; never seed with builders.
/// </summary>
public abstract class GoldenE2ETestBase : E2ETestBase
{
    /// <summary>Template this test's database is cloned from. Override for empty-state tests.</summary>
    protected virtual GoldenTemplate Template => GoldenTemplate.Golden;

    /// <summary>The per-test clone's database name.</summary>
    protected string DatabaseName { get; private set; } = string.Empty;

    /// <summary>
    /// Shape the clone before the app starts: GoldenDataset.Reduce/Mutate and read-back guards
    /// of canonical-edit preconditions. Missing reduce/mutate operations get added to the builders.
    /// </summary>
    protected virtual Task ArrangeDataAsync(AppDbContext context) => Task.CompletedTask;

    /// <summary>A direct context on this test's clone, for arrangement and read-back assertions.</summary>
    protected AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GoldenTemplates.ConnectionStringFor(E2EFixture.BaseConnectionString, DatabaseName))
            .Options);

    protected override async Task<TestWebApplicationFactory> CreateFactoryAsync()
    {
        DatabaseName = E2EFixture.GetUniqueDatabaseName();
        await GoldenTemplates.CloneAsync(E2EFixture.BaseConnectionString, Template, DatabaseName);

        await using (var context = CreateDbContext())
        {
            await ArrangeDataAsync(context);
        }

        return new TestWebApplicationFactory(
            DatabaseName, databaseAlreadyExists: true, sharedKeysDirectory: E2EFixture.SharedKeysDirectory);
    }

    protected override async Task OnFactoryDisposedAsync()
    {
        // The factory drops its database on dispose; this covers a setup that failed before
        // the factory existed (e.g. ArrangeDataAsync threw). DROP … IF EXISTS makes it idempotent.
        if (DatabaseName.Length > 0)
        {
            await GoldenTemplates.DropAsync(E2EFixture.BaseConnectionString, DatabaseName);
        }
    }

    protected Task LoginAsOwnerAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.OwnerId);
    protected Task LoginAsAdminAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.AdminId);
    protected Task LoginAsGlobalAdminAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.GlobalAdminId);

    /// <summary>
    /// Injects the app's own auth cookie for an existing canonical web user (no SUT writes).
    /// Uses the user's stored security stamp so UserSessionValidator accepts the session.
    /// </summary>
    protected async Task LoginAsCanonicalAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdAsync(userId)
            ?? throw new InvalidOperationException($"Canonical user {userId} not found in {DatabaseName}");

        var cookies = scope.ServiceProvider.GetRequiredService<IAuthCookieService>();
        var value = cookies.GenerateCookieValue(
            new WebUserIdentity(user.Id, user.Email, user.PermissionLevel), user.SecurityStamp);
        var host = new Uri(Factory.ServerAddress).Host;

        await Context.AddCookiesAsync([
            new Cookie
            {
                Name = cookies.CookieName,
                Value = value,
                Domain = host,
                Path = "/",
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteAttribute.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
            }
        ]);
    }
}
```

Note: `BaseSetUp` must handle `CreateFactoryAsync` throwing (it already guards `Factory != null` in TearDown); verify `OnFactoryDisposedAsync` runs even when `Factory` is null — move the call outside any `if (Factory != null)`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.E2ETests --filter "FullyQualifiedName~GoldenHarnessTests"` → 7 passed (6 + the empty-template test).

- [ ] **Step 5: Prove the tests can fail** (revert each after confirming):
  - Remove the key-copy block → `SharedKeyRing_AppDecryptsCanonicalApiKeys` fails (null).
  - Make `CreateFactoryAsync` reuse one fixed `DatabaseName` and skip drop → isolation pair fails (or clone fails because the db exists).
  - Move `ArrangeDataAsync` after `StartServer` (or drop the reduce) → `ArrangeDataAsync_RunsBeforeTheAppStarts` fails.

- [ ] **Step 6: Setup-failure cleanup check** — temporarily make `ArrangeDataAsync` throw for one test, run it, confirm it fails with the thrown message and that `SELECT datname FROM pg_database WHERE datname LIKE 'e2e_test_%'` (via a scratch `psql`/script against the container, or a follow-on test querying `pg_database`) shows no leftover clone after the run. Revert.

- [ ] **Step 7: Full E2E suite** — `dotnet test TelegramGroupsAdmin.E2ETests` → 292 + 7 passed, 0 failed (existing tests unaffected by the hook).

- [ ] **Step 8: Commit** (`test(e2e): add GoldenE2ETestBase — per-test golden/empty clones with a fresh app`)

---

### Task 5: App-side changes

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/LoginVerify.razor` (remove `_successMessage` field, both `= null` assignments, and the `alert-success` block)
- Modify: `TelegramGroupsAdmin/Components/Pages/Users.razor` (trust/untrust icon button `aria-label`), `TelegramGroupsAdmin/Components/Shared/UserInfoCell.razor` (Trusted / Admin badge `aria-label`)
- Test: `TelegramGroupsAdmin.ComponentTests/Components/...UserInfoCellTests.cs` (existing file if present: `git ls-files | grep -i UserInfoCellTests`)

- [ ] **Step 1: Failing component test** — in `UserInfoCellTests`, render a trusted user and assert `cut.Find("[aria-label='Trusted user']")` exists; render an admin and assert `[aria-label='Telegram chat admin']` (read `UserInfoCell.razor:19-30` for the exact badge semantics and word the labels accordingly). Run → fails (no element).
- [ ] **Step 2: Add the `aria-label`s** to the badge elements in `UserInfoCell.razor`, and to the trust toggle `MudIconButton` in `Users.razor:79-83` (`aria-label="@(user.IsTrusted ? "Untrust user" : "Trust user")"` — MudBlazor passes unmatched attributes through to the button). Run → passes.
- [ ] **Step 3: Remove the dead `_successMessage` block** from `LoginVerify.razor` (lines ~20-22, field ~118, assignments ~161 and ~215). Build; run `dotnet test TelegramGroupsAdmin.ComponentTests` and the E2E `RecoveryCodesTests`/`TwoFactorTests` fixtures → green.
- [ ] **Step 4: Two commits** — `fix(a11y): give the Users page trust toggle and user badges accessible names` and `refactor(auth): drop the never-set success message from the 2FA verify page`.

---

### Task 6: New coverage — high value (one commit per area)

Written by a Fable agent per area, **sequentially** (E2E runs must not overlap). Each area: new test class(es) deriving `GoldenE2ETestBase` under `TelegramGroupsAdmin.E2ETests/Tests/<Area>/`, page-object fixes noted, mutation check of every new assertion (report results), data rule from Global Constraints, commit `test(e2e): cover <behaviour> on golden data`.

Per area, before writing tests, **mine canonical** for the anchor (`grep -n` the relevant `TelegramGroupsAdmin.Testing.Golden/SQL/canonical/NN_*.sql` file), confirm no test or doc references it if it will be flag-edited (`grep -rn <id> TelegramGroupsAdmin.*Tests docs`), and add any new anchor to `GoldenDatasetConstants` with an XML doc plus a read-back assertion in `ArrangeDataAsync`.

**6a. Web Admin Accounts — per-user actions** (`Tests/Settings/WebAdminAccountsGoldenTests.cs`)
- Fix `WebAdminAccountsPage.ActionMenuButton`: target `UserRow(email).Locator("td[data-label='Actions'] button")` (MudBlazor 9.9 never renders `data-testid`); keep `OpenActionMenuForUserAsync`, `ClickActionMenuItemAsync`, `ConfirmDialogAsync`, `CancelDialogAsync`, `UserPermissionChip`, `UserTotpIcon`, `UserLockedChip` and use them.
- Tests (as Owner, against canonical `reshoot@` / other active non-owner users mined from `01_users.sql`): disable → status chip shows Disabled and DB `status` changed; cancel disable → unchanged; enable; delete → row leaves default filter; restore; edit permission (Admin → GlobalAdmin) → chip updates; TOTP actions on a TOTP-enabled user (`ahead@`): disable TOTP → icon changes; reset TOTP; unlock a locked user (flag-edit an unreferenced canonical user to locked state — `locked_until` in the future — if none exists).

**6b. Invite creation and registration with invite** (`Tests/Settings/InviteGoldenTests.cs`)
- Fix `CreateInviteDialogButton` → "Generate Invite"; `InviteLinkText` → `GetByLabel("Invite Link")` asserted with `ToHaveValueAsync(new Regex(@"register\?invite="))`; `ValidDaysInput` → `GetByLabel("Invite valid for (days)")`.
- Tests: Owner creates an invite → link field value + "Invite created successfully" snackbar; GlobalAdmin sees only allowed permission options; register via the generated link (non-first-run canonical) → account created → resolve the TODO in `RegistrationTests` (~:126-133) by pointing it at this test.

**6c. Reports Warn** (`Tests/Reports/ReportsGoldenTests.cs`) — canonical pending moderation report (mine `30_reports.sql`); click Warn (`ReportsPage.ClickWarnAsync`) → report leaves Pending, snackbar, and a `user_actions` warn row exists for that user (read-back via `CreateDbContext`).

**6d. Audit Log filters** (`Tests/Audit/AuditLogGoldenTests.cs`) — fix `FilterByTelegramUserIdAsync` / `FilterByIssuedByAsync` to commit the value (press Enter or blur; the fields are non-`Immediate`); tests: filter moderation log by a canonical telegram user id → every row's user cell matches and count equals the DB count for that user (read at runtime); filter by issued-by → `ModerationEntryWithIssuedBy` rows only.

**6e. Content Detection** (`Tests/Settings/ContentDetectionGoldenTests.cs`) — fix `FillAndSubmitAddSampleDialogAsync` 'Ham' → 'CLEAN'; add training sample (spam and clean) → appears via `TrainingSample(text)`/`TrainingSampleRows`; Source filter (`SelectSourceFilterAsync`) narrows rows to the chosen source (expected rows read from DB); training-mode toggle → save → reload → persisted (and the "Training Mode Active" chip).

**6f. Ban Celebrations** (`Tests/Settings/BanCelebrationGoldenTests.cs`) — fix `NameInput` → `Dialog.GetByLabel("Name (optional)")`; add caption → `CaptionRows` contains it + "Caption added successfully"; upload a GIF with a name → `GifRow(name)`; upload the same GIF again → duplicate warning (`WaitForDuplicateWarningAsync`) → Cancel Upload keeps count / Keep Both adds one; add from URL (`EnterUrlAsync`) using a URL served by the test app itself (e.g. a static file under the app's wwwroot) — if no such file exists, report and skip this one test rather than add an external dependency.

**6g. Background Jobs** — cancel in the job config dialog (`CancelJobConfigDialogAsync`) leaves the schedule unchanged (value read before/after from the table).

**6h. Users page** (`Tests/Users/UsersGoldenTests.cs`, after Task 5) — rewrite `TrustButton`/`TrustedIndicator`/`AdminIndicator` as role/label locators (`GetByRole(AriaRole.Button, new() { Name = "Trust user" })`, `GetByLabel("Trusted user")`…); trust a canonical untrusted user → badge appears + DB `is_trusted`; badges for a canonical trusted user and a canonical chat admin; View Details opens the dialog (`ClickViewDetailsAsync`, `Dialog`, `CloseDialogAsync`); tab badges (`TabBadge`) equal DB counts per tab; warnings cell (`WarningsCell`) shows the canonical warning count for a warned user on Active.

**6i. Analytics** — Content Detection tab renders its content: assert a specific section heading (read `ContentDetectionAnalytics.razor`) with a tight locator; replace `ActiveTabContent` with it. Use `Mutate` re-time so date-windowed numbers are non-empty, and assert one number against a DB-derived value.

---

### Task 7: New coverage — lower priority (one commit per group)

Same rules as Task 6.

- **Reports exam chips:** `ExamScoreChip` text matches the canonical exam result's score ("X/Y correct (Z%)" from DB); `ExamMcPassedChip` scoped to the Multiple Choice section (fix the selector so it cannot match "Passed — auto-admitted" or the AI chip) for an above-threshold canonical exam.
- **Chats:** `ChatIdCaption` in `Chats_SearchByIdWorks`-style golden test; `BotStatusChip` for a canonical chat; `ClickRefreshHealthAsync` → health-check job scheduled (assert via the factory's `IJobScheduler` mock received a call); `InactiveChip` for a canonical inactive chat (flag-edit if none).
- **Login page links:** `ClickForgotPasswordAsync` → `/forgot-password`; `ClickResendVerificationAsync` → `/resend-verification`; register/forgot/reset "Sign in" links (`SignInLink` consts on ForgotPasswordPage/ResetPasswordPage, `ClickSignInLinkAsync`) → `/login`.
- **Register warning:** "email verification disabled" warning (`WarningAlert`) shown on the non-first-run register page when SendGrid is disabled in canonical config.
- **Messages:** `ChatLastMessage` preview for a canonical chat equals its latest message (truncated per component rule); `ClickBackButtonAsync` under a mobile viewport (`Page.SetViewportSizeAsync(390, 844)`) returns to the chat list.

---

### Task 8: Close-out

- [ ] **Step 1: Delete members still unreferenced** — run the orphan check from this branch (private consts referenced once in their file; public page-object members with no references outside their file) and delete what it reports; build.
- [ ] **Step 2: Full suites** — `dotnet test TelegramGroupsAdmin.IntegrationTests`, `dotnet test TelegramGroupsAdmin.ComponentTests`, `dotnet test TelegramGroupsAdmin.UnitTests`, `dotnet test TelegramGroupsAdmin.E2ETests` → all green; record counts.
- [ ] **Step 3: Commit** (`refactor(e2e): remove page-object members left unused after the coverage pass`) and report the branch summary (commits, counts, any skipped item with reason).

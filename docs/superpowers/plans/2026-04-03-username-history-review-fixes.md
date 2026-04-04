# Username History Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Address 8 code review findings from the `feat/username-history` branch before PR to `develop`.

**Architecture:** All changes are surgical fixes to existing files — no new files, no new migrations. The largest structural change is moving the name history query from the Razor component into the service layer.

**Tech Stack:** Blazor Server, MudBlazor 9, EF Core 10, NUnit, bUnit

---

### Task 1: Add `Actor.ProfileDiffDetection` static field and update call site

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Models/Actor.cs:57`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs:696`

- [ ] **Step 1: Add the static field to Actor.cs**

Add after the `Bootstrap` field (line 57):

```csharp
public static readonly Actor ProfileDiffDetection = FromSystem("profile_diff_detection");
```

- [ ] **Step 2: Update the call site in MessageProcessingService.cs**

Replace line 696:
```csharp
IssuedBy: Actor.FromSystem("profile_diff_detection"),
```
with:
```csharp
IssuedBy: Actor.ProfileDiffDetection,
```

- [ ] **Step 3: Verify build**

Run: `dotnet build TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/Actor.cs TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs
git commit -m "fix: add Actor.ProfileDiffDetection static field, remove magic string"
```

---

### Task 2: Fix `UsernameHistoryRepository` to materialize before mapping

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/UsernameHistoryRepository.cs:31-36`

- [ ] **Step 1: Update GetByUserIdAsync to materialize before mapping**

Replace the return statement (lines 31-36):
```csharp
        return await context.UsernameHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .Select(h => h.ToModel())
            .ToListAsync(cancellationToken);
```
with:
```csharp
        var dtos = await context.UsernameHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .ToListAsync(cancellationToken);

        return dtos.Select(h => h.ToModel()).ToList();
```

- [ ] **Step 2: Verify build**

Run: `dotnet build TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 3: Run existing integration tests to confirm no regression**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "UsernameHistoryRepository" --verbosity normal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Repositories/UsernameHistoryRepository.cs
git commit -m "fix: materialize query before ToModel() mapping in UsernameHistoryRepository"
```

---

### Task 3: Clean up `UsernameHistoryDto` — remove `virtual` and redundant `[ForeignKey]`

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/UsernameHistoryDto.cs:35-37`

- [ ] **Step 1: Remove virtual keyword and ForeignKey attribute**

Replace lines 35-37:
```csharp
    // Navigation
    [ForeignKey(nameof(UserId))]
    public virtual TelegramUserDto? User { get; set; }
```
with:
```csharp
    // Navigation
    public TelegramUserDto? User { get; set; }
```

- [ ] **Step 2: Verify build**

Run: `dotnet build TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj`
Expected: Build succeeded

- [ ] **Step 3: Verify no migration diff is generated**

Run: `dotnet ef migrations has-pending-model-changes -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`
Expected: No pending model changes (Fluent API already configures the FK)

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/UsernameHistoryDto.cs
git commit -m "fix: remove virtual keyword and redundant ForeignKey attribute from UsernameHistoryDto"
```

---

### Task 4: Fix index naming convention in AppDbContext

**Files:**
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs:647-658`

- [ ] **Step 1: Rename indexes to snake_case convention**

Replace lines 647-658:
```csharp
        // UsernameHistory indexes
        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.UserId)
            .HasDatabaseName("IX_username_history_user_id");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.Username)
            .HasDatabaseName("IX_username_history_username_lower");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => new { h.FirstName, h.LastName })
            .HasDatabaseName("IX_username_history_name_lower");
```
with:
```csharp
        // UsernameHistory indexes
        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.UserId)
            .HasDatabaseName("ix_username_history_user_id");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => h.Username)
            .HasDatabaseName("ix_username_history_username");

        modelBuilder.Entity<UsernameHistoryDto>()
            .HasIndex(h => new { h.FirstName, h.LastName })
            .HasDatabaseName("ix_username_history_name");
```

- [ ] **Step 2: Generate migration for the index renames**

Run: `dotnet ef migrations add RenameUsernameHistoryIndexes -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`

- [ ] **Step 3: Review the generated migration**

Open the generated migration file. It should contain `RenameIndex` operations only — no table drops/creates. Verify it looks like:
```csharp
migrationBuilder.RenameIndex(
    name: "IX_username_history_user_id",
    table: "username_history",
    newName: "ix_username_history_user_id");
// ... similar for the other two
```

If EF Core generated DROP+CREATE instead of RENAME, manually fix the migration to use `RenameIndex`.

- [ ] **Step 4: Verify build**

Run: `dotnet build TelegramGroupsAdmin.Data/TelegramGroupsAdmin.Data.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Data/AppDbContext.cs TelegramGroupsAdmin.Data/Migrations/
git commit -m "fix: rename username_history indexes to snake_case convention"
```

---

### Task 5: Move name history loading from Razor into service layer

This is the largest change — moves `IUsernameHistoryRepository` out of the Razor component and behind `ITelegramUserManagementService`.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/TelegramUserDetail.cs:48`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ITelegramUserManagementService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/TelegramUserManagementService.cs`
- Modify: `TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor:19,591,629`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogTests.cs`

- [ ] **Step 1: Add NameHistory to TelegramUserDetail model**

In `TelegramGroupsAdmin.Telegram/Models/TelegramUserDetail.cs`, add after the Tags property (line 48):

```csharp
    public List<UsernameHistoryRecord> NameHistory { get; set; } = [];
```

- [ ] **Step 2: Add GetNameHistoryAsync to ITelegramUserManagementService**

In `TelegramGroupsAdmin.Telegram/Services/ITelegramUserManagementService.cs`, add before the closing brace:

```csharp
    /// <summary>Gets the username/name change history for a user.</summary>
    Task<List<UsernameHistoryRecord>> GetNameHistoryAsync(long telegramUserId, CancellationToken cancellationToken = default);
```

Add the using at the top if not present:
```csharp
using TelegramGroupsAdmin.Telegram.Models;
```

- [ ] **Step 3: Implement GetNameHistoryAsync in TelegramUserManagementService**

In `TelegramGroupsAdmin.Telegram/Services/TelegramUserManagementService.cs`:

Add `IUsernameHistoryRepository` to the constructor:
```csharp
    private readonly IUsernameHistoryRepository _usernameHistoryRepository;

    public TelegramUserManagementService(
        ITelegramUserRepository userRepository,
        IUserActionsRepository userActionsRepository,
        IUsernameHistoryRepository usernameHistoryRepository,
        ILogger<TelegramUserManagementService> logger)
    {
        _userRepository = userRepository;
        _userActionsRepository = userActionsRepository;
        _usernameHistoryRepository = usernameHistoryRepository;
        _logger = logger;
    }
```

Add the method implementation:
```csharp
    /// <inheritdoc/>
    public Task<List<UsernameHistoryRecord>> GetNameHistoryAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        return _usernameHistoryRepository.GetByUserIdAsync(telegramUserId, cancellationToken);
    }
```

Add the using at the top:
```csharp
using TelegramGroupsAdmin.Telegram.Repositories;
```

- [ ] **Step 4: Update UserDetailDialog.razor — remove repository injection and use service**

In `TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor`:

Remove line 19:
```razor
@inject IUsernameHistoryRepository UsernameHistoryRepo
```

Replace the `_nameHistory` field declaration (line 591):
```csharp
    private List<UsernameHistoryRecord> _nameHistory = [];
```
with:
```csharp
    private List<UsernameHistoryRecord> _nameHistory = [];
```
(This stays the same — but now it's populated differently.)

Replace line 629:
```csharp
            _nameHistory = await UsernameHistoryRepo.GetByUserIdAsync(UserId);
```
with:
```csharp
            _nameHistory = await UserManagementService.GetNameHistoryAsync(UserId);
```

- [ ] **Step 5: Update component tests — remove IUsernameHistoryRepository mock, use service mock**

In `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs`:

Remove the `_mockHistoryRepo` field (line 28):
```csharp
    private IUsernameHistoryRepository _mockHistoryRepo = null!;
```

In the constructor, remove:
```csharp
        _mockHistoryRepo = Substitute.For<IUsernameHistoryRepository>();
```
and:
```csharp
        Services.AddSingleton(_mockHistoryRepo);
```
and:
```csharp
        // Default: no history entries
        _mockHistoryRepo.GetByUserIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
```

Instead, add to the existing `_mockUserService` setup area:
```csharp
        _mockUserService.GetNameHistoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
```

In the `[SetUp]` method, replace:
```csharp
        _mockHistoryRepo.GetByUserIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
```
with:
```csharp
        _mockUserService.GetNameHistoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
```

In each test that sets up history records, replace `_mockHistoryRepo.GetByUserIdAsync(...)` with `_mockUserService.GetNameHistoryAsync(...)`. There are 3 tests to update:
- `NameHistoryPanel_RenderedCollapsed_WhenHistoryExists` (line 150)
- `NameHistoryPanel_ShowsCorrectData_WhenExpanded` (line 180)
- `NameHistoryPanel_HandlesNullUsername` (line 212)

In `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogTests.cs`:

Remove the `IUsernameHistoryRepository` mock registration if present. Add `GetNameHistoryAsync` default to the `_mockUserService` setup:
```csharp
        _mockUserService.GetNameHistoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<UsernameHistoryRecord>()));
```

- [ ] **Step 6: Verify build and tests**

Run: `dotnet build`
Expected: Build succeeded

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "UserDetailDialog" --verbosity normal`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Models/TelegramUserDetail.cs \
  TelegramGroupsAdmin.Telegram/Services/ITelegramUserManagementService.cs \
  TelegramGroupsAdmin.Telegram/Services/TelegramUserManagementService.cs \
  TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor \
  TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs \
  TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogTests.cs
git commit -m "refactor: move name history loading from Razor into TelegramUserManagementService"
```

---

### Task 6: Fix date formatting and expansion panel styling in UserDetailDialog

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor:431,449`

- [ ] **Step 1: Fix expansion panel styling to match sibling sections**

Replace lines 431-455:
```razor
                    <MudExpansionPanels Class="mt-4" Elevation="0">
                        <MudExpansionPanel Text="@($"Name History ({_nameHistory.Count})")"
                                           Icon="@Icons.Material.Filled.History"
                                           Expanded="false">
                            <MudSimpleTable Dense="true" Hover="true" Striped="true">
                                <thead>
                                    <tr>
                                        <th>Username</th>
                                        <th>Name</th>
                                        <th>Date</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    @foreach (var entry in _nameHistory)
                                    {
                                        <tr>
                                            <td>@(entry.Username != null ? $"@{entry.Username}" : "(no username)")</td>
                                            <td>@FormatName(entry.FirstName, entry.LastName)</td>
                                            <td>@entry.RecordedAt.LocalDateTime.ToString("MMM d, yyyy")</td>
                                        </tr>
                                    }
                                </tbody>
                            </MudSimpleTable>
                        </MudExpansionPanel>
                    </MudExpansionPanels>
```
with:
```razor
                    <MudExpansionPanels Class="mb-4" Elevation="1">
                        <MudExpansionPanel Text="@($"Name History ({_nameHistory.Count})")"
                                           Icon="@Icons.Material.Filled.History"
                                           Expanded="false">
                            <MudSimpleTable Dense="true" Hover="true" Striped="true">
                                <thead>
                                    <tr>
                                        <th>Username</th>
                                        <th>Name</th>
                                        <th>Date</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    @foreach (var entry in _nameHistory)
                                    {
                                        <tr>
                                            <td>@(entry.Username != null ? $"@{entry.Username}" : "(no username)")</td>
                                            <td>@FormatName(entry.FirstName, entry.LastName)</td>
                                            <td><LocalTimestamp Value="@entry.RecordedAt" /></td>
                                        </tr>
                                    }
                                </tbody>
                            </MudSimpleTable>
                        </MudExpansionPanel>
                    </MudExpansionPanels>
```

Changes: `Class="mt-4"` → `Class="mb-4"`, `Elevation="0"` → `Elevation="1"`, date column uses `<LocalTimestamp>`.

- [ ] **Step 2: Update the component test that asserts on the date format**

In `TelegramGroupsAdmin.ComponentTests/Components/UserDetailDialogHistoryTests.cs`, the test `NameHistoryPanel_ShowsCorrectData_WhenExpanded` (line 193) asserts `Does.Contain("Jun")`. Since `<LocalTimestamp>` renders differently than `.LocalDateTime.ToString("MMM d, yyyy")`, update the assertion to check for the recorded date content that `<LocalTimestamp>` will produce. The `<LocalTimestamp>` component renders the UTC value formatted for the user's timezone — in tests the cascading `TimeZoneInfo` defaults to UTC, so the output will still contain "Jun" and "2024". Keep the assertion as-is since the component test environment uses UTC.

No change needed to the test — `<LocalTimestamp>` in UTC timezone with `2024-06-15T12:00:00Z` will still render with "Jun".

- [ ] **Step 3: Verify build and component tests**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "UserDetailDialogHistory" --verbosity normal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/UserDetailDialog.razor
git commit -m "fix: use LocalTimestamp for name history dates and match sibling panel styling"
```

---

### Task 7: Update search placeholder text

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/Users.razor:23`

- [ ] **Step 1: Update placeholder text**

Replace line 23:
```razor
                      Placeholder="Search by name, username, ID, or past names..."
```
with:
```razor
                      Placeholder="Search by name, username, ID, or name history..."
```

- [ ] **Step 2: Commit**

```bash
git add TelegramGroupsAdmin/Components/Pages/Users.razor
git commit -m "fix: clarify search placeholder to say 'name history'"
```

---

### Task 8: Add integration tests for SearchByNameAsync past-name subquery

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs`

- [ ] **Step 1: Add test for SearchByNameAsync matching past username**

Add to the `#region Search with Username History Tests` section:

```csharp
    [Test]
    public async Task SearchByNameAsync_MatchesPastUsername()
    {
        // Seed an active user with current username "current_handle"
        const long userId = 200010L;
        await SeedActiveUserAsync(userId, username: "current_handle", firstName: "Search", lastName: "Test");

        // Insert a history entry recording the old username
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "legacy_handle", "Search", "Test");
        }

        // Search by old username — should find the user via username_history subquery
        var results = await _repository!.SearchByNameAsync("legacy_handle", limit: 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
    }
```

- [ ] **Step 2: Add test for SearchByNameAsync matching past first name**

```csharp
    [Test]
    public async Task SearchByNameAsync_MatchesPastFirstName()
    {
        const long userId = 200011L;
        await SeedActiveUserAsync(userId, username: "name_search_user", firstName: "CurrentFirst", lastName: "Test");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userId, "name_search_user", "FormerFirst", "Test");
        }

        var results = await _repository!.SearchByNameAsync("FormerFirst", limit: 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
    }
```

- [ ] **Step 3: Add negative/isolation test — past name search does not bleed across users**

```csharp
    [Test]
    public async Task GetPagedUsersAsync_SearchByPastName_DoesNotReturnDifferentUser()
    {
        // Seed two users
        const long userA = 200020L;
        const long userB = 200021L;
        await SeedActiveUserAsync(userA, username: "user_a", firstName: "Alice", lastName: "Smith");
        await SeedActiveUserAsync(userB, username: "user_b", firstName: "Bob", lastName: "Jones");

        // Only userA has a history entry with the distinctive name
        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userA, "unique_old_handle", "Alice", "Smith");
        }

        // Search by userA's old username — should NOT return userB
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 10,
            searchText: "unique_old_handle", chatIds: null,
            sortLabel: null, sortDescending: false);

        Assert.Multiple(() =>
        {
            Assert.That(totalCount, Is.EqualTo(1));
            Assert.That(items[0].TelegramUserId, Is.EqualTo(userA));
        });
    }
```

- [ ] **Step 4: Add negative/isolation test for SearchByNameAsync**

```csharp
    [Test]
    public async Task SearchByNameAsync_DoesNotReturnUserWithoutMatchingHistory()
    {
        // Seed two users — only userA has a matching history entry
        const long userA = 200030L;
        const long userB = 200031L;
        await SeedActiveUserAsync(userA, username: "iso_user_a", firstName: "Ava", lastName: "Test");
        await SeedActiveUserAsync(userB, username: "iso_user_b", firstName: "Ben", lastName: "Test");

        await using (var scope = _serviceProvider!.CreateAsyncScope())
        {
            var historyRepo = scope.ServiceProvider.GetRequiredService<IUsernameHistoryRepository>();
            await historyRepo.InsertAsync(userA, "distinctive_old_name", "Ava", "Test");
        }

        var results = await _repository!.SearchByNameAsync("distinctive_old_name", limit: 10);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].TelegramUserId, Is.EqualTo(userA));
        });
    }
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "TelegramUserRepositoryTests" --verbosity normal`
Expected: All tests pass (existing + 4 new)

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserRepositoryTests.cs
git commit -m "test: add integration tests for SearchByNameAsync past-name subquery and isolation"
```

---

### Task 9: Final verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test --verbosity normal`
Expected: All 3,757+ tests pass, 0 failures

- [ ] **Step 2: Verify migration applies cleanly**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: Migration applies without error, process exits cleanly
